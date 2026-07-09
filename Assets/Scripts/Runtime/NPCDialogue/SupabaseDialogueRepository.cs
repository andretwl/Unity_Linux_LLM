using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EditorAttributes;
using Supabase;
using static Postgrest.Constants;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
{
    [Serializable]
    public class SupabaseDialogueRepository : MonoBehaviour
    {
        [Title("Supabase Dialogue Repository (SDK)")]
        [FoldoutGroup("References", true, nameof(_authService))]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [SerializeField, HideProperty, FormerlySerializedAs("authService")]
        PlayerAuthService _authService;

        [FoldoutGroup("Behaviour", true, nameof(requestTimeoutSeconds))]
        [SerializeField]
        EditorAttributes.Void behaviourGroup;

        [SerializeField, HideProperty, Suffix("s")]
        float requestTimeoutSeconds = 10f;

        [FoldoutGroup(
            "Debug",
            true,
            nameof(lastStatus),
            nameof(lastOperation),
            nameof(lastOperationDurationMs)
        )]
        [SerializeField]
        EditorAttributes.Void debugGroup;

        [SerializeField, HideProperty, EditorAttributes.ReadOnly]
        string lastStatus = "Idle";

        [SerializeField, HideProperty, EditorAttributes.ReadOnly]
        string lastOperation = string.Empty;

        [SerializeField, HideProperty, EditorAttributes.ReadOnly]
        long lastOperationDurationMs;

        [ShowInInspector]
        string IsConfiguredPreview => IsConfigured ? "Yes" : "No (check auth)";

        [Button("Validate Repository Settings")]
        void ValidateRepositorySettings()
        {
            bool validAuth = _authService != null && _authService.SupabaseClient != null;
            bool authed = _authService != null && _authService.IsAuthenticated;

            lastStatus =
                validAuth && authed
                    ? "Repository settings look valid."
                    : "Repository settings are incomplete. Check AuthService reference and ensure authenticated.";
            lastOperation = "ValidateRepositorySettings";

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    validAuth && authed
                        ? NPCFlowStatus.Success
                        : NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Info,
                    lastStatus,
                    source: nameof(SupabaseDialogueRepository),
                    data: new Dictionary<string, object>
                    {
                        ["authAssigned"] = validAuth,
                        ["isAuthenticated"] = authed,
                    }
                );
        }

        string _lastSessionId;
        NPCFlowLogger _logger;
        Supabase.Client _client;

        void Awake()
        {
            _logger = NPCFlowLogger.FindOrCreate();
        }

        public bool IsConfigured =>
            _authService != null
            && _authService.SupabaseClient != null
            && _authService.IsAuthenticated;

        Supabase.Client GetClient()
        {
            if (_client == null && _authService != null)
                _client = _authService.SupabaseClient;
            return _client;
        }

        // ── Load ──────────────────────────────────────────────────

        public async Task<List<DialogueEntry>> LoadHistoryAsync(string npcSlug)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(npcSlug))
                return null;

            lastOperation = $"LoadHistoryAsync({npcSlug})";
            long startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            try
            {
                var client = GetClient();

                string sessionId = await client.Rpc<string>("find_or_create_dialogue_session", new
                {
                    p_player_id = _authService.CurrentSession.playerId,
                    p_npc_slug = npcSlug,
                });

                if (string.IsNullOrWhiteSpace(sessionId))
                    return null;

                _lastSessionId = sessionId;

                var response = await client
                    .From<DialogueTurnRecord>()
                    .Filter("session_id", Operator.Equals, sessionId)
                    .Order("created_at", Ordering.Ascending)
                    .Get();

                var entries = new List<DialogueEntry>();
                if (response.Models != null)
                {
                    foreach (var turn in response.Models)
                    {
                        if (string.IsNullOrWhiteSpace(turn.Role) || turn.Content == null)
                            continue;
                        entries.Add(new DialogueEntry(turn.Role, turn.Content));
                    }
                }

                lastStatus =
                    $"Loaded {entries.Count} turns from session {sessionId} for NPC '{npcSlug}'.";
                Log(NPCFlowStage.HistoryLoad, NPCFlowStatus.Success, lastStatus);

                return entries;
            }
            catch (Exception ex)
            {
                lastStatus = $"Load failed for NPC '{npcSlug}': {ex.Message}";
                Log(
                    NPCFlowStage.HistoryLoad,
                    NPCFlowStatus.Error,
                    lastStatus + $" ({ex.GetType().Name})"
                );
                return null;
            }
            finally
            {
                lastOperationDurationMs =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAt;
            }
        }

        // ── Save ──────────────────────────────────────────────────

        public async Task<bool> SaveTurnAsync(string npcSlug, string role, string content)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(npcSlug))
                return false;

            lastOperation = $"SaveTurnAsync({npcSlug}, {role})";
            long startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            try
            {
                var client = GetClient();

                if (string.IsNullOrWhiteSpace(_lastSessionId))
                {
                    string sessionId = await client.Rpc<string>("find_or_create_dialogue_session", new
                    {
                        p_player_id = _authService.CurrentSession.playerId,
                        p_npc_slug = npcSlug,
                    });
                    if (string.IsNullOrWhiteSpace(sessionId))
                        return false;
                    _lastSessionId = sessionId;
                }

                var turn = new DialogueTurnRecord
                {
                    SessionId = _lastSessionId,
                    PlayerId = _authService.CurrentSession.playerId,
                    Role = role,
                    Content = content,
                };

                await client.From<DialogueTurnRecord>().Insert(turn);

                lastStatus = $"Saved {role} turn for NPC '{npcSlug}' (session {_lastSessionId}).";
                Log(NPCFlowStage.HistoryPersist, NPCFlowStatus.Success, lastStatus);
                return true;
            }
            catch (Exception ex)
            {
                lastStatus = $"Save failed for NPC '{npcSlug}': {ex.Message}";
                Log(NPCFlowStage.HistoryPersist, NPCFlowStatus.Error, lastStatus);
                return false;
            }
            finally
            {
                lastOperationDurationMs =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAt;
            }
        }

        // ── Delete ────────────────────────────────────────────────

        public async Task<bool> DeleteHistoryAsync(string npcSlug)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(npcSlug))
                return false;

            lastOperation = $"DeleteHistoryAsync({npcSlug})";
            long startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            try
            {
                var client = GetClient();

                await client.Rpc("close_dialogue_session", new
                {
                    p_player_id = _authService.CurrentSession.playerId,
                    p_npc_slug = npcSlug,
                });

                _lastSessionId = null;
                lastStatus = $"History deleted for NPC '{npcSlug}'.";
                Log(NPCFlowStage.HistoryPersist, NPCFlowStatus.Success, lastStatus);
                return true;
            }
            catch (Exception ex)
            {
                lastStatus = $"Delete failed for NPC '{npcSlug}': {ex.Message}";
                Log(NPCFlowStage.HistoryPersist, NPCFlowStatus.Error, lastStatus);
                return false;
            }
            finally
            {
                lastOperationDurationMs =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAt;
            }
        }

        // ── Logging ──────────────────────────────────────────────

        void Log(NPCFlowStage stage, NPCFlowStatus status, string message)
        {
            _logger?.Log(
                stage,
                status,
                NPCFlowLogLevel.Debug,
                message,
                source: nameof(SupabaseDialogueRepository),
                data: new Dictionary<string, object>
                {
                    ["sessionId"] = _lastSessionId ?? string.Empty,
                    ["authPlayerId"] = _authService?.CurrentSession?.playerId ?? string.Empty,
                }
            );
        }
    }
}
