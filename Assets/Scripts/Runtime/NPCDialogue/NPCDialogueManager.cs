using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace NPCSystem
{
    [DefaultExecutionOrder(-1500)]
    public class NPCDialogueManager : MonoBehaviour
    {
        [FoldoutGroup("Chat Client", true, nameof(_chatClient))]
        [HelpBox(
            "All NPC dialogue goes through NPCLocalAIClient (HTTP to LocalAI). The local RAG (NPCLocalRAG) is an optional fallback when Qdrant is disabled.",
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
            nameof(_localRag),
            nameof(_ragEmbeddingPath),
            nameof(_useQdrantRag),
            nameof(_qdrantRag)
        )]
        [SerializeField]
        EditorAttributes.Void ragServicesGroup;

        [FormerlySerializedAs("LocalRag")]
        [SerializeField, HideProperty]
        public NPCLocalRAG _localRag;

        [FilePath(true, "rag")]
        [FormerlySerializedAs("RagEmbeddingPath")]
        [FormerlySerializedAs("ragEmbeddingPath")]
        [SerializeField, HideProperty]
        string _ragEmbeddingPath = "RAG/NPCDialogues.rag";

        [SerializeField, HideProperty]
        [FormerlySerializedAs("useQdrantRag")]
        [FormerlySerializedAs("UseQdrantRag")]
        bool _useQdrantRag = false;

        [FormerlySerializedAs("qdrantRag")]
        [SerializeField, HideProperty, ShowField(nameof(_useQdrantRag))]
        public QdrantRAGService _qdrantRag;

        [FoldoutGroup("Game Systems", true, nameof(_actionPlanner), nameof(_evidenceState))]
        [SerializeField]
        EditorAttributes.Void gameSystemsGroup;

        [FormerlySerializedAs("actionPlanner")]
        [SerializeField, HideProperty]
        public NPCDialogueActionPlanner _actionPlanner;

        [FormerlySerializedAs("evidenceState")]
        [SerializeField, HideProperty]
        public NPCEvidenceState _evidenceState;

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
        int _remotePort = 8090;

        [Dropdown(nameof(_cachedModelNames))]
        [HideProperty]
        [FormerlySerializedAs("remoteModel")]
        [FormerlySerializedAs("RemoteModel")]
        [SerializeField]
        string _remoteModel = "llama-3.2-3b-instruct:q8_0";

        [SerializeField, HideInInspector]
        string[] _cachedModelNames = new string[] { "llama-3.2-3b-instruct:q8_0" };

        [HideProperty, Suffix("port")]
        [FormerlySerializedAs("remoteEmbeddingHost")]
        [FormerlySerializedAs("RemoteEmbeddingHost")]
        [SerializeField]
        string _remoteEmbeddingHost = "localhost";

        [HideProperty, Suffix("port")]
        [FormerlySerializedAs("remoteEmbeddingPort")]
        [FormerlySerializedAs("RemoteEmbeddingPort")]
        [SerializeField]
        int _remoteEmbeddingPort = 8090;

        [FoldoutGroup(
            "Dialogue Settings",
            true,
            nameof(_profiles),
            nameof(_persistHistory),
            nameof(_enableRAG),
            nameof(_rebuildRagFromKnowledgeIfMissing),
            nameof(_maxHistoryPerNPC),
            nameof(_initializeOnStart)
        )]
        [SerializeField]
        EditorAttributes.Void dialogueSettingsGroup;

        [HideProperty]
        [FormerlySerializedAs("profiles")]
        [SerializeField]
        NPCProfile[] _profiles = Array.Empty<NPCProfile>();

        [HideProperty, FormerlySerializedAs("persistHistory")]
        [SerializeField]
        bool _persistHistory = true;

        [HideProperty, FormerlySerializedAs("enableRAG")]
        [SerializeField]
        bool _enableRAG = true;

        [HideProperty, FormerlySerializedAs("rebuildRagFromKnowledgeIfMissing")]
        [SerializeField]
        bool _rebuildRagFromKnowledgeIfMissing = true;

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

        [FoldoutGroup(
            "Events",
            true,
            nameof(OnNpcChanged),
            nameof(OnResponseStart),
            nameof(OnResponseComplete),
            nameof(OnError)
        )]
        [HelpBox(
            "Subscribe to these UnityEvents to react to dialogue lifecycle changes. Events fire on the NPC switch, response start, streaming update, completion, and error paths.",
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

        // ── Bootstrapper flags (formerly NPCDialogueBootstrapper) ──

        [FoldoutGroup(
            "Startup Behaviour",
            true,
            nameof(_autoSelectDefaultNPC),
            nameof(_defaultNpcSlug)
        )]
        [SerializeField]
        EditorAttributes.Void startupGroup;

        [SerializeField, HideProperty]
        [FormerlySerializedAs("autoSelectDefaultNPC")]
        bool _autoSelectDefaultNPC = true;

        [SerializeField, HideProperty]
        [FormerlySerializedAs("defaultNpcSlug")]
        string _defaultNpcSlug = "";

        readonly Dictionary<string, NPCProfile> _profilesBySlug = new Dictionary<
            string,
            NPCProfile
        >(StringComparer.OrdinalIgnoreCase);
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
        public string RemoteHost
        {
            get => _remoteHost;
            set => _remoteHost = value;
        }
        public int RemotePort
        {
            get => _remotePort;
            set => _remotePort = value;
        }
        public string RemoteModel
        {
            get => _remoteModel;
            set => _remoteModel = value;
        }
        public string RemoteEmbeddingHost
        {
            get => _remoteEmbeddingHost;
            set => _remoteEmbeddingHost = value;
        }
        public int RemoteEmbeddingPort
        {
            get => _remoteEmbeddingPort;
            set => _remoteEmbeddingPort = value;
        }
        public string RagEmbeddingPath
        {
            get => _ragEmbeddingPath;
            set => _ragEmbeddingPath = value;
        }
        public bool UseQdrantRag
        {
            get => _useQdrantRag;
            set => _useQdrantRag = value;
        }
        public bool PersistHistory
        {
            get => _persistHistory;
            set => _persistHistory = value;
        }
        public bool EnableRAG
        {
            get => _enableRAG;
            set => _enableRAG = value;
        }
        public bool RebuildRagFromKnowledgeIfMissing
        {
            get => _rebuildRagFromKnowledgeIfMissing;
            set => _rebuildRagFromKnowledgeIfMissing = value;
        }
        public int MaxHistoryPerNPC
        {
            get => _maxHistoryPerNPC;
            set => _maxHistoryPerNPC = value;
        }
        public bool InitializeOnStart
        {
            get => _initializeOnStart;
            set => _initializeOnStart = value;
        }
        public bool IsResponding => _sessionService != null && _sessionService.IsResponding;
        public bool IsInitialized =>
            _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
        public bool IsRagAvailable => _retrievalService != null && _retrievalService.IsRagAvailable;

        void Start()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            DatadogMetricsService.Initialize();
            DatadogTracer.Initialize();
#endif
            if (InitializeOnStart)
            {
                _ = InitializeAsync();
            }
        }

        public Task InitializeAsync()
        {
            lock (_initializationLock)
            {
                _initializationTask ??= InitializeInternalAsync();
                return _initializationTask;
            }
        }

        async Task InitializeInternalAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(RemoteHost) && NPCNetworkUtils.IsLocalHost(RemoteHost))
            {
                try
                {
                    Uri pageUri = new Uri(Application.absoluteURL);
                    if (!NPCNetworkUtils.IsLocalHost(pageUri.Host))
                    {
                        RemoteHost = pageUri.Host;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[NPCDialogueManager] Failed to dynamically resolve RemoteHost: {ex.Message}"
                    );
                }
            }
            if (
                !string.IsNullOrWhiteSpace(RemoteEmbeddingHost)
                && NPCNetworkUtils.IsLocalHost(RemoteEmbeddingHost)
            )
            {
                try
                {
                    Uri pageUri = new Uri(Application.absoluteURL);
                    if (!NPCNetworkUtils.IsLocalHost(pageUri.Host))
                    {
                        RemoteEmbeddingHost = pageUri.Host;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[NPCDialogueManager] Failed to dynamically resolve RemoteEmbeddingHost: {ex.Message}"
                    );
                }
            }
#endif

            using var scope = NPCFlowScope.Start(
                Logger,
                NPCFlowStage.SceneBootstrap,
                source: nameof(NPCDialogueManager)
            );
            using var initSpan = DatadogTracer.StartSpan(
                "dialogue.manager.initialize",
                service: "unity-dedicated-server",
                resource: "InitializeDialogue",
                type: "system",
                tags: new[]
                {
                    $"profile_count:{Profiles.Length}",
                    $"use_qdrant:{UseQdrantRag}",
                }
            );
            var initSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                AutoAssignReferencesIfNeeded();
                ValidateReferences();
                BuildProfileIndex();
                _historyService?.Initialize(_supabaseRepo, PersistHistory, MaxHistoryPerNPC);
                if (_historyService != null)
                    await _historyService.LoadAllHistoriesAsync(Profiles);
                _retrievalService?.Initialize(
                    _localRag,
                    RagEmbeddingPath,
                    EnableRAG,
                    UseQdrantRag,
                    _qdrantRag,
                    RebuildRagFromKnowledgeIfMissing,
                    RemoteEmbeddingHost,
                    RemoteEmbeddingPort
                );
                if (_retrievalService != null)
                    await _retrievalService.LoadOrBuildIndexAsync(Profiles);

                _sessionService?.Initialize(
                    _chatClient,
                    _historyService,
                    _retrievalService,
                    _actionPlanner,
                    _evidenceState,
                    _contextService,
                    RemoteHost,
                    RemotePort,
                    RemoteModel,
                    _profiles
                );

                if (_sessionService != null)
                {
                    _sessionService.OnResponseStart += OnSessionResponseStart;
                    _sessionService.OnResponseComplete += OnSessionResponseComplete;
                    _sessionService.OnError += OnSessionError;
                }

                initSw.Stop();
                initSpan.SetTag("status", "success");
                DatadogMetricsService.Timer("dialogue.manager.initialize.duration", initSw.ElapsedMilliseconds, tags: new[]
                {
                    $"profile_count:{Profiles.Length}",
                    $"use_qdrant:{UseQdrantRag}",
                });
                DatadogMetricsService.Increment("dialogue.manager.initialize.count", tags: new[]
                {
                    "status:success",
                });

                scope.Success("Initialization complete.");

                // Auto-select default NPC if configured (formerly NPCDialogueBootstrapper)
                if (_autoSelectDefaultNPC && _currentNPC == null)
                {
                    string npcKey = !string.IsNullOrWhiteSpace(_defaultNpcSlug)
                        ? _defaultNpcSlug.Trim()
                        : GetDefaultProfileSlug();
                    if (!string.IsNullOrWhiteSpace(npcKey))
                    {
                        _ = SwitchToNPCAsync(npcKey);
                    }
                }
            }
            catch (Exception ex)
            {
                initSw.Stop();
                initSpan.SetError(ex.Message);
                DatadogMetricsService.Timer("dialogue.manager.initialize.duration", initSw.ElapsedMilliseconds, tags: new[]
                {
                    $"profile_count:{Profiles.Length}",
                    $"use_qdrant:{UseQdrantRag}",
                });
                DatadogMetricsService.Increment("dialogue.manager.initialize.count", tags: new[]
                {
                    "status:failed",
                });

                scope.Error(ex, "Initialization failed.");
                throw;
            }
        }

        [Button("Auto Assign Dialogue References")]
        void AutoAssignReferencesIfNeeded()
        {
            if (_chatClient == null)
            {
                _chatClient = FindAnyObjectByType<NPCLocalAIClient>(FindObjectsInactive.Include);
            }

            if (_localRag == null)
            {
                _localRag = FindAnyObjectByType<NPCLocalRAG>(FindObjectsInactive.Include);
            }

            if (UseQdrantRag && _qdrantRag == null)
            {
                _qdrantRag = FindAnyObjectByType<QdrantRAGService>(FindObjectsInactive.Include);
            }

            if (_actionPlanner == null)
            {
                _actionPlanner = FindAnyObjectByType<NPCDialogueActionPlanner>(
                    FindObjectsInactive.Include
                );
            }

            if (_evidenceState == null)
            {
                _evidenceState = FindAnyObjectByType<NPCEvidenceState>(FindObjectsInactive.Include);
            }

            if (_historyService == null)
            {
                _historyService = GetComponent<NPCDialogueHistoryService>();
            }
            if (_historyService == null)
            {
                _historyService = FindAnyObjectByType<NPCDialogueHistoryService>(
                    FindObjectsInactive.Include
                );
            }

            if (_retrievalService == null)
            {
                _retrievalService = GetComponent<NPCDialogueRetrievalService>();
            }
            if (_retrievalService == null)
            {
                _retrievalService = FindAnyObjectByType<NPCDialogueRetrievalService>(
                    FindObjectsInactive.Include
                );
            }

            if (_sessionService == null)
            {
                _sessionService = GetComponent<NPCDialogueSessionService>();
            }
            if (_sessionService == null)
            {
                _sessionService = FindAnyObjectByType<NPCDialogueSessionService>(
                    FindObjectsInactive.Include
                );
            }

            if (_contextService == null)
            {
                _contextService = FindAnyObjectByType<PlayerDialogueContextService>(
                    FindObjectsInactive.Include
                );
            }

            if (_retrievalService != null)
            {
                _retrievalService.SyncEmbedderHost();
            }

            Logger.Log(
                NPCFlowStage.SceneBootstrap,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Debug,
                "AutoAssignReferencesIfNeeded completed.",
                source: nameof(NPCDialogueManager)
            );
        }

        [Button("Validate Dialogue References")]
        void ValidateReferences()
        {
            if (_chatClient == null)
                Logger.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "NPCLocalAIClient reference not set!",
                    source: nameof(NPCDialogueManager)
                );
            if (_localRag == null)
                Logger.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    "LocalRAG reference not set. Prompt-only mode will be used.",
                    source: nameof(NPCDialogueManager)
                );
        }

        // ── SessionService event bridge ──
        void OnSessionResponseStart(string msg) => OnResponseStart?.Invoke(msg);

        void OnSessionResponseComplete(string npc, string response) =>
            OnResponseComplete?.Invoke(npc, response);

        void OnSessionError(string err) => OnError?.Invoke(err);

        [Button("Fetch Models from LocalAI")]
        void FetchAvailableModelsFromLocalAI()
        {
            string url = $"http://{RemoteHost}:{RemotePort}/v1/models";
            Debug.Log($"[NPC] Fetching models from {url}...");

            try
            {
                var handler = new System.Net.Http.HttpClientHandler();
                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var responseTask = client.GetAsync(url);
                    var responseMsg = responseTask.GetAwaiter().GetResult();
                    string response = responseMsg
                        .Content.ReadAsStringAsync()
                        .GetAwaiter()
                        .GetResult();

                    Debug.Log(
                        $"[NPC] Raw response ({response.Length} chars): {response.Substring(0, Mathf.Min(response.Length, 300))}"
                    );

                    var wrapper = JsonUtility.FromJson<LocalAIModelsResponse>(response);
                    if (wrapper != null && wrapper.data != null && wrapper.data.Length > 0)
                    {
                        _cachedModelNames = Array.ConvertAll(wrapper.data, m => m.id);
                        string modelList = string.Join(", ", _cachedModelNames);
                        Debug.Log($"[NPC] Found {_cachedModelNames.Length} model(s): {modelList}");
                        Logger.Log(
                            NPCFlowStage.EditorWorkflow,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            $"Found {_cachedModelNames.Length} model(s): {modelList}",
                            source: nameof(NPCDialogueManager)
                        );

#if UNITY_EDITOR
                        UnityEditor.EditorUtility.SetDirty(this);
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
                    }
                    else
                    {
                        string errorDetail =
                            wrapper == null
                                ? "JsonUtility returned null"
                                : $"data array is empty or null (object={wrapper.@object})";
                        Debug.LogError($"[NPC] Failed to parse models response. {errorDetail}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[NPC] Failed to fetch models from {url}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
                );
            }
        }

        void BuildProfileIndex()
        {
            _profilesBySlug.Clear();

            foreach (NPCProfile profile in Profiles)
            {
                string slug = profile.GetNpcSlug();
                if (_profilesBySlug.ContainsKey(slug))
                {
                    Logger.Log(
                        NPCFlowStage.ProfileIndexBuild,
                        NPCFlowStatus.Warning,
                        NPCFlowLogLevel.Warning,
                        $"Duplicate NPC profile slug detected: {slug}. Keeping the first entry.",
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

        public void SwitchToNPC(string npcName)
        {
            _ = SwitchToNPCAsync(npcName);
        }

        public async Task SwitchToNPCAsync(string npcName)
        {
            using var scope = NPCFlowScope.Start(
                Logger,
                NPCFlowStage.NPCSwitch,
                source: nameof(NPCDialogueManager),
                npcSlug: npcName
            );
            await InitializeAsync();

            NPCProfile profile = FindProfile(npcName);
            if (profile == null)
            {
                string error = $"NPC '{npcName}' not found";
                Logger.Log(
                    NPCFlowStage.NPCSwitch,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Warning,
                    error + "!",
                    source: nameof(NPCDialogueManager)
                );
                OnError?.Invoke(error);
                scope.Skipped(error);
                return;
            }

            CancelRequests();

            _currentNPC = profile;
            scope.Success(
                $"Switched to NPC: {profile.GetDisplayName()}",
                new Dictionary<string, object> { ["npcSlug"] = profile.GetNpcSlug() }
            );

            DatadogMetricsService.Increment("dialogue.npc.switch.count", tags: new[]
            {
                $"npc:{profile.GetNpcSlug()}",
                $"display_name:{profile.GetDisplayName()}",
            });

            OnNpcChanged?.Invoke(profile.GetDisplayName());
        }

        public void SendDialogueMessage(string playerMessage)
        {
            if (_currentNPC == null)
            {
                Logger.Log(
                    NPCFlowStage.RequestStart,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Warning,
                    "No NPC selected! Call SwitchToNPC() first.",
                    source: nameof(NPCDialogueManager)
                );
                OnError?.Invoke("No NPC selected");
                return;
            }

            // Resolve current player name from the auth bridge before each turn
            string activePlayerName = AuthNetworkBridge.ActivePlayerName;
            if (!string.IsNullOrWhiteSpace(activePlayerName) && activePlayerName != "Player")
            {
                SetRuntimePlayerContext(activePlayerName);
            }

            _sessionService?.SendDialogueMessage(playerMessage, _currentNPC);
        }

        public void SetRuntimePlayerContext(string playerName, ulong? clientId = null)
        {
            _sessionService?.SetRuntimePlayerContext(playerName, clientId);
        }

        public void ClearRuntimePlayerContext()
        {
            _sessionService?.ClearRuntimePlayerContext();
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
                || string.Equals(profile.name, key, StringComparison.OrdinalIgnoreCase)
            );
        }

        public async Task AddNPCKnowledge(string npcName, string knowledgeText)
        {
            if (_retrievalService != null)
                await _retrievalService.AddKnowledgeAsync(npcName, knowledgeText, Profiles);
        }

        public void SaveRAGEmbeddings()
        {
            _retrievalService?.SaveIndex();
        }

        public List<DialogueEntry> GetHistory(string npcName)
        {
            NPCProfile profile = FindProfile(npcName);
            if (profile == null)
                return new List<DialogueEntry>();
            return _historyService?.GetHistory(profile) ?? new List<DialogueEntry>();
        }

        public Dictionary<string, List<DialogueEntry>> CaptureHistorySnapshot()
        {
            return _historyService?.CaptureHistorySnapshot(Profiles)
                ?? new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);
        }

        public void ApplyHistorySnapshot(Dictionary<string, List<DialogueEntry>> historyByNpc)
        {
            _historyService?.ApplyHistorySnapshot(historyByNpc, Profiles);
        }

        public NPCEvidenceStateSnapshot CaptureEvidenceSnapshot()
        {
            return _evidenceState != null
                ? _evidenceState.CreateSnapshot()
                : new NPCEvidenceStateSnapshot();
        }

        public void ApplyEvidenceSnapshot(NPCEvidenceStateSnapshot snapshot)
        {
            if (_evidenceState == null)
                return;
            _evidenceState.ApplySnapshot(snapshot);
        }

        public async Task ClearHistory(string npcName)
        {
            if (_historyService != null)
                await _historyService.ClearHistoryAsync(npcName, Profiles);
        }

        public void CancelRequests()
        {
            _sessionService?.CancelRequests();
        }

        public string[] GetNPCNames()
        {
            return Profiles.Select(profile => profile.GetNpcSlug()).ToArray();
        }

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

            // Discover optional service references without auto-adding or destroying scene components.
            if (_qdrantRag == null)
            {
                _qdrantRag = GetComponent<QdrantRAGService>();
            }
        }
#endif
    }

    [Serializable]
    public class LocalAIModelEntry
    {
        public string id;
        public string @object;
    }

    [Serializable]
    public class LocalAIModelsResponse
    {
        public string @object;
        public LocalAIModelEntry[] data;
    }
}
