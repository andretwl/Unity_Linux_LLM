using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EditorAttributes;
using LLMUnity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace NPCSystem
{
    [DefaultExecutionOrder(-1500)]
    public class NPCDialogueManager : MonoBehaviour
    {
        [Title("LLMUnity References")]
        [HelpBox("The active scene currently uses direct LocalAI HTTP when Use Remote Server is enabled. Keep LLMUnity references assigned for local RAG and compatibility, but avoid making direct LocalAI depend on local LLMAgent startup.", MessageMode.Log, drawAbove: true)]
        [Required]
        [Header("LLM Reference")]
        public LLM llm;

        [Required]
        [Header("LLM Agent")]
        public LLMAgent llmAgent;

        [Title("Local RAG")]
        [Header("RAG Reference")]
        public RAG rag;
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

        [Title("Direct LocalAI HTTP")]
        [Header("Remote LLM Configuration")]
        public bool useRemoteServer = false;
        [ShowField(nameof(useRemoteServer))]
        public string remoteHost = "localhost";
        [ShowField(nameof(useRemoteServer))]
        public int remotePort = 11435;
        [Dropdown(nameof(_cachedModelNames))]
        public string remoteModel = "default-llm";

        [SerializeField, HideInInspector]
        string[] _cachedModelNames = new string[] { "default-llm" };

        [Title("Embedding Backend")]
        [Header("Embedding Backend")]
        public bool forceRemoteEmbedder = false;
        [ShowField(nameof(forceRemoteEmbedder))]
        public string remoteEmbeddingHost = "localhost";
        [ShowField(nameof(forceRemoteEmbedder))]
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
        public bool enablePreloadedLoraSwitching = false;

        [ShowInInspector]
        string DirectLocalAiEndpointPreview => $"http://{remoteHost}:{remotePort}/v1/chat/completions";

        [ShowInInspector]
        string ActiveProfilePreview => currentProfile == null ? "<none>" : currentProfile.GetDisplayName();

        [Title("Events")]
        [Header("Events")]
        public UnityEvent<string> onNPCChanged = new UnityEvent<string>();
        public UnityEvent<string> onResponseStart = new UnityEvent<string>();
        public UnityEvent<string> onResponseUpdated = new UnityEvent<string>();
        public UnityEvent<string, string> onResponseComplete = new UnityEvent<string, string>();
        public UnityEvent<string> onError = new UnityEvent<string>();

        readonly Dictionary<string, NPCProfile> _profilesBySlug = new Dictionary<string, NPCProfile>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<DialogueEntry>> _historyByNpc = new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _preloadedLoras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        readonly object _initializationLock = new object();
        Task _initializationTask;
        NPCProfile _currentNPC;
        NPCProfile _responseNPC;
        bool _isResponding;
        bool _ragReady;
        bool _ragUnavailable;
        string _currentPartialResponse = string.Empty;

        static NPCFlowLogger Logger => NPCFlowLogger.FindOrCreate();

        public NPCProfile currentProfile => _currentNPC;
        public bool isResponding => _isResponding;
        public bool isInitialized => _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
        public bool isRagAvailable => enableRAG && rag != null && !_ragUnavailable;
        public NPCProfile[] Profiles => profiles == null ? Array.Empty<NPCProfile>() : profiles.Where(profile => profile != null).ToArray();

        void Start()
        {
            _ = InitializeAsync();
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
            if (llmAgent == null)
            {
                llmAgent = FindAnyObjectByType<LLMAgent>(FindObjectsInactive.Include);
            }

            if (llm == null)
            {
                llm = llmAgent != null ? llmAgent.llm : FindAnyObjectByType<LLM>(FindObjectsInactive.Include);
            }

            if (rag == null)
            {
                rag = FindAnyObjectByType<RAG>(FindObjectsInactive.Include);
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

            // Sync host and port settings dynamically to prevent conflicts
            if (useRemoteServer)
            {
                if (llmAgent != null)
                {
                    llmAgent.host = remoteHost;
                    llmAgent.port = remotePort;
                }

                if (llm != null)
                {
                    llm.port = remotePort;
                }
            }

            LLMEmbedder embedder = FindAnyObjectByType<LLMEmbedder>(FindObjectsInactive.Include);
            if (embedder != null && (forceRemoteEmbedder || useRemoteServer))
            {
                embedder.remote = true;
                embedder.host = remoteEmbeddingHost;
                embedder.port = remoteEmbeddingPort;
            }
        }

        [Button("Validate Dialogue References")]
        void ValidateReferences()
        {
            if (llm == null)
                Logger.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "LLM reference not set!", source: nameof(NPCDialogueManager));
            if (llmAgent == null)
                Logger.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "LLMAgent reference not set!", source: nameof(NPCDialogueManager));
            if (rag == null)
                Logger.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                    "RAG reference not set. Prompt-only mode will be used.", source: nameof(NPCDialogueManager));
        }

        [Button("Fetch Models from LocalAI")]
        void FetchAvailableModelsFromLocalAI()
        {
            if (!useRemoteServer)
            {
                UnityEngine.Debug.LogWarning("[NPC] Remote server is not enabled. Enable Use Remote Server first.");
                return;
            }

            string url = $"http://{remoteHost}:{remotePort}/v1/models";
            UnityEngine.Debug.Log($"[NPC] Fetching models from {url}...");

            try
            {
                var handler = new System.Net.Http.HttpClientHandler();
                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    client.Timeout = System.TimeSpan.FromSeconds(10);
                    var responseTask = client.GetAsync(url);
                    var responseMsg = responseTask.GetAwaiter().GetResult();
                    string response = responseMsg.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    UnityEngine.Debug.Log($"[NPC] Raw response ({response.Length} chars): {response.Substring(0, Mathf.Min(response.Length, 300))}");

                    var wrapper = UnityEngine.JsonUtility.FromJson<LocalAIModelsResponse>(response);
                    if (wrapper != null && wrapper.data != null && wrapper.data.Length > 0)
                    {
                        _cachedModelNames = System.Array.ConvertAll(wrapper.data, m => m.id);
                        string modelList = string.Join(", ", _cachedModelNames);
                        UnityEngine.Debug.Log($"[NPC] Found {_cachedModelNames.Length} model(s): {modelList}");
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
                        UnityEngine.Debug.LogError($"[NPC] Failed to parse models response. {errorDetail}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[NPC] Failed to fetch models from {url}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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

        public void RegisterPreloadedLoras(IEnumerable<string> loraPaths)
        {
            _preloadedLoras.Clear();
            if (loraPaths == null) return;

            foreach (string loraPath in loraPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                _preloadedLoras.Add(loraPath.Trim());
            }

            if (_preloadedLoras.Count > 0)
            {
                enablePreloadedLoraSwitching = true;
            }
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
            ApplyProfileSettings(profile);
            ApplyProfileLoraWeights(profile);
            await RestoreHistoryIntoAgentAsync(profile);

            onNPCChanged?.Invoke(profile.GetDisplayName());
            scope.Success($"Switched to NPC: {profile.GetDisplayName()}",
                new Dictionary<string, object> { ["npcSlug"] = profile.GetNpcSlug() });
        }

        void ApplyProfileSettings(NPCProfile profile)
        {
            if (llmAgent == null || profile == null) return;

            llmAgent.systemPrompt = NPCProfilePromptComposer.BuildSystemPrompt(profile);
            llmAgent.temperature = profile.temperature;
            llmAgent.topP = profile.topP;
            llmAgent.minP = profile.minP;
            llmAgent.topK = profile.topK;
            llmAgent.repeatPenalty = profile.repeatPenalty;
            llmAgent.numPredict = profile.maxTokens;
            llmAgent.save = string.Empty;
        }

        void ApplyProfileLoraWeights(NPCProfile activeProfile)
        {
            if (!enablePreloadedLoraSwitching || llm == null || _preloadedLoras.Count == 0) return;

            Dictionary<string, float> loraWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in _preloadedLoras)
            {
                loraWeights[path] = 0f;
            }

            string activeLoraPath = activeProfile.GetLoraAdapterPath();
            if (!string.IsNullOrWhiteSpace(activeLoraPath))
            {
                if (_preloadedLoras.Contains(activeLoraPath))
                {
                    loraWeights[activeLoraPath] = activeProfile.loraWeight;
                }
                else
                {
                    Logger.Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                        $"LoRA '{activeLoraPath}' is configured on {activeProfile.GetDisplayName()} but was not preloaded. Skipping runtime LoRA switch.",
                        source: nameof(NPCDialogueManager), npcSlug: activeProfile.GetNpcSlug(),
                        data: new Dictionary<string, object> { ["loraPath"] = activeLoraPath });
                }
            }

            llm.SetLoraWeights(loraWeights);
        }

        async Task RestoreHistoryIntoAgentAsync(NPCProfile profile)
        {
            if (profile == null) return;

            string npcSlug = profile.GetNpcSlug();
            if (!_historyByNpc.TryGetValue(npcSlug, out List<DialogueEntry> history))
            {
                history = new List<DialogueEntry>();
                _historyByNpc[npcSlug] = history;
            }

            history = NPCHistoryStore.NormalizeForChatTemplate(history, out int droppedCount);
            _historyByNpc[npcSlug] = history;
            if (droppedCount > 0)
            {
                Logger.Log(NPCFlowStage.HistoryRestore, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                    $"Dropped {droppedCount} malformed history entr{(droppedCount == 1 ? "y" : "ies")} for {profile.GetDisplayName()} before restoring history.",
                    source: nameof(NPCDialogueManager), npcSlug: npcSlug,
                    data: new Dictionary<string, object> { ["droppedCount"] = droppedCount });
                if (persistHistory) NPCHistoryStore.Save(profile.GetHistorySaveFile(), history);
            }

            if (useRemoteServer)
            {
                Logger.Log(NPCFlowStage.HistoryRestore, NPCFlowStatus.Skipped, NPCFlowLogLevel.Debug,
                    "Direct LocalAI mode uses serialized dialogue history and does not restore into LLMAgent.",
                    source: nameof(NPCDialogueManager), npcSlug: npcSlug,
                    data: new Dictionary<string, object> { ["entryCount"] = history.Count });
                return;
            }

            if (llmAgent == null) return;

            await llmAgent.ClearHistory();

            foreach (DialogueEntry entry in history)
            {
                if (string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase))
                    await llmAgent.AddAssistantMessage(entry.content);
                else
                    await llmAgent.AddUserMessage(entry.content);
            }
        }

        public new void SendMessage(string playerMessage)
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

        async Task<string> SendToLocalAIAsync(NPCProfile profile, string playerMessage, string prompt, string reqId, string slug)
        {
            using var scope = NPCFlowScope.Start(Logger, NPCFlowStage.DialogueGeneration, source: nameof(NPCDialogueManager) + ".LocalAI", requestId: reqId, npcSlug: slug);
            string dialogueMessage = string.Empty;
            
            // Build conversation history messages
            List<OpenAIMessage> messages = new List<OpenAIMessage>();
            string sysPrompt = NPCProfilePromptComposer.BuildSystemPrompt(profile);
            if (string.IsNullOrWhiteSpace(sysPrompt)) sysPrompt = "You are a helpful assistant.";

            // Inject the authenticated player name so NPCs can personalise responses
            string playerName = AuthNetworkBridge.ActivePlayerName;
            if (!string.IsNullOrEmpty(playerName) && !string.Equals(playerName, "Player", StringComparison.OrdinalIgnoreCase))
            {
                sysPrompt += $"\n\nThe player who is speaking to you is named '{playerName}'. Address them by name naturally when appropriate.";
            }

            messages.Add(new OpenAIMessage { role = "system", content = sysPrompt + "\n" + prompt });

            if (_historyByNpc.TryGetValue(slug, out var history))
            {
                foreach (var entry in history)
                {
                    string role = string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
                    messages.Add(new OpenAIMessage { role = role, content = entry.content });
                }
            }
            
            messages.Add(new OpenAIMessage { role = "user", content = playerMessage });

            // Create request payload
            string modelName = string.IsNullOrWhiteSpace(remoteModel) ? "default-llm" : remoteModel.Trim();
            string payload = "{\"model\":\"" + modelName + "\",\"messages\":[";
            for (int i = 0; i < messages.Count; i++)
            {
                payload += $"{{\"role\":\"{messages[i].role}\",\"content\":\"{EscapeJson(messages[i].content)}\"}}";
                if (i < messages.Count - 1) payload += ",";
            }
            payload += "]}";

            string uri = $"http://{remoteHost}:{remotePort}/v1/chat/completions";
            using (UnityWebRequest request = new UnityWebRequest(uri, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(payload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
                {
                    throw new Exception($"LocalAI request failed: {request.error}\n{request.downloadHandler.text}");
                }

                string responseJson = request.downloadHandler.text;
                var localAiResponse = JsonUtility.FromJson<OpenAIResponse>(responseJson);
                
                if (localAiResponse != null && localAiResponse.choices != null && localAiResponse.choices.Length > 0)
                {
                    string rawContent = localAiResponse.choices[0].message.content;
                    
                    // Strip <think>...</think> block if present
                    rawContent = System.Text.RegularExpressions.Regex.Replace(rawContent, @"<think>.*?</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

                    dialogueMessage = rawContent;
                }
                scope.Success("LocalAI response received.",
                    new Dictionary<string, object> { ["responseLength"] = dialogueMessage?.Length ?? 0 });
            }

            return dialogueMessage;
        }

        private string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        async Task SendToLLMAsync(NPCProfile respondingProfile, string playerMessage)
        {
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            string reqId = logger.NextRequestId();
            string slug = respondingProfile.GetNpcSlug();
            using var scope = NPCFlowScope.Start(logger, NPCFlowStage.DialogueGeneration, source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: slug);
            try
            {
                await RestoreHistoryIntoAgentAsync(respondingProfile);

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

                if (useRemoteServer)
                {
                    dialogueMessage = await SendToLocalAIAsync(respondingProfile, playerMessage, prompt, reqId, slug);
                }
                else
                {
                    dialogueMessage = await llmAgent.Chat(prompt, OnLLMResponse, null, false) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(dialogueMessage)) dialogueMessage = _currentPartialResponse;
                }
                
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
                                $"🎮 {action.ToHistoryLine()}", source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: slug);
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
                    $"LLM Chat error: {ex.Message}",
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
            }
        }

        void OnLLMResponse(string text)
        {
            _currentPartialResponse = text ?? string.Empty;
            onResponseUpdated?.Invoke(_currentPartialResponse);
        }

        async Task<string> BuildRAGPromptAsync(NPCProfile profile, string playerMessage, string reqId = null)
        {
            using var scope = NPCFlowScope.Start(Logger, NPCFlowStage.ContextRetrieval, source: nameof(NPCDialogueManager), requestId: reqId, npcSlug: profile?.GetNpcSlug());
            string ragKnowledge = string.Empty;

            try
            {
                if (useQdrantRag && qdrantRag != null)
                {
                    try
                    {
                        string qdrantResult = await qdrantRag.SearchMemoryAsync(rag, playerMessage, Mathf.Max(1, profile.ragResults), reqId, profile.GetNpcSlug());
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
                
                if (string.IsNullOrWhiteSpace(ragKnowledge) && enableRAG && !_ragUnavailable && rag != null && profile != null && !string.IsNullOrWhiteSpace(profile.GetRagCategory()))
                {
                    try
                    {
                        await EnsureRagReadyAsync();
                        (string[] similarResults, _) = await rag.Search(playerMessage, Mathf.Max(1, profile.ragResults), profile.GetRagCategory());
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
                    ["localRagEnabled"] = enableRAG && rag != null
                });
                return playerMessage;
            }

            string combinedKnowledge = $"Relevant knowledge for {profile.GetDisplayName()}:\n";
            if (!string.IsNullOrWhiteSpace(ragKnowledge)) combinedKnowledge += $"{ragKnowledge}\n";

            scope.Success("Retrieval context added to prompt.", new Dictionary<string, object>
            {
                ["ragKnowledgeLength"] = ragKnowledge.Length,
                ["ragCategory"] = profile.GetRagCategory()
            });

            string basePrompt = $"{combinedKnowledge}\nPlayer message: {playerMessage}\n\nReply in character. Use the knowledge above only if it is relevant and avoid mentioning this instruction block.";

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

            if (!useRemoteServer && llmAgent != null)
            {
                await llmAgent.AddUserMessage(playerMessage);
                await llmAgent.AddAssistantMessage(response);
            }

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
            if (rag == null || string.IsNullOrWhiteSpace(ragEmbeddingPath))
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
                    bool loaded = await rag.Load(ragEmbeddingPath);
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

                bool built = await NPCRAGImporter.RebuildAsync(rag, Profiles, ragEmbeddingPath);
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
                GetRagEmbeddingLLM(),
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
            if (rag == null || _ragReady) return;
            if (_ragUnavailable) throw new InvalidOperationException("RAG is unavailable.");
            if (rag.search == null) rag.UpdateGameObjects();

            LLM ragLlm = GetRagEmbeddingLLM();

            if (ragLlm != null)
            {
                await ragLlm.WaitUntilReady();
            }

            if (rag.search == null || rag.search.llmEmbedder == null)
            {
                await Task.Yield();
                return;
            }

            Exception lastException = null;
            for (int attempt = 0; attempt < 120; attempt++)
            {
                try
                {
                    List<float> embeddings = await rag.search.llmEmbedder.Embeddings("ready");
                    if (embeddings == null || embeddings.Count == 0)
                    {
                        throw new InvalidOperationException("RAG embedder returned empty embeddings. Check that the RAG LLM object is using a valid embedding model and embedding length.");
                    }

                    _ragReady = true;
                    return;
                }
                catch (Exception e)
                {
                    lastException = e;
                    if (!IsTransientLlmClientStartupError(e)) throw;
                    await Task.Delay(100);
                }
            }

            throw new TimeoutException($"RAG embedder was not ready after startup wait: {lastException?.Message}");
        }

        static bool IsTransientLlmClientStartupError(Exception exception)
        {
            string message = exception.Message;
            return message.IndexOf("caller not initialized", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("llmClient not initialized", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("service is not available", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        LLM GetRagEmbeddingLLM()
        {
            return rag != null && rag.search != null && rag.search.llmEmbedder != null
                ? rag.search.llmEmbedder.llm
                : llm;
        }

        public async Task AddNPCKnowledge(string npcName, string knowledgeText)
        {
            if (rag == null)
            {
                Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "RAG not configured!", source: nameof(NPCDialogueManager));
                return;
            }

            NPCProfile profile = FindProfile(npcName);
            if (profile == null) return;

            await EnsureRagReadyAsync();
            await rag.Add(knowledgeText, profile.GetRagCategory());
            rag.Save(ragEmbeddingPath);

            Logger.Log(NPCFlowStage.LocalRagReady, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                "AddNPCKnowledge saved RAG without updating source metadata. Prefer rebuilding from source files for durable indexes.",
                source: nameof(NPCDialogueManager), npcSlug: profile.GetNpcSlug());
        }

        public void SaveRAGEmbeddings()
        {
            if (rag != null)
            {
                rag.Save(ragEmbeddingPath);
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

            if (_currentNPC != null)
            {
                _ = RestoreHistoryIntoAgentAsync(_currentNPC);
            }
        }

        public async Task WarmupCurrentNPCAsync()
        {
            if (_currentNPC == null || llmAgent == null) return;
            await llmAgent.Warmup();
        }

        public void CancelRequests()
        {
            if (llmAgent != null) llmAgent.CancelRequests();
            _isResponding = false;
            _responseNPC = null;
        }

        public string[] GetNPCNames()
        {
            return Profiles.Select(profile => profile.GetNpcSlug()).ToArray();
        }

        void OnDestroy()
        {
            CancelRequests();
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
    public class OpenAIMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class OpenAIRequest
    {
        public string model;
        public OpenAIMessage[] messages;
        public bool stream;
        public string response_format;
    }

    [Serializable]
    public class OpenAIResponseChoice
    {
        public OpenAIMessage message;
    }

    [Serializable]
    public class OpenAIResponse
    {
        public OpenAIResponseChoice[] choices;
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
