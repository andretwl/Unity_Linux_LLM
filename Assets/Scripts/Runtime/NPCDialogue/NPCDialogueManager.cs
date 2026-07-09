using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Events;

namespace NPCSystem
{
    [DefaultExecutionOrder(-1500)]
    public class NPCDialogueManager : MonoBehaviour
    {
        [FoldoutGroup("Chat Client", true, nameof(chatClient))]
        [HelpBox(
            "All NPC dialogue goes through NPCLocalAIClient (HTTP to LocalAI). The local RAG (NPCLocalRAG) is an optional fallback when Qdrant is disabled.",
            MessageMode.Log,
            drawAbove: true
        )]
        [SerializeField]
        EditorAttributes.Void chatClientGroup;

        [SerializeField, HideProperty, Required]
        public NPCLocalAIClient chatClient;

        [FoldoutGroup(
            "RAG Services",
            true,
            nameof(localRag),
            nameof(ragEmbeddingPath),
            nameof(useQdrantRag),
            nameof(qdrantRag)
        )]
        [SerializeField]
        EditorAttributes.Void ragServicesGroup;

        [SerializeField, HideProperty]
        public NPCLocalRAG localRag;

        [FilePath(true, "rag")]
        [SerializeField, HideProperty]
        public string ragEmbeddingPath = "RAG/NPCDialogues.rag";

        [SerializeField, HideProperty]
        public bool useQdrantRag = false;

        [SerializeField, HideProperty, ShowField(nameof(useQdrantRag))]
        public QdrantRAGService qdrantRag;

        [FoldoutGroup("Game Systems", true, nameof(actionPlanner), nameof(evidenceState))]
        [SerializeField]
        EditorAttributes.Void gameSystemsGroup;

        [SerializeField, HideProperty]
        public NPCDialogueActionPlanner actionPlanner;

        [SerializeField, HideProperty]
        public NPCEvidenceState evidenceState;

        [FoldoutGroup("Persistence", true, nameof(supabaseRepo))]
        [SerializeField]
        EditorAttributes.Void persistenceGroup;

        [SerializeField, HideProperty]
        public SupabaseDialogueRepository supabaseRepo;

        [FoldoutGroup(
            "LLM Configuration",
            true,
            nameof(remoteHost),
            nameof(remotePort),
            nameof(remoteModel),
            nameof(remoteEmbeddingHost),
            nameof(remoteEmbeddingPort)
        )]
        [SerializeField]
        EditorAttributes.Void llmConfigGroup;

        [HideProperty]
        public string remoteHost = "localhost";

        [ShowField(nameof(enableRemoteServer))]
        [HideProperty, Suffix("port")]
        public int remotePort = 11435;

        [Dropdown(nameof(_cachedModelNames))]
        [HideProperty]
        public string remoteModel = "llama-3.2-3b-instruct:q8_0";

        [SerializeField, HideInInspector]
        string[] _cachedModelNames = new string[] { "default-llm" };

        [ShowField(nameof(enableRemoteServer))]
        [HideProperty, Suffix("port")]
        public string remoteEmbeddingHost = "localhost";

        [ShowField(nameof(enableRemoteServer))]
        [HideProperty, Suffix("port")]
        public int remoteEmbeddingPort = 8080;

        [FoldoutGroup(
            "Dialogue Settings",
            true,
            nameof(profiles),
            nameof(persistHistory),
            nameof(enableRAG),
            nameof(rebuildRagFromKnowledgeIfMissing),
            nameof(maxHistoryPerNPC),
            nameof(initializeOnStart)
        )]
        [SerializeField]
        EditorAttributes.Void dialogueSettingsGroup;

        [HideProperty]
        public NPCProfile[] profiles = Array.Empty<NPCProfile>();

        [HideProperty]
        public bool persistHistory = true;

        [HideProperty]
        public bool enableRAG = true;

        [HideProperty]
        public bool rebuildRagFromKnowledgeIfMissing = true;

        [Clamp(1, 200)]
        [HideProperty]
        public int maxHistoryPerNPC = 20;

        [Tooltip(
            "If true, dialogue systems initialize on Start. If false, they are initialized on-demand (e.g., after player login success)."
        )]
        [HideProperty]
        public bool initializeOnStart = false;

        [FoldoutGroup(
            "Events",
            true,
            nameof(onNPCChanged),
            nameof(onResponseStart),
            nameof(onResponseUpdated),
            nameof(onResponseComplete),
            nameof(onError)
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
            $"http://{remoteHost}:{remotePort}/v1/chat/completions";

        [ReadOnly]
        [ShowInInspector]
        string ActiveProfilePreview =>
            currentProfile == null ? "<none>" : currentProfile.GetDisplayName();

        [SerializeField, HideInInspector]
        string lastBackendStatus = "Idle";

        [ReadOnly]
        [ShowInInspector]
        string LastBackendStatusPreview => lastBackendStatus;

        bool enableRemoteServer => true;

        [HideProperty]
        public UnityEvent<string> onNPCChanged = new UnityEvent<string>();

        [HideProperty]
        public UnityEvent<string> onResponseStart = new UnityEvent<string>();

        [HideProperty]
        public UnityEvent<string> onResponseUpdated = new UnityEvent<string>();

        [HideProperty]
        public UnityEvent<string, string> onResponseComplete = new UnityEvent<string, string>();

        [HideProperty]
        public UnityEvent<string> onError = new UnityEvent<string>();

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

        static NPCFlowLogger Logger => NPCFlowLogger.FindOrCreate();

        public NPCProfile currentProfile => _currentNPC;
        public bool isResponding => _sessionService != null && _sessionService.isResponding;
        public bool isInitialized =>
            _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
        public bool isRagAvailable => _retrievalService != null && _retrievalService.IsRagAvailable;
        public NPCProfile[] Profiles =>
            profiles == null
                ? Array.Empty<NPCProfile>()
                : profiles.Where(profile => profile != null).ToArray();

        void Start()
        {
            if (initializeOnStart)
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
            if (
                !string.IsNullOrWhiteSpace(remoteHost)
                && (remoteHost == "localhost" || remoteHost == "127.0.0.1")
            )
            {
                try
                {
                    Uri pageUri = new Uri(Application.absoluteURL);
                    if (pageUri.Host != "localhost" && pageUri.Host != "127.0.0.1")
                    {
                        remoteHost = pageUri.Host;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[NPCDialogueManager] Failed to dynamically resolve remoteHost: {ex.Message}"
                    );
                }
            }
            if (
                !string.IsNullOrWhiteSpace(remoteEmbeddingHost)
                && (remoteEmbeddingHost == "localhost" || remoteEmbeddingHost == "127.0.0.1")
            )
            {
                try
                {
                    Uri pageUri = new Uri(Application.absoluteURL);
                    if (pageUri.Host != "localhost" && pageUri.Host != "127.0.0.1")
                    {
                        remoteEmbeddingHost = pageUri.Host;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[NPCDialogueManager] Failed to dynamically resolve remoteEmbeddingHost: {ex.Message}"
                    );
                }
            }
#endif

            using var scope = NPCFlowScope.Start(
                Logger,
                NPCFlowStage.SceneBootstrap,
                source: nameof(NPCDialogueManager)
            );
            try
            {
                AutoAssignReferencesIfNeeded();
                ValidateReferences();
                BuildProfileIndex();
                _historyService?.Initialize(supabaseRepo, persistHistory, maxHistoryPerNPC);
                if (_historyService != null)
                    await _historyService.LoadAllHistoriesAsync(Profiles);
                _retrievalService?.Initialize(
                    localRag,
                    ragEmbeddingPath,
                    enableRAG,
                    useQdrantRag,
                    qdrantRag,
                    rebuildRagFromKnowledgeIfMissing,
                    remoteEmbeddingHost,
                    remoteEmbeddingPort
                );
                if (_retrievalService != null)
                    await _retrievalService.LoadOrBuildIndexAsync(Profiles);

                _sessionService?.Initialize(
                    chatClient,
                    _historyService,
                    _retrievalService,
                    actionPlanner,
                    evidenceState,
                    remoteHost,
                    remotePort,
                    Profiles
                );

                if (_sessionService != null)
                {
                    _sessionService.onResponseStart += OnSessionResponseStart;
                    _sessionService.onResponseComplete += OnSessionResponseComplete;
                    _sessionService.onError += OnSessionError;
                }

                scope.Success("Initialization complete.");
            }
            catch (Exception ex)
            {
                scope.Error(ex, "Initialization failed.");
                throw;
            }
        }

        [Button("Auto Assign Dialogue References")]
        void AutoAssignReferencesIfNeeded()
        {
            if (chatClient == null)
            {
                chatClient = FindAnyObjectByType<NPCLocalAIClient>(FindObjectsInactive.Include);
            }

            if (localRag == null)
            {
                localRag = FindAnyObjectByType<NPCLocalRAG>(FindObjectsInactive.Include);
            }

            if (useQdrantRag && qdrantRag == null)
            {
                qdrantRag = FindAnyObjectByType<QdrantRAGService>(FindObjectsInactive.Include);
            }

            if (actionPlanner == null)
            {
                actionPlanner = FindAnyObjectByType<NPCDialogueActionPlanner>(
                    FindObjectsInactive.Include
                );
            }

            if (evidenceState == null)
            {
                evidenceState = FindAnyObjectByType<NPCEvidenceState>(FindObjectsInactive.Include);
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

            if (_retrievalService != null)
            {
                _retrievalService.SyncEmbedderHost();
            }

            if (chatClient != null)
            {
                chatClient.host = remoteHost;
                chatClient.port = remotePort;
                chatClient.model = remoteModel;
            }
        }

        [Button("Validate Dialogue References")]
        void ValidateReferences()
        {
            if (chatClient == null)
                Logger.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "NPCLocalAIClient reference not set!",
                    source: nameof(NPCDialogueManager)
                );
            if (localRag == null)
                Logger.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    "LocalRAG reference not set. Prompt-only mode will be used.",
                    source: nameof(NPCDialogueManager)
                );
        }

        // ── SessionService event bridge ──
        void OnSessionResponseStart(string msg) => onResponseStart?.Invoke(msg);

        void OnSessionResponseComplete(string npc, string response) =>
            onResponseComplete?.Invoke(npc, response);

        void OnSessionError(string err) => onError?.Invoke(err);

        [Button("Fetch Models from LocalAI")]
        void FetchAvailableModelsFromLocalAI()
        {
            string url = $"http://{remoteHost}:{remotePort}/v1/models";
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
                onError?.Invoke(error);
                scope.Skipped(error);
                return;
            }

            CancelRequests();

            _currentNPC = profile;
            scope.Success(
                $"Switched to NPC: {profile.GetDisplayName()}",
                new Dictionary<string, object> { ["npcSlug"] = profile.GetNpcSlug() }
            );

            onNPCChanged?.Invoke(profile.GetDisplayName());
        }

        public new void SendMessage(string playerMessage)
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
                onError?.Invoke("No NPC selected");
                return;
            }

            _sessionService?.SendMessage(playerMessage, _currentNPC);
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
            return evidenceState != null
                ? evidenceState.CreateSnapshot()
                : new NPCEvidenceStateSnapshot();
        }

        public void ApplyEvidenceSnapshot(NPCEvidenceStateSnapshot snapshot)
        {
            if (evidenceState == null)
                return;
            evidenceState.ApplySnapshot(snapshot);
        }

        public async void ClearHistory(string npcName)
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
                _sessionService.onResponseStart -= OnSessionResponseStart;
                _sessionService.onResponseComplete -= OnSessionResponseComplete;
                _sessionService.onError -= OnSessionError;
                _sessionService.CancelRequests();
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying)
                return;

            // Discover optional service references without auto-adding or destroying scene components.
            if (qdrantRag == null)
            {
                qdrantRag = GetComponent<QdrantRAGService>();
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
