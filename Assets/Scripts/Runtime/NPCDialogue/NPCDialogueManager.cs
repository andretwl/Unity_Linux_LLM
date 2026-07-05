using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace NPCSystem
{
    [DefaultExecutionOrder(-1500)]
    public class NPCDialogueManager : MonoBehaviour
    {
        [Title("LocalAI Chat Client")]
        [HelpBox("All NPC dialogue goes through NPCLocalAIClient (HTTP to LocalAI). The local RAG (NPCLocalRAG) is an optional fallback when Qdrant is disabled.", MessageMode.Log, drawAbove: true)]
        [Required]
        [Header("Chat Client")]
        public NPCLocalAIClient chatClient;

        [Title("Local RAG (Fallback)")]
        [Header("RAG Reference")]
        public NPCLocalRAG localRag;
        [FilePath(true, "rag")]
        public string ragEmbeddingPath = "RAG/NPCDialogues.rag";

        [Title("Qdrant RAG")]
        [Header("Qdrant RAG Reference")]
        [ShowField(nameof(useQdrantRag))]
        public QdrantRAGService qdrantRag;
        public bool useQdrantRag = false;

        [Header("Action Planning")]
        public NPCDialogueActionPlanner actionPlanner;

        [Header("Evidence & Game State")]
        public NPCEvidenceState evidenceState;

        [Title("Remote LLM Configuration")]
        [Header("Remote LLM Configuration")]
        public string remoteHost = "localhost";
        [ShowField(nameof(enableRemoteServer))]
        public int remotePort = 11435;
        [Dropdown(nameof(_cachedModelNames))]
        public string remoteModel = "default-llm";

        [SerializeField, HideInInspector]
        string[] _cachedModelNames = new string[] { "default-llm" };

        [Header("Embedding Backend")]
        [ShowField(nameof(enableRemoteServer))]
        public string remoteEmbeddingHost = "localhost";
        [ShowField(nameof(enableRemoteServer))]
        public int remoteEmbeddingPort = 8080;

        [Title("NPC Profiles")]
        [Header("NPC Profiles")]
        public NPCProfile[] profiles = Array.Empty<NPCProfile>();

        [Title("Dialogue Settings")]
        [Header("Settings")]
        public bool persistHistory = true;
        public bool enableRAG = true;
        public bool rebuildRagFromKnowledgeIfMissing = true;
        public int maxHistoryPerNPC = 20;
        [Tooltip("If true, dialogue systems initialize on Start. If false, they are initialized on-demand (e.g., after player login success).")]
        public bool initializeOnStart = false;

        [ShowInInspector]
        string DirectLocalAiEndpointPreview => $"http://{remoteHost}:{remotePort}/v1/chat/completions";

        [ShowInInspector]
        string ActiveProfilePreview => currentProfile == null ? "<none>" : currentProfile.GetDisplayName();

        [SerializeField, HideInInspector]
        string lastBackendStatus = "Idle";

        [ShowInInspector]
        string LastBackendStatusPreview => lastBackendStatus;

        bool enableRemoteServer = true; // Always true for LocalAI-only mode

        [Title("Events")]
        [Header("Events")]
        public UnityEvent<string> onNPCChanged = new UnityEvent<string>();
        public UnityEvent<string> onResponseStart = new UnityEvent<string>();
        public UnityEvent<string> onResponseUpdated = new UnityEvent<string>();
        public UnityEvent<string, string> onResponseComplete = new UnityEvent<string, string>();
        public UnityEvent<string> onError = new UnityEvent<string>();

        readonly Dictionary<string, NPCProfile> _profilesBySlug = new Dictionary<string, NPCProfile>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<DialogueEntry>> _historyByNpc = new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);

        readonly object _initializationLock = new object();
        Task _initializationTask;
        NPCProfile _currentNPC;
        NPCProfile _responseNPC;
        bool _isResponding;
        bool _ragReady;
        bool _ragUnavailable;
        string _currentPartialResponse = string.Empty;
        string _activePlayerNameOverride = string.Empty;
        ulong? _activePlayerClientIdOverride;

        static NPCFlowLogger Logger => NPCFlowLogger.FindOrCreate();

        public NPCProfile currentProfile => _currentNPC;
        public bool isResponding => _isResponding;
        public bool isInitialized => _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
        public bool isRagAvailable => enableRAG && localRag != null && !_ragUnavailable;
        public NPCProfile[] Profiles => profiles == null ? Array.Empty<NPCProfile>() : profiles.Where(profile => profile != null).ToArray();

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
            if (!string.IsNullOrWhiteSpace(remoteHost) && (remoteHost == "localhost" || remoteHost == "127.0.0.1"))
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
                    Debug.LogWarning($"[NPCDialogueManager] Failed to dynamically resolve remoteHost: {ex.Message}");
                }
            }
            if (!string.IsNullOrWhiteSpace(remoteEmbeddingHost) && (remoteEmbeddingHost == "localhost" || remoteEmbeddingHost == "127.0.0.1"))
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
                    Debug.LogWarning($"[NPCDialogueManager] Failed to dynamically resolve remoteEmbeddingHost: {ex.Message}");
                }
            }
#endif

            using var scope = NPCFlowScope.Start(Logger, NPCFlowStage.SceneBootstrap, source: nameof(NPCDialogueManager));
            try
            {
                AutoAssignReferencesIfNeeded();
                ValidateReferences();
                BuildProfileIndex();
                LoadAllHistories();
                await LoadOrBuildRagEmbeddingsAsync();
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
                actionPlanner = FindAnyObjectByType<NPCDialogueActionPlanner>(FindObjectsInactive.Include);
            }

            if (evidenceState == null)
            {
                evidenceState = FindAnyObjectByType<NPCEvidenceState>(FindObjectsInactive.Include);
            }

            if (chatClient != null)
            {
                chatClient.host = remoteHost;
                chatClient.port = remotePort;
            }
        }

        [Button("Validate Dialogue References")]
        void ValidateReferences()
        {
            if (chatClient == null)
                Logger.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "NPCLocalAIClient reference not set!", source: nameof(NPCDialogueManager));
            if (localRag == null)
                Logger.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                    "LocalRAG reference not set. Prompt-only mode will be used.", source: nameof(NPCDialogueManager));
        }

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
                    string response = responseMsg.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    Debug.Log($"[NPC] Raw response ({response.Length} chars): {response.Substring(0, Mathf.Min(response.Length, 300))}");

                    var wrapper = JsonUtility.FromJson<LocalAIModelsResponse>(response);
                    if (wrapper != null && wrapper.data != null && wrapper.data.Length > 0)
                    {
                        _cachedModelNames = Array.ConvertAll(wrapper.data, m => m.id);
                        string modelList = string.Join(", ", _cachedModelNames);
                        Debug.Log($"[NPC] Found {_cachedModelNames.Length} model(s): {modelList}");
                        Logger.Log(NPCFlowStage.EditorWorkflow, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                            $"Found {_cachedModelNames.Length} model(s): {modelList}",
                            source: nameof(NPCDialogueManager));

#if UNITY_EDITOR
                        UnityEditor.EditorUtility.SetDirty(this);
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
                    }
                    else
                    {
                        string errorDetail = wrapper == null ? "JsonUtility returned null" : $"data array is empty or null (object={wrapper.@object})";
                        Debug.LogError($"[NPC] Failed to parse models response. {errorDetail}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NPC] Failed to fetch models from {url}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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
                    Logger.Log(NPCFlowStage.ProfileIndexBuild, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                        $"Duplicate NPC profile slug detected: {slug}. Keeping the first entry.",
                        source: nameof(NPCDialogueManager), data: new Dictionary<string, object> { ["slug"] = slug });
                    continue;
                }

                _profilesBySlug[slug] = profile;
            }
        }

        void LoadAllHistories()
        {
            _historyByNpc.Clear();

            foreach (NPCProfile profile in Profiles)
            {
                _historyByNpc[profile.GetNpcSlug()] = persistHistory
                    ? NPCHistoryStore.Load(profile.GetHistorySaveFile())
                    : new List<DialogueEntry>();
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
            using var scope = NPCFlowScope.Start(Logger, NPCFlowStage.NPCSwitch, source: nameof(NPCDialogueManager), npcSlug: npcName);
            await InitializeAsync();

            NPCProfile profile = FindProfile(npcName);
            if (profile == null)
            {
                string error = $"NPC '{npcName}' not found";
                Logger.Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Error, NPCFlowLogLevel.Warning,
                    error + "!", source: nameof(NPCDialogueManager));
                onError?.Invoke(error);
                scope.Skipped(error);
                return;
            }

            if (_isResponding)
            {
                CancelRequests();
            }

            _currentNPC = profile;
            scope.Success($"Switched to NPC: {profile.GetDisplayName()}",
                new Dictionary<string, object> { ["npcSlug"] = profile.GetNpcSlug() });

            onNPCChanged?.Invoke(profile.GetDisplayName());
        }

        public void SendMessage(string playerMessage)
        {
            if (_currentNPC == null)
            {
                Logger.Log(NPCFlowStage.RequestStart, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "No NPC selected! Call SwitchToNPC() first.", source: nameof(NPCDialogueManager));
                onError?.Invoke("No NPC selected");
                return;
            }

            if (_isResponding || string.IsNullOrWhiteSpace(playerMessage)) return;

            string trimmedMessage = playerMessage.Trim();
            _isResponding = true;
            _responseNPC = _currentNPC;
            _currentPartialResponse = string.Empty;
            onResponseStart?.Invoke(trimmedMessage);

            _ = SendToLLMAsync(_responseNPC, trimmedMessage);
        }

        public void SetRuntimePlayerContext(string playerName, ulong? clientId = null)
        {
            _activePlayerNameOverride = string.IsNullOrWhiteSpace(playerName) ? string.Empty : playerName.Trim();
            _activePlayerClientIdOverride = clientId;
        }

        public void ClearRuntimePlayerContext()
        {
            _activePlayerNameOverride = string.Empty;
            _activePlayerClientIdOverride = null;
        }

        static bool IsTechnicalCodebaseQuestion(string playerMessage)
        {
            if (string.IsNullOrWhiteSpace(playerMessage)) return false;

            string normalized = playerMessage.Trim().ToLowerInvariant();
            string[] technicalMarkers =
            {
                "codebase", "script", "scripts", "class", "method", "function", "file", "path",
                "namespace", "asmdef", "qdrant", "rag", "collection", "embedding", "localai",
                "npcdialoguemanager", "qdrantragservice", "where is", "which script", "which file",
                "how does", "implemented", "implementation", "search", "retrieval", "runtime", "network"
            };

            foreach (string marker in technicalMarkers)
            {
                if (normalized.Contains(marker)) return true;
            }

            return false;
        }

        async Task<string> SendToLocalAIAsync(NPCProfile profile, string playerMessage, string prompt, string reqId, string slug)
        {
            using var scope = NPCFlowScope.Start(Logger, NPCFlowStage.BackendRequest, source: nameof(NPCDialogueManager) + ".LocalAI", requestId: reqId, npcSlug: slug,
                data: new Dictionary<string, object>
                {
                    ["backend"] = "LocalAI",
                    ["endpoint"] = DirectLocalAiEndpointPreview
                });
            string dialogueMessage = string.Empty;

            // Build conversation history messages
            List<NPCOpenAIMessage> messages = new List<NPCOpenAIMessage>();
            string sysPrompt = NPCProfilePromptComposer.BuildSystemPrompt(profile);
            if (string.IsNullOrWhiteSpace(sysPrompt)) sysPrompt = "You are a helpful assistant.";

            // Inject the authenticated player name so NPCs can personalise responses
            string playerName = ResolveActivePlayerName();
            if (!string.IsNullOrEmpty(playerName) && !string.Equals(playerName, "Player", StringComparison.OrdinalIgnoreCase))
            {
                sysPrompt += $"\n\nThe player who is speaking to you is named '{playerName}'. This is a factual part of the current conversation context. If the player asks what their name is, answer that their name is '{playerName}'. Address them by name naturally when appropriate.";
            }

            messages.Add(new NPCOpenAIMessage { role = "system", content = sysPrompt + "\n" + prompt });

            if (_historyByNpc.TryGetValue(slug, out var history))
            {
                foreach (var entry in history)
                {
                    string role = string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
                    messages.Add(new NPCOpenAIMessage { role = role, content = entry.content });
                }
            }

            messages.Add(new NPCOpenAIMessage { role = "user", content = playerMessage });

            if (chatClient != null)
            {
                string modelName = string.IsNullOrWhiteSpace(remoteModel) ? "default-llm" : remoteModel.Trim();
                if (chatClient.model != modelName)
                {
                    chatClient.model = modelName;
                }

                dialogueMessage = await chatClient.ChatAsync(messages.ToArray(), profile != null ? profile.temperature : (float?)null);
            }

            if (dialogueMessage != null)
            {
                // Strip <think>...</think> block if present
                dialogueMessage = System.Text.RegularExpressions.Regex.Replace(dialogueMessage, @"<think>.*?</think>", "",
                    System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
            }

            scope.Success("LocalAI response received.",
                new Dictionary<string, object>
                {
                    ["responseLength"] = dialogueMessage?.Length ?? 0,
                    ["playerName"] = playerName,
                    ["playerClientId"] = _activePlayerClientIdOverride.HasValue ? _activePlayerClientIdOverride.Value : 0ul
                });
            lastBackendStatus = $"LocalAI response received for player '{playerName}'.";

            return dialogueMessage ?? string.Empty;
        }

        async Task SendToLLMAsync(NPCProfile respondingProfile, string playerMessage)
        {
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            string reqId = logger.NextRequestId();
            string slug = respondingProfile.GetNpcSlug();
            using var scope = NPCFlowScope.Start(logger, NPCFlowStage.DialogueGeneration, source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: slug);
            try
            {
                string prompt = await BuildRAGPromptAsync(respondingProfile, playerMessage, reqId);
                string dialogueMessage = string.Empty;
                NPCDialogueActionPlan actionPlan = actionPlanner != null
                    ? actionPlanner.Plan(playerMessage, respondingProfile)
                    : NPCDialogueActionPlan.None("No action planner available.");

                string actionHint = NPCDialogueActionPlanner.BuildPromptHint(actionPlan);
                if (!string.IsNullOrWhiteSpace(actionHint))
                {
                    prompt += "\n\n" + actionHint;
                }

                dialogueMessage = await SendToLocalAIAsync(respondingProfile, playerMessage, prompt, reqId, slug);
                dialogueMessage = dialogueMessage?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(dialogueMessage))
                {
                    // Add the user message and LLM's response
                    await AppendConversationAsync(respondingProfile, playerMessage, dialogueMessage);

                    // Post-generation: analyze response for detectable game actions
                    if (evidenceState != null)
                    {
                        List<DialogueActionResult> actions = NPCDialogueActionHandler.AnalyzeResponse(
                            dialogueMessage, slug, evidenceState, respondingProfile);
                        foreach (var action in actions)
                        {
                            await AppendConversationAsync(respondingProfile, "[System]", action.ToHistoryLine());
                            Logger.Log(NPCFlowStage.ActionExecution, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                                $"\U0001f3ae {action.ToHistoryLine()}", source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: slug);
                        }
                    }
                }

                onResponseComplete?.Invoke(respondingProfile.GetDisplayName(), dialogueMessage);

                var logData = new Dictionary<string, object>
                {
                    ["playerMessage"] = logger.SummarizeText("player", playerMessage),
                    ["npcResponse"] = logger.SummarizeText("npc", dialogueMessage),
                    ["plannedAction"] = actionPlan.actionType.ToString(),
                    ["actionReason"] = actionPlan.reason
                };
                scope.Success("Dialogue generation complete.", logData);
            }
            catch (Exception ex)
            {
                Logger.Log(NPCFlowStage.DialogueGeneration, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    $"LocalAI Chat error: {ex.Message}",
                    source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: slug,
                    data: new Dictionary<string, object>
                    {
                        ["exceptionType"] = ex.GetType().Name,
                        ["exceptionMessage"] = ex.Message
                    });
                onError?.Invoke(ex.Message);
                scope.Error(ex, "Dialogue generation failed.");
            }
            finally
            {
                _isResponding = false;
                _responseNPC = null;
                _currentPartialResponse = string.Empty;
                ClearRuntimePlayerContext();
            }
        }

        async Task<string> BuildRAGPromptAsync(NPCProfile profile, string playerMessage, string reqId = null)
        {
            using var scope = NPCFlowScope.Start(Logger, NPCFlowStage.ContextRetrieval, source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: profile?.GetNpcSlug());
            string ragKnowledge = string.Empty;
            bool isTechnicalQuestion = IsTechnicalCodebaseQuestion(playerMessage);

            try
            {
                if (useQdrantRag && qdrantRag != null)
                {
                    try
                    {
                        string qdrantResult = await qdrantRag.SearchMemoryAsync(playerMessage, Mathf.Max(1, profile.ragResults), reqId, profile.GetNpcSlug());
                        if (!string.IsNullOrWhiteSpace(qdrantResult))
                        {
                            ragKnowledge = qdrantResult;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log(NPCFlowStage.ContextRetrieval, NPCFlowStatus.Fallback, NPCFlowLogLevel.Warning,
                            $"Qdrant search failed: {e.Message}",
                            source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: profile?.GetNpcSlug(),
                            data: new Dictionary<string, object>
                            {
                                ["exceptionType"] = e.GetType().Name,
                                ["exceptionMessage"] = e.Message,
                                ["source"] = "Qdrant"
                            });
                    }
                }

                if (string.IsNullOrWhiteSpace(ragKnowledge) && enableRAG && !_ragUnavailable && localRag != null && profile != null && !string.IsNullOrWhiteSpace(profile.GetRagCategory()))
                {
                    try
                    {
                        await EnsureRagReadyAsync();
                        (string[] similarResults, _) = await localRag.Search(playerMessage, Mathf.Max(1, profile.ragResults), profile.GetRagCategory());
                        if (similarResults != null && similarResults.Length > 0)
                        {
                            ragKnowledge = string.Join(Environment.NewLine, similarResults.Select(result => $"- {result}"));
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log(NPCFlowStage.ContextRetrieval, NPCFlowStatus.Fallback, NPCFlowLogLevel.Warning,
                            $"RAG search failed: {e.Message}",
                            source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: profile?.GetNpcSlug(),
                            data: new Dictionary<string, object>
                            {
                                ["exceptionType"] = e.GetType().Name,
                                ["exceptionMessage"] = e.Message,
                                ["source"] = "LocalRAG"
                            });
                        _ragUnavailable = true;
                    }
                }
            }
            catch (Exception ex)
            {
                scope.Error(ex, "Failed during ContextRetrieval.");
            }

            if (string.IsNullOrWhiteSpace(ragKnowledge))
            {
                scope.Skipped("No retrieval context found.", new Dictionary<string, object>
                {
                    ["qdrantEnabled"] = useQdrantRag && qdrantRag != null,
                    ["localRagEnabled"] = enableRAG && localRag != null,
                    ["technicalQuestion"] = isTechnicalQuestion
                });

                if (isTechnicalQuestion)
                {
                    return $"Technical question from the player: {playerMessage}\n\nNo retrieved codebase context was found for this request. Be honest about uncertainty. Do not invent file names, classes, methods, or architecture details. If you cannot answer from known context, say that you do not have enough retrieved project information yet.";
                }

                return playerMessage;
            }

            string combinedKnowledge = $"Relevant knowledge for {profile.GetDisplayName()}:\n";
            if (!string.IsNullOrWhiteSpace(ragKnowledge)) combinedKnowledge += $"{ragKnowledge}\n";

            scope.Success("Retrieval context added to prompt.", new Dictionary<string, object>
            {
                ["ragKnowledgeLength"] = ragKnowledge.Length,
                ["ragCategory"] = profile.GetRagCategory(),
                ["technicalQuestion"] = isTechnicalQuestion
            });

            string basePrompt;
            if (isTechnicalQuestion)
            {
                basePrompt =
                    $"{combinedKnowledge}\n" +
                    $"Player technical question: {playerMessage}\n\n" +
                    "This turn is a technical codebase/support question, not a lore exchange. Prioritize factual accuracy over roleplay style. " +
                    "Answer using the retrieved knowledge above. Mention concrete script/class names and file paths when the context includes them. " +
                    "If the retrieved context is insufficient, say exactly what is missing instead of guessing. Avoid inventing project details.";
            }
            else
            {
                basePrompt = $"{combinedKnowledge}\nPlayer message: {playerMessage}\n\nReply in character. Use the knowledge above only if it is relevant and avoid mentioning this instruction block.";
            }

            // Inject evidence state context so the NPC remembers what it has shared
            if (evidenceState != null && profile != null)
            {
                string npcStateLine = evidenceState.BuildNpcStateLine(profile.GetNpcSlug());
                string stateContext = evidenceState.BuildStateContextString();
                if (!string.IsNullOrWhiteSpace(npcStateLine) || !string.IsNullOrWhiteSpace(stateContext))
                {
                    basePrompt += $"\n\n{Environment.NewLine}{npcStateLine}{Environment.NewLine}{stateContext}";
                }
            }

            return basePrompt;
        }

        async Task AppendConversationAsync(NPCProfile profile, string playerMessage, string response)
        {
            if (profile == null) return;

            if (!persistHistory) return;

            List<DialogueEntry> history = GetOrCreateHistory(profile);
            history.Add(new DialogueEntry("user", playerMessage));
            history.Add(new DialogueEntry("assistant", response));
            TrimHistory(history);
            NPCHistoryStore.Save(profile.GetHistorySaveFile(), history);
        }

        List<DialogueEntry> GetOrCreateHistory(NPCProfile profile)
        {
            string slug = profile.GetNpcSlug();
            if (!_historyByNpc.TryGetValue(slug, out List<DialogueEntry> history))
            {
                history = new List<DialogueEntry>();
                _historyByNpc[slug] = history;
            }
            return history;
        }

        static List<DialogueEntry> CloneEntries(List<DialogueEntry> history)
        {
            List<DialogueEntry> clone = new List<DialogueEntry>();
            foreach (DialogueEntry entry in history ?? new List<DialogueEntry>())
            {
                if (entry == null) continue;
                clone.Add(new DialogueEntry
                {
                    role = entry.role,
                    content = entry.content,
                    timestampUtc = entry.timestampUtc
                });
            }

            return clone;
        }

        void TrimHistory(List<DialogueEntry> history)
        {
            int maxEntries = Mathf.Max(1, maxHistoryPerNPC) * 2;
            if (history.Count > maxEntries)
            {
                history.RemoveRange(0, history.Count - maxEntries);
            }
        }

        NPCProfile FindProfile(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName)) return null;

            string key = npcName.Trim();
            if (_profilesBySlug.TryGetValue(key, out NPCProfile bySlug)) return bySlug;

            return Profiles.FirstOrDefault(profile =>
                string.Equals(profile.GetDisplayName(), key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.name, key, StringComparison.OrdinalIgnoreCase));
        }

        async Task LoadOrBuildRagEmbeddingsAsync()
        {
            if (localRag == null || string.IsNullOrWhiteSpace(ragEmbeddingPath))
            {
                Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Skipped, NPCFlowLogLevel.Debug,
                    "RAG is disabled. Skipping RAG initialization.",
                    source: nameof(NPCDialogueManager));
                return;
            }

            if (useQdrantRag)
            {
                Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Skipped, NPCFlowLogLevel.Debug,
                    "Qdrant RAG is enabled. Skipping local RAG index load/rebuild.",
                    source: nameof(NPCDialogueManager));
                return;
            }

            try
            {
                await EnsureRagReadyAsync();

                if (IsRagMetadataCurrent(out string metadataReason))
                {
                    bool loaded = await localRag.LoadFile(ragEmbeddingPath);
                    if (loaded)
                    {
                        Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                            $"RAG embeddings loaded from {ragEmbeddingPath}",
                            source: nameof(NPCDialogueManager),
                            data: new Dictionary<string, object> { ["ragEmbeddingPath"] = ragEmbeddingPath });
                        return;
                    }

                    Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                        $"RAG metadata is current but index load failed. Rebuilding {ragEmbeddingPath}.",
                        source: nameof(NPCDialogueManager),
                        data: new Dictionary<string, object> { ["ragEmbeddingPath"] = ragEmbeddingPath });
                }
                else
                {
                    Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Start, NPCFlowLogLevel.Debug,
                        $"RAG index rebuild required: {metadataReason}",
                        source: nameof(NPCDialogueManager));
                }

                if (!rebuildRagFromKnowledgeIfMissing) return;

                bool built = await NPCRAGImporter.RebuildAsync(localRag, Profiles, ragEmbeddingPath);
                if (built)
                {
                    Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                        $"RAG embeddings rebuilt and saved to {ragEmbeddingPath}",
                        source: nameof(NPCDialogueManager),
                        data: new Dictionary<string, object> { ["ragEmbeddingPath"] = ragEmbeddingPath });
                }
            }
            catch (Exception e)
            {
                Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Fallback, NPCFlowLogLevel.Warning,
                    $"RAG initialization skipped: {e.Message}",
                    source: nameof(NPCDialogueManager),
                    data: new Dictionary<string, object>
                    {
                        ["exceptionType"] = e.GetType().Name,
                        ["exceptionMessage"] = e.Message
                    });
                _ragUnavailable = true;
            }
        }

        bool IsRagMetadataCurrent(out string reason)
        {
            reason = string.Empty;
            NPCRAGMetadata expected = NPCRAGMetadataStore.CreateExpected(
                ragEmbeddingPath,
                Profiles,
                NPCRAGImporter.MaxChunkCharacters
            );

            if (!NPCRAGMetadataStore.TryLoad(ragEmbeddingPath, out NPCRAGMetadata actual))
            {
                reason = "metadata sidecar is missing or unreadable";
                return false;
            }

            return NPCRAGMetadataStore.IsCurrent(actual, expected, out reason);
        }

        async Task EnsureRagReadyAsync()
        {
            if (localRag == null || _ragReady) return;
            if (_ragUnavailable) throw new InvalidOperationException("RAG is unavailable.");
            if (localRag.search == null) localRag.UpdateGameObjects();

            if (localRag.search == null || localRag.search.llmEmbedder == null)
            {
                await Task.Yield();
                return;
            }

            Exception lastException = null;
            for (int attempt = 0; attempt < 120; attempt++)
            {
                try
                {
                    List<float> embeddings = await localRag.search.llmEmbedder.Embeddings("ready");
                    if (embeddings == null || embeddings.Count == 0)
                    {
                        throw new InvalidOperationException("RAG embedder returned empty embeddings.");
                    }

                    _ragReady = true;
                    return;
                }
                catch (Exception e)
                {
                    lastException = e;
                    if (!IsTransientEmbedderStartupError(e)) throw;
                    await Task.Delay(100);
                }
            }

            throw new TimeoutException($"RAG embedder was not ready after startup wait: {lastException?.Message}");
        }

        static bool IsTransientEmbedderStartupError(Exception exception)
        {
            string message = exception.Message;
            return message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public async Task AddNPCKnowledge(string npcName, string knowledgeText)
        {
            if (localRag == null)
            {
                Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "RAG not configured!", source: nameof(NPCDialogueManager));
                return;
            }

            NPCProfile profile = FindProfile(npcName);
            if (profile == null) return;

            await EnsureRagReadyAsync();
            await localRag.Add(knowledgeText, profile.GetRagCategory());
            localRag.SaveFile(ragEmbeddingPath);

            Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                "AddNPCKnowledge saved RAG without updating source metadata. Prefer rebuilding from source files for durable indexes.",
                source: nameof(NPCDialogueManager), npcSlug: profile.GetNpcSlug());
        }

        public void SaveRAGEmbeddings()
        {
            if (localRag != null)
            {
                localRag.SaveFile(ragEmbeddingPath);
                Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                    "SaveRAGEmbeddings saved RAG without updating source metadata. Prefer NPCRAGImporter.RebuildAsync for durable indexes.",
                    source: nameof(NPCDialogueManager));
            }
        }

        public List<DialogueEntry> GetHistory(string npcName)
        {
            NPCProfile profile = FindProfile(npcName);
            if (profile == null) return new List<DialogueEntry>();
            return CloneEntries(GetOrCreateHistory(profile));
        }

        public Dictionary<string, List<DialogueEntry>> CaptureHistorySnapshot()
        {
            var snapshot = new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (NPCProfile profile in Profiles)
            {
                string slug = profile.GetNpcSlug();
                snapshot[slug] = CloneEntries(GetOrCreateHistory(profile));
            }

            return snapshot;
        }

        public void ApplyHistorySnapshot(Dictionary<string, List<DialogueEntry>> historyByNpc)
        {
            _historyByNpc.Clear();
            foreach (NPCProfile profile in Profiles)
            {
                string slug = profile.GetNpcSlug();
                if (historyByNpc != null && historyByNpc.TryGetValue(slug, out List<DialogueEntry> history))
                {
                    _historyByNpc[slug] = NPCHistoryStore.NormalizeForChatTemplate(CloneEntries(history), out _);
                }
                else
                {
                    _historyByNpc[slug] = new List<DialogueEntry>();
                }
            }
        }

        public NPCEvidenceStateSnapshot CaptureEvidenceSnapshot()
        {
            return evidenceState != null ? evidenceState.CreateSnapshot() : new NPCEvidenceStateSnapshot();
        }

        public void ApplyEvidenceSnapshot(NPCEvidenceStateSnapshot snapshot)
        {
            if (evidenceState == null) return;
            evidenceState.ApplySnapshot(snapshot);
        }

        public void ClearHistory(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
            {
                foreach (NPCProfile profile in Profiles)
                {
                    _historyByNpc[profile.GetNpcSlug()] = new List<DialogueEntry>();
                    NPCHistoryStore.Delete(profile.GetHistorySaveFile());
                }
            }
            else
            {
                NPCProfile profile = FindProfile(npcName);
                if (profile == null) return;
                _historyByNpc[profile.GetNpcSlug()] = new List<DialogueEntry>();
                NPCHistoryStore.Delete(profile.GetHistorySaveFile());
            }
        }

        public void CancelRequests()
        {
            _isResponding = false;
            _responseNPC = null;
            ClearRuntimePlayerContext();
        }

        public string[] GetNPCNames()
        {
            return Profiles.Select(profile => profile.GetNpcSlug()).ToArray();
        }

        void OnDestroy()
        {
            CancelRequests();
        }

        string ResolveActivePlayerName()
        {
            if (!string.IsNullOrWhiteSpace(_activePlayerNameOverride))
            {
                return _activePlayerNameOverride;
            }

            return AuthNetworkBridge.ActivePlayerName;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) return;

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
