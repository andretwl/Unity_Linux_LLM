using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EditorAttributes;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Loads, caches, and refreshes <see cref="PlayerDialogueContext"/>
    /// for the authenticated player, merging persistent Supabase data
    /// with local NPCEvidenceState.
    ///
    /// Call <see cref="GetOrLoadContextAsync"/> before each dialogue
    /// session to get the enriched player context for prompt building.
    /// The result is cached by NPC slug and invalidated on state-change
    /// events.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public class PlayerDialogueContextService : MonoBehaviour
    {
        [FoldoutGroup("References", true, nameof(_authService), nameof(_evidenceState))]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [SerializeField, HideProperty]
        PlayerAuthService _authService;

        [SerializeField, HideProperty]
        NPCEvidenceState _evidenceState;

        [FoldoutGroup("Behaviour", true, nameof(_enableServerContext))]
        [SerializeField]
        EditorAttributes.Void behaviourGroup;

        [SerializeField, HideProperty, Tooltip(
            "When true (default), loads player context from the Supabase "
            + "get_player_dialogue_context RPC. When false, only local "
            + "NPCEvidenceState is used."
        )]
        bool _enableServerContext = true;

        [FoldoutGroup("Debug", true, nameof(_currentNpcSlug), nameof(_cachedContext))]
        [SerializeField]
        EditorAttributes.Void debugGroup;

        [SerializeField, HideProperty, ReadOnly]
        string _currentNpcSlug = string.Empty;

        [SerializeField, HideProperty, ReadOnly]
        string _cachedContext = string.Empty;

        // ── State ────────────────────────────────────────────────────

        readonly Dictionary<string, PlayerDialogueContext> _contextCache =
            new Dictionary<string, PlayerDialogueContext>(StringComparer.OrdinalIgnoreCase);

        PlayerDialogueContext _lastLoadedContext;
        NPCFlowLogger _logger;

        // ── Public Events ────────────────────────────────────────────

        /// <summary>
        /// Fired when the context for any NPC slug is refreshed.
        /// </summary>
        public event Action<string, PlayerDialogueContext> OnContextRefreshed;

        // ── Lifecycle ──────────────────────────────────────────────

        void Awake()
        {
            _logger = NPCFlowLogger.FindOrCreate();
        }

        void Reset()
        {
            _authService = GetComponent<PlayerAuthService>();
            if (_authService == null)
                _authService = FindAnyObjectByType<PlayerAuthService>(FindObjectsInactive.Include);

            _evidenceState = GetComponent<NPCEvidenceState>();
            if (_evidenceState == null)
                _evidenceState = FindAnyObjectByType<NPCEvidenceState>(FindObjectsInactive.Include);
        }

        // ── Public API ──────────────────────────────────────────────

        /// <summary>
        /// Get (or load from cache) the player dialogue context for a
        /// given NPC slug.  The first call per NPC slug fetches from
        /// Supabase (if enabled) and merges with local evidence state.
        /// Subsequent calls return the cached value until
        /// <see cref="InvalidateContext"/> is called.
        /// </summary>
        public async Task<PlayerDialogueContext> GetOrLoadContextAsync(
            string npcSlug,
            bool forceRefresh = false
        )
        {
            string key = npcSlug ?? "__global__";

            if (!forceRefresh && _contextCache.TryGetValue(key, out PlayerDialogueContext cached))
            {
                _currentNpcSlug = npcSlug;
                _cachedContext = cached.BuildPromptBlock(npcSlug);
                return cached;
            }

            _currentNpcSlug = npcSlug;
            PlayerDialogueContext ctx = await LoadContextInternalAsync(npcSlug);
            _contextCache[key] = ctx;
            _lastLoadedContext = ctx;
            _cachedContext = ctx.BuildPromptBlock(npcSlug);

            _logger?.Log(
                NPCFlowStage.ContextRetrieval,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Debug,
                $"Player context loaded for NPC '{npcSlug ?? "<global>"}' (server={_enableServerContext}).",
                source: nameof(PlayerDialogueContextService),
                data: new Dictionary<string, object>
                {
                    ["npcSlug"] = npcSlug ?? string.Empty,
                    ["trustScore"] = ctx.TrustScore,
                    ["mood"] = ctx.CurrentMood,
                    ["clueCount"] = ctx.KnownClues.Count,
                    ["itemCount"] = ctx.Inventory.Count,
                    ["loadedFromServer"] = ctx.LoadedFromServer,
                }
            );

            OnContextRefreshed?.Invoke(npcSlug, ctx);
            return ctx;
        }

        /// <summary>
        /// Clear the cached context for all NPC slugs so the next call
        /// to <see cref="GetOrLoadContextAsync"/> re-fetches.
        /// </summary>
        public void InvalidateAllContexts()
        {
            _contextCache.Clear();
            _currentNpcSlug = string.Empty;
            _cachedContext = string.Empty;
        }

        /// <summary>
        /// Clear the cached context for a specific NPC slug.
        /// Call after a state-change event (new clue, item, trust update).
        /// </summary>
        public void InvalidateContext(string npcSlug)
        {
            string key = npcSlug ?? "__global__";
            _contextCache.Remove(key);

            if (string.Equals(_currentNpcSlug, npcSlug, StringComparison.OrdinalIgnoreCase))
            {
                _cachedContext = string.Empty;
            }
        }

        // ── Internal ────────────────────────────────────────────────

        async Task<PlayerDialogueContext> LoadContextInternalAsync(string npcSlug)
        {
            string playerId = _authService?.CurrentSession?.playerId ?? string.Empty;
            string playerName = AuthNetworkBridge.ActivePlayerName;

            // 1. Start with local evidence state (always available)
            PlayerDialogueContext localContext = PlayerDialogueContext.FromLocalState(
                playerName,
                playerId,
                npcSlug,
                _evidenceState
            );

            // 2. Try to hydrate from Supabase for persistent state
            if (_enableServerContext && _authService != null && _authService.IsAuthenticated)
            {
                try
                {
                    PlayerDialogueContext serverContext = await FetchFromServerAsync(
                        playerId, npcSlug, playerName
                    );
                    return Merge(localContext, serverContext);
                }
                catch (Exception ex)
                {
                    _logger?.Log(
                        NPCFlowStage.ContextRetrieval,
                        NPCFlowStatus.Warning,
                        NPCFlowLogLevel.Warning,
                        $"Failed to load server context for NPC '{npcSlug}': {ex.Message}. Using local state only.",
                        source: nameof(PlayerDialogueContextService)
                    );
                }
            }

            return localContext;
        }

        async Task<PlayerDialogueContext> FetchFromServerAsync(
            string playerId,
            string npcSlug,
            string playerName
        )
        {
            var client = _authService.SupabaseClient;
            if (client == null)
                throw new InvalidOperationException("Supabase client is not initialized.");

            JObject raw = await client.Rpc<JObject>(
                "get_player_dialogue_context",
                new Dictionary<string, object>
                {
                    ["p_user_id"] = Guid.Parse(playerId),
                    ["p_npc_slug"] = npcSlug ?? (object)DBNull.Value,
                }
            );

            if (raw == null)
                throw new InvalidOperationException("RPC returned null.");

            return new PlayerDialogueContext(
                playerName: raw.Value<string>("player_name") ?? playerName,
                playerId: playerId,
                trustScore: raw.Value<int>("trust_score"),
                currentMood: raw.Value<string>("current_mood") ?? "neutral",
                dialogueCount: raw.Value<int>("dialogue_count"),
                knownClues: raw["clues"]?.ToObject<string[]>(),
                inventory: raw["items"]?.ToObject<string[]>(),
                visitedLocations: raw["locations"]?.ToObject<string[]>(),
                loadedFromServer: true
            );
        }

        static PlayerDialogueContext Merge(
            PlayerDialogueContext local,
            PlayerDialogueContext server
        )
        {
            // Server wins for trust/mood/dialogue count (persistent across sessions).
            // Local wins for clues/items/locations discovered *this session*
            // that haven't been persisted yet.  We merge both.
            var mergedClues = new HashSet<string>();
            foreach (var c in server.KnownClues) mergedClues.Add(c);
            foreach (var c in local.KnownClues) mergedClues.Add(c);

            var mergedItems = new HashSet<string>();
            foreach (var i in server.Inventory) mergedItems.Add(i);
            foreach (var i in local.Inventory) mergedItems.Add(i);

            var mergedLocations = new HashSet<string>();
            foreach (var l in server.VisitedLocations) mergedLocations.Add(l);
            foreach (var l in local.VisitedLocations) mergedLocations.Add(l);

            return new PlayerDialogueContext(
                playerName: !string.IsNullOrWhiteSpace(server.PlayerName)
                    ? server.PlayerName
                    : local.PlayerName,
                playerId: !string.IsNullOrWhiteSpace(server.PlayerId)
                    ? server.PlayerId
                    : local.PlayerId,
                trustScore: server.TrustScore,
                currentMood: server.CurrentMood,
                dialogueCount: server.DialogueCount,
                knownClues: mergedClues,
                inventory: mergedItems,
                visitedLocations: mergedLocations,
                loadedFromServer: true
            );
        }

        // ── Editor Validation ───────────────────────────────────────

        [Button("Validate Settings")]
        void ValidateSettings()
        {
            bool hasAuth = _authService != null;
            var authStatus = hasAuth
                ? $"Auth: {_authService.CurrentSession?.username ?? "<no session>"}"
                : "Missing PlayerAuthService reference.";

            _logger?.Log(
                NPCFlowStage.ConfigurationValidation,
                hasAuth ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                NPCFlowLogLevel.Info,
                $"PlayerDialogueContextService: {authStatus}, EvidenceState={( _evidenceState != null ? "OK" : "missing" )}",
                source: nameof(PlayerDialogueContextService)
            );
        }
    }
}
