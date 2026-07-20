using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;


using NPCSystem.Monitoring;
using NPCSystem.Monitoring.Datadog;
using NPCSystem.Dialogue.Core;
using NPCSystem.Network.Core;
using NPCSystem.Character.Player;
using NPCSystem.Auth;
using NPCSystem.Items;
using NPCSystem.LocalAI;
using NPCSystem.Initialization;
using NPCSystem.Character.NPC;
using NPCSystem.Dialogue.Session;
using NPCSystem.Dialogue.UI;
using NPCSystem.Dialogue.RAG;
using NPCSystem.Dialogue.Persistence;
namespace NPCSystem.Dialogue.Core
{
    [DefaultExecutionOrder(-1500)]
    public class NPCDialogueManager : MonoBehaviour
    {
        [FoldoutGroup("Chat Client", true, nameof(_chatClient))]
        [HelpBox(
            "All NPC dialogue goes through NPCLocalAIClient (HTTP to LocalAI). Qdrant RAG provides vector search for NPC knowledge.",
            MessageMode.Log,
            drawAbove: true
        )]
        [SerializeField]
        EditorAttributes.Void chatClientGroup;

        [FormerlySerializedAs("ChatClient")]
        [SerializeField, HideProperty, Required]
        public NPCLocalAIClient _chatClient;

        [FoldoutGroup(
            "RAG Services",
            true,
            nameof(_useQdrantRag),
            nameof(_qdrantRag)
        )]
        [SerializeField]
        EditorAttributes.Void ragServicesGroup;

        [Tooltip("Use Qdrant vector database for NPC knowledge search. Requires QdrantRAGService + NPCLocalAIEmbedder on the GameObject.")]
        [SerializeField, HideProperty]
        bool _useQdrantRag = true;

        [FormerlySerializedAs("qdrantRag")]
        [SerializeField, HideProperty, ShowField(nameof(_useQdrantRag))]
        public QdrantRAGService _qdrantRag;

        [FoldoutGroup("Persistence", true, nameof(_supabaseRepo))]
[SerializeField]
        EditorAttributes.Void persistenceGroup;

        [FormerlySerializedAs("supabaseRepo")]
        [SerializeField, HideProperty]
        public SupabaseDialogueRepository _supabaseRepo;

        [FoldoutGroup(
            "LLM Configuration",
            true,
            nameof(_remoteHost),
            nameof(_remotePort),
            nameof(_remoteModel),
            nameof(_remoteEmbeddingHost),
            nameof(_remoteEmbeddingPort)
        )]
        [SerializeField]
        EditorAttributes.Void llmConfigGroup;

        [HideProperty]
        [FormerlySerializedAs("remoteHost")]
        [FormerlySerializedAs("RemoteHost")]
        [SerializeField]
        string _remoteHost = "localhost";

        [HideProperty]
        [FormerlySerializedAs("remotePort")]
        [FormerlySerializedAs("RemotePort")]
        [SerializeField]
        int _remotePort = NPCLocalAIConfig.LocalAIDirectPort;

        [HideProperty]
        [FormerlySerializedAs("remoteModel")]
        [FormerlySerializedAs("RemoteModel")]
        [SerializeField]
        string _remoteModel = "llama-3.2-3b-instruct:q8_0";

        [HideProperty, Suffix("port")]
        [FormerlySerializedAs("remoteEmbeddingHost")]
        [FormerlySerializedAs("RemoteEmbeddingHost")]
        [SerializeField]
        string _remoteEmbeddingHost = "localhost";

        [HideProperty, Suffix("port")]
        [FormerlySerializedAs("remoteEmbeddingPort")]
        [FormerlySerializedAs("RemoteEmbeddingPort")]
        [SerializeField]
        int _remoteEmbeddingPort = NPCLocalAIConfig.LocalAIDirectPort;

        [HideProperty]
        [FormerlySerializedAs("ragEmbeddingPath")]
        [SerializeField]
        string _ragEmbeddingPath = "RAG/NPCDialogues-minilm-chunked.rag";

        // ── Dialogue Settings ──

        [HideProperty]
        [FormerlySerializedAs("profiles")]
        [SerializeField]
        NPCProfile[] _profiles = Array.Empty<NPCProfile>();

        [Clamp(1, 200)]
        [HideProperty, FormerlySerializedAs("maxHistoryPerNPC")]
        [SerializeField]
        int _maxHistoryPerNPC = 20;

        [Tooltip(
            "If true, dialogue systems initialize on Start. If false, they are initialized on-demand (e.g., after player login success)."
        )]
        [HideProperty, FormerlySerializedAs("initializeOnStart")]
        [SerializeField]
        bool _initializeOnStart = false;

        // ── Events ──

        [HelpBox(
            "Subscribe to these UnityEvents to react to dialogue lifecycle changes.",
            MessageMode.Log
        )]
        [SerializeField]
        EditorAttributes.Void eventsGroup;

        [Title("Runtime Status")]
        [ReadOnly]
        [ShowInInspector]
        string DirectLocalAiEndpointPreview =>
            $"http://{RemoteHost}:{RemotePort}/v1/chat/completions";

        [ReadOnly]
        [ShowInInspector]
        string ActiveProfilePreview =>
            _currentNPC == null ? "<none>" : _currentNPC.GetDisplayName();

        [FormerlySerializedAs("onNPCChanged")]
        [HideProperty]
        public UnityEvent<string> OnNpcChanged = new UnityEvent<string>();

        [FormerlySerializedAs("onResponseStart")]
        [HideProperty]
        public UnityEvent<string> OnResponseStart = new UnityEvent<string>();

        [FormerlySerializedAs("onResponseComplete")]
        [HideProperty]
        public UnityEvent<string, string> OnResponseComplete = new UnityEvent<string, string>();

        [FormerlySerializedAs("onError")]
        [HideProperty]
        public UnityEvent<string> OnError = new UnityEvent<string>();

        // ── Internal state ──

        readonly Dictionary<string, NPCProfile> _profilesBySlug = new Dictionary<string, NPCProfile>(
            StringComparer.OrdinalIgnoreCase
        );
        readonly object _initializationLock = new object();
        Task _initializationTask;
        NPCProfile _currentNPC;
        NPCDialogueHistoryService _historyService;
        NPCDialogueRetrievalService _retrievalService;
        NPCDialogueSessionService _sessionService;
        PlayerDialogueContextService _contextService;

        static NPCFlowLogger Logger => NPCFlowLogger.FindOrCreate();

        public NPCProfile CurrentProfile => _currentNPC;
        public NPCProfile[] Profiles
        {
            get => _profiles ?? Array.Empty<NPCProfile>();
            set => _profiles = value;
        }

        // ─── Configuration properties (backed by [SerializeField] private fields) ───
        public string RemoteHost { get => _remoteHost; set => _remoteHost = value; }
        public int RemotePort { get => _remotePort; set => _remotePort = value; }
        public string RemoteModel { get => _remoteModel; set => _remoteModel = value; }
        public string RemoteEmbeddingHost { get => _remoteEmbeddingHost; set => _remoteEmbeddingHost = value; }
        public int RemoteEmbeddingPort { get => _remoteEmbeddingPort; set => _remoteEmbeddingPort = value; }
        public string RagEmbeddingPath { get => _ragEmbeddingPath; set => _ragEmbeddingPath = value; }
        public bool UseQdrantRag { get => _useQdrantRag; set => _useQdrantRag = value; }
        public bool PersistHistory => true;
        public int MaxHistoryPerNPC { get => _maxHistoryPerNPC; set => _maxHistoryPerNPC = value; }
        public bool InitializeOnStart { get => _initializeOnStart; set => _initializeOnStart = value; }
        public bool IsResponding => _sessionService != null && _sessionService.IsResponding;
        public bool IsInitialized => _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
        public bool IsRagAvailable => _retrievalService != null && _retrievalService.IsRagAvailable;

        void Awake()
        {
            ResolveServices();
        }

        void Start()
        {
            // Intentionally empty — dialogue initialization is sequenced by
            // NPCSceneInitializationController Phase 4 (DialogueServices).
            // The InitializeOnStart flag is read by the controller, not self-triggered.
        }

        /// <summary>
        /// Resolve child/core service references via direct GetComponent (not FindObjectOfType).
        /// Services are expected to be on the same GameObject hierarchy.
        /// </summary>
        void ResolveServices()
        {
            _historyService ??= GetComponentInChildren<NPCDialogueHistoryService>(true)
                ?? GetComponent<NPCDialogueHistoryService>();
            _retrievalService ??= GetComponentInChildren<NPCDialogueRetrievalService>(true)
                ?? GetComponent<NPCDialogueRetrievalService>();
            _sessionService ??= GetComponentInChildren<NPCDialogueSessionService>(true)
                ?? GetComponent<NPCDialogueSessionService>();
            _contextService ??= GetComponentInChildren<PlayerDialogueContextService>(true)
                ?? GetComponent<PlayerDialogueContextService>();
        }

        public Task InitializeAsync()
        {
            lock (_initializationLock)
            {
                if (_initializationTask == null)
                {
                    _initializationTask = InitializeInternalAsync();
                }
                return _initializationTask;
            }
        }

        async Task InitializeInternalAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ResolveWebGlHost();
#endif
            using var scope = NPCFlowScope.Start(
                Logger,
                NPCFlowStage.SceneBootstrap,
                source: nameof(NPCDialogueManager)
            );

            try
            {
                BuildProfileIndex();

                // Each service self-initializes using config from this Manager
                // We await them in parallel since they run on their own schedule
                var serviceTasks = new List<Task>(3);

                if (_historyService != null)
                {
                    _historyService.Initialize(_supabaseRepo, PersistHistory, MaxHistoryPerNPC);
                    serviceTasks.Add(_historyService.LoadAllHistoriesAsync(Profiles));
                }
                if (_retrievalService != null)
                {
                    _retrievalService.Initialize(
                        _useQdrantRag, _qdrantRag,
                        RemoteEmbeddingHost, RemoteEmbeddingPort
                    );
                    _retrievalService.SyncEmbedderHost();
                    serviceTasks.Add(_retrievalService.LoadOrBuildIndexAsync(Profiles));
                }
                if (_sessionService != null)
                {
                    _sessionService.Initialize(
                        _chatClient, _historyService, _retrievalService,
                        _contextService,
                        RemoteHost, RemotePort, RemoteModel, _profiles
                    );
                }

                if (serviceTasks.Count > 0)
                    await Task.WhenAll(serviceTasks);

                // Subscribe session events → UnityEvents
                if (_sessionService != null)
                {
                    _sessionService.OnResponseStart -= OnSessionResponseStart;
                    _sessionService.OnResponseComplete -= OnSessionResponseComplete;
                    _sessionService.OnError -= OnSessionError;
                    _sessionService.OnResponseStart += OnSessionResponseStart;
                    _sessionService.OnResponseComplete += OnSessionResponseComplete;
                    _sessionService.OnError += OnSessionError;
                }

                Logger?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    "Dialogue services initialized.",
                    source: nameof(NPCDialogueManager)
                );

                scope.Success("Initialization complete.");

                // Auto-select first available NPC profile
                if (_currentNPC == null)
                {
                    string defaultSlug = GetDefaultProfileSlug();
                    if (!string.IsNullOrWhiteSpace(defaultSlug))
                    {
                        _ = SwitchToNPCAsync(defaultSlug);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"Initialization failed: {ex.Message}",
                    source: nameof(NPCDialogueManager)
                );
                scope.Error(ex, "Initialization failed.");
                throw;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        void ResolveWebGlHost()
        {
            try
            {
                Uri pageUri = new Uri(Application.absoluteURL);
                if (!NPCNetworkUtils.IsLocalHost(pageUri.Host))
                {
                    if (!string.IsNullOrWhiteSpace(RemoteHost) && NPCNetworkUtils.IsLocalHost(RemoteHost))
                        RemoteHost = pageUri.Host;
                    if (!string.IsNullOrWhiteSpace(RemoteEmbeddingHost) && NPCNetworkUtils.IsLocalHost(RemoteEmbeddingHost))
                        RemoteEmbeddingHost = pageUri.Host;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NPCDialogueManager] Failed to resolve WebGL host: {ex.Message}");
            }
        }
#endif

        // ── SessionService event bridge ──
        void OnSessionResponseStart(string msg) => OnResponseStart?.Invoke(msg);
        void OnSessionResponseComplete(string npc, string response) => OnResponseComplete?.Invoke(npc, response);
        void OnSessionError(string err) => OnError?.Invoke(err);

        void BuildProfileIndex()
        {
            _profilesBySlug.Clear();
            foreach (NPCProfile profile in Profiles)
            {
                string slug = profile.GetNpcSlug();
                if (_profilesBySlug.ContainsKey(slug))
                {
                    Logger?.Log(
                        NPCFlowStage.ProfileIndexBuild,
                        NPCFlowStatus.Warning,
                        NPCFlowLogLevel.Warning,
                        $"Duplicate NPC profile slug: {slug}.",
                        source: nameof(NPCDialogueManager),
                        data: new Dictionary<string, object> { ["slug"] = slug }
                    );
                    continue;
                }
                _profilesBySlug[slug] = profile;
            }
        }

        public string GetDefaultProfileSlug()
        {
            NPCProfile firstProfile = Profiles.FirstOrDefault();
            return firstProfile != null ? firstProfile.GetNpcSlug() : string.Empty;
        }

        public void SwitchToNPC(string npcName) { _ = SwitchToNPCAsync(npcName); }

        public async Task SwitchToNPCAsync(string npcName)
        {
            using var scope = NPCFlowScope.Start(
                Logger, NPCFlowStage.NPCSwitch,
                source: nameof(NPCDialogueManager), npcSlug: npcName
            );
            await InitializeAsync();

            NPCProfile profile = FindProfile(npcName);
            if (profile == null)
            {
                string error = $"NPC '{npcName}' not found";
                Logger?.Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Error, NPCFlowLogLevel.Warning, error, source: nameof(NPCDialogueManager));
                OnError?.Invoke(error);
                scope.Skipped(error);
                return;
            }

            CancelRequests();
            _currentNPC = profile;
            OnNpcChanged?.Invoke(profile.GetDisplayName());
            scope.Success($"Switched to NPC: {profile.GetDisplayName()}");
        }

        public void SendDialogueMessage(string playerMessage)
        {
            if (_currentNPC == null)
            {
                Logger?.Log(NPCFlowStage.RequestStart, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "No NPC selected! Call SwitchToNPC() first.", source: nameof(NPCDialogueManager));
                OnError?.Invoke("No NPC selected");
                return;
            }

            // Resolve player name before each turn
            string activePlayerName = AuthNetworkBridge.ActivePlayerName;
            if (!string.IsNullOrWhiteSpace(activePlayerName) && activePlayerName != "Player")
            {
                _sessionService?.SetRuntimePlayerContext(activePlayerName);
            }

            _sessionService?.SendDialogueMessage(playerMessage, _currentNPC);
        }

        NPCProfile FindProfile(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return null;
            string key = npcName.Trim();
            if (_profilesBySlug.TryGetValue(key, out NPCProfile bySlug))
                return bySlug;
            return Profiles.FirstOrDefault(profile =>
                string.Equals(profile.GetDisplayName(), key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.name, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Get history count for a given NPC (used by UI).</summary>
        public List<DialogueEntry> GetHistory(string npcName)
        {
            NPCProfile profile = FindProfile(npcName);
            if (profile == null)
                return new List<DialogueEntry>();
            return _historyService?.GetHistory(profile) ?? new List<DialogueEntry>();
        }

        /// <summary>Network-bridge snapshot: capture all NPC histories.</summary>
        public Dictionary<string, List<DialogueEntry>> CaptureHistorySnapshot()
        {
            return _historyService?.CaptureHistorySnapshot(Profiles)
                ?? new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Network-bridge snapshot: restore all NPC histories.</summary>
        public void ApplyHistorySnapshot(Dictionary<string, List<DialogueEntry>> historyByNpc)
        {
            _historyService?.ApplyHistorySnapshot(historyByNpc, Profiles);
        }

        /// <summary>Clear history for a given NPC or all NPCs.</summary>
public async Task ClearHistory(string npcName)
        {
            if (_historyService != null)
                await _historyService.ClearHistoryAsync(npcName, Profiles);
        }

        /// <summary>Set the runtime player name/ID override for the dialogue turn.</summary>
        public void SetRuntimePlayerContext(string playerName, ulong? clientId = null)
        {
            _sessionService?.SetRuntimePlayerContext(playerName, clientId);
        }

        /// <summary>Clear any runtime player-context override.</summary>
        public void ClearRuntimePlayerContext()
        {
            _sessionService?.ClearRuntimePlayerContext();
        }

        public void CancelRequests() { _sessionService?.CancelRequests(); }

        public string[] GetNPCNames() => Profiles.Select(p => p.GetNpcSlug()).ToArray();

        void OnDestroy()
        {
            if (_sessionService != null)
            {
                _sessionService.OnResponseStart -= OnSessionResponseStart;
                _sessionService.OnResponseComplete -= OnSessionResponseComplete;
                _sessionService.OnError -= OnSessionError;
                _sessionService.CancelRequests();
            }
            DatadogMetricsService.Shutdown();
            DatadogTracer.Shutdown();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying)
                return;
            if (_qdrantRag == null)
                _qdrantRag = GetComponent<QdrantRAGService>();
        }
#endif
    }
}
