using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Owns the dialogue session flow — isResponding state, player context,
    /// all LLM/LocalAI send paths, prompt building, and cancellation.
    ///
    /// Created / initialized by NPCDialogueManager (Phase 3 extraction).
    /// </summary>
    [DefaultExecutionOrder(-1200)]
    public class NPCDialogueSessionService : MonoBehaviour
    {
        // ────────────────────────────────────────────── State ────
        bool _isResponding;
        NPCProfile _responseNPC;
        string _activePlayerNameOverride = string.Empty;
        ulong? _activePlayerClientIdOverride;

        // ────────────────────────────────────────── Dependencies ────
        NPCLocalAIClient _chatClient;
        NPCDialogueHistoryService _historyService;
        NPCDialogueRetrievalService _retrievalService;
        NPCDialogueActionPlanner _actionPlanner;
        NPCEvidenceState _evidenceState;
        PlayerDialogueContextService _contextService;
        string _remoteHost;
        int _remotePort;
        NPCProfile[] _profiles;

        /// Model string sourced from NPCDialogueManager.remoteModel during Initialize.
        /// Passed to NPCLocalAIClient.ChatAsync as modelOverride at request time —
        /// the AIClient never reads its own model field for dialogue requests.
        string _chatModel;

        // ────────────────────────────────────────────── Events ────
        public event Action<string> OnResponseStart;
        public event Action<string, string> OnResponseComplete;
        public event Action<string> OnError;

        // ────────────────────────────────────────── Properties ────
        public bool IsResponding => _isResponding;
        public string ActivePlayerName => ResolveActivePlayerName();

        // ────────────────────────────────────────── Initialize ────

        /// <summary>
        /// Initialise the service with all dependencies from NPCDialogueManager.
        /// Call once during manager initialisation before any other methods.
        /// </summary>
        public void Initialize(
            NPCLocalAIClient chatClient,
            NPCDialogueHistoryService historyService,
            NPCDialogueRetrievalService retrievalService,
            NPCDialogueActionPlanner actionPlanner,
            NPCEvidenceState evidenceState,
            PlayerDialogueContextService contextService,
            string remoteHost,
            int remotePort,
            string chatModel,
            NPCProfile[] profiles
        )
        {
            _chatClient = chatClient;
            _historyService = historyService;
            _retrievalService = retrievalService;
            _actionPlanner = actionPlanner;
            _evidenceState = evidenceState;
            _contextService = contextService;
            _remoteHost = remoteHost ?? "localhost";
            _remotePort = remotePort;
            _chatModel = chatModel ?? "";
            _profiles = profiles;
        }

        // ────────────────────────────────────────── Public API ────

        /// <summary>
        /// Start a dialogue turn for the currently active NPC.
        /// Validates state, sets responding flag, and fires the async LLM chain.
        /// </summary>
        public void SendDialogueMessage(string playerMessage, NPCProfile currentNpc)
        {
            if (_isResponding || string.IsNullOrWhiteSpace(playerMessage))
                return;

            string trimmedMessage = playerMessage.Trim();
            _isResponding = true;
            _responseNPC = currentNpc;
            OnResponseStart?.Invoke(trimmedMessage);

            _ = SendToLLMAsync(_responseNPC, trimmedMessage);
        }

        /// <summary>
        /// Override the player-name context used for NPC personalisation.
        /// Pass null/empty to clear the override (reverts to AuthNetworkBridge).
        /// </summary>
        public void SetRuntimePlayerContext(string playerName, ulong? clientId = null)
        {
            _activePlayerNameOverride = string.IsNullOrWhiteSpace(playerName)
                ? string.Empty
                : playerName.Trim();
            _activePlayerClientIdOverride = clientId;
        }

        /// <summary>
        /// Clear any runtime player-context override.
        /// </summary>
        public void ClearRuntimePlayerContext()
        {
            _activePlayerNameOverride = string.Empty;
            _activePlayerClientIdOverride = null;
        }

        /// <summary>
        /// Cancel any in-flight request and reset session state.
        /// </summary>
        public void CancelRequests()
        {
            _isResponding = false;
            _responseNPC = null;
            _chatClient?.CancelActiveRequest();
            ClearRuntimePlayerContext();
        }

        /// <summary>
        /// Sync remote-endpoint host/port at runtime (model is owned by Manager and synced to chatClient).
        /// </summary>
        public void SyncRemoteConfig(string host, int port)
        {
            _remoteHost = host;
            _remotePort = port;
        }

        // ─────────────────────────────────────── Dialogue Flow ────

        async Task SendToLLMAsync(NPCProfile respondingProfile, string playerMessage)
        {
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            string reqId = logger.NextRequestId();
            string slug = respondingProfile.GetNpcSlug();

            using var scope = NPCFlowScope.Start(
                logger,
                NPCFlowStage.DialogueGeneration,
                source: nameof(NPCDialogueSessionService),
                requestId: reqId,
                npcSlug: slug
            );

            using var turnSpan = DatadogTracer.StartSpan(
                "dialogue.turn",
                service: "unity-dedicated-server",
                resource: $"NPC/{slug}",
                type: "dialogue",
                tags: new[]
                {
                    $"npc:{slug}",
                    $"request_id:{reqId}",
                }
            );

            var turnSw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                string prompt = await BuildRAGPromptAsync(respondingProfile, playerMessage, reqId);
                string dialogueMessage = string.Empty;

                NPCDialogueActionPlan actionPlan =
                    _actionPlanner != null
                        ? _actionPlanner.Plan(
                            playerMessage,
                            respondingProfile,
                            trustScore: ResolveTrustScore(slug)
                        )
                        : NPCDialogueActionPlan.None("No action planner available.");

                string actionHint = NPCDialogueActionPlanner.BuildPromptHint(actionPlan);
                if (!string.IsNullOrWhiteSpace(actionHint))
                {
                    prompt += "\n\n" + actionHint;
                }

                dialogueMessage = await SendToLocalAIAsync(
                    respondingProfile,
                    playerMessage,
                    prompt,
                    reqId,
                    slug
                );
                dialogueMessage = dialogueMessage?.Trim() ?? string.Empty;
                dialogueMessage = NPCFlowTextSanitizer.CleanDialogueText(dialogueMessage);

                if (!string.IsNullOrWhiteSpace(dialogueMessage))
                {
                    await _historyService.AppendConversationAsync(
                        respondingProfile,
                        playerMessage,
                        dialogueMessage
                    );

                    if (_evidenceState != null)
                    {
                        List<DialogueActionResult> actions =
                            NPCDialogueActionHandler.AnalyzeResponse(
                                dialogueMessage,
                                slug,
                                _evidenceState,
                                respondingProfile
                            );
                        foreach (var action in actions)
                        {
                            await _historyService.AppendConversationAsync(
                                respondingProfile,
                                "[System]",
                                action.ToHistoryLine()
                            );
                            logger.Log(
                                NPCFlowStage.ActionExecution,
                                NPCFlowStatus.Success,
                                NPCFlowLogLevel.Info,
                                $"\U0001f3ae {action.ToHistoryLine()}",
                                source: nameof(NPCDialogueSessionService),
                                requestId: reqId,
                                npcSlug: slug
                            );
                        }
                    }
                }

                turnSw.Stop();
                turnSpan.SetTag("action_type", actionPlan.ActionType.ToString());
                turnSpan.SetTag("has_response", string.IsNullOrEmpty(dialogueMessage) ? "false" : "true");
                turnSpan.SetTag("status", "success");
                DatadogMetricsService.Timer("dialogue.session.turn.duration", turnSw.ElapsedMilliseconds, tags: new[]
                {
                    $"npc:{slug}",
                    $"action_type:{actionPlan.ActionType}",
                    $"has_response:{(string.IsNullOrEmpty(dialogueMessage) ? "false" : "true")}",
                });
                DatadogMetricsService.Increment("dialogue.session.turn.count", tags: new[]
                {
                    $"npc:{slug}",
                    $"action_type:{actionPlan.ActionType}",
                });

                OnResponseComplete?.Invoke(respondingProfile.GetDisplayName(), dialogueMessage);

                var logData = new Dictionary<string, object>
                {
                    ["playerMessage"] = logger.SummarizeText("player", playerMessage),
                    ["npcResponse"] = logger.SummarizeText("npc", dialogueMessage),
                    ["plannedAction"] = actionPlan.ActionType.ToString(),
                    ["actionReason"] = actionPlan.Reason,
                };
                scope.Success("Dialogue generation complete.", logData);
            }
            catch (Exception ex)
            {
                turnSw.Stop();
                turnSpan.SetError(ex.Message);
                DatadogMetricsService.Increment("dialogue.session.error", tags: new[]
                {
                    $"npc:{slug}",
                    $"exception:{ex.GetType().Name}",
                });

                logger.Log(
                    NPCFlowStage.DialogueGeneration,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"LocalAI Chat error: {ex.Message}",
                    source: nameof(NPCDialogueSessionService),
                    requestId: reqId,
                    npcSlug: slug,
                    data: new Dictionary<string, object>
                    {
                        ["exceptionType"] = ex.GetType().Name,
                        ["exceptionMessage"] = ex.Message,
                    }
                );
                OnError?.Invoke(ex.Message);
                scope.Error(ex, "Dialogue generation failed.");
            }
            finally
            {
                _isResponding = false;
                _responseNPC = null;
                ClearRuntimePlayerContext();
            }
        }

        // ─────────────────────────────────── LocalAI Backend ────

        string DirectLocalAiEndpointPreview =>
            $"http://{_remoteHost}:{_remotePort}/v1/chat/completions";

        /// <summary>
        /// Model string from NPCDialogueManager.remoteModel, set during Initialize.
        /// Never reads from NPCLocalAIClient.model — the AIClient's field is only a
        /// fallback for standalone use (not the dialogue path).
        /// </summary>
        string ResolvedModelName =>
            !string.IsNullOrWhiteSpace(_chatModel)
                ? _chatModel.Trim()
                : "llama-3.2-3b-instruct:q8_0";

        async Task<string> SendToLocalAIAsync(
            NPCProfile profile,
            string playerMessage,
            string prompt,
            string reqId,
            string slug
        )
        {
            using var scope = NPCFlowScope.Start(
                NPCFlowLogger.FindOrCreate(),
                NPCFlowStage.BackendRequest,
                source: nameof(NPCDialogueSessionService) + ".LocalAI",
                requestId: reqId,
                npcSlug: slug,
                data: new Dictionary<string, object>
                {
                    ["backend"] = "LocalAI",
                    ["endpoint"] = DirectLocalAiEndpointPreview,
                }
            );

            string dialogueMessage = string.Empty;

            // Build conversation history messages
            List<NPCOpenAIMessage> messages = new List<NPCOpenAIMessage>();

            // Gather runtime variables for template substitution in profile prompts
            string playerName = ResolveActivePlayerName();
            PromptVariables promptVars = PromptVariables.Default;
            promptVars.playerName = !string.IsNullOrEmpty(playerName) ? playerName : "Player";
            promptVars.npcSlug = slug;

            PlayerDialogueContext playerCtx = default;
            if (_contextService != null && profile != null)
            {
                try
                {
                    playerCtx = await _contextService.GetOrLoadContextAsync(slug);
                    promptVars.trustScore = playerCtx.TrustScore;
                    promptVars.trustLabel = playerCtx.TrustLabel;
                    promptVars.mood = playerCtx.CurrentMood;
                    promptVars.dialogueCount = playerCtx.DialogueCount;
                    // Use last visited location as current location; fall back to default
                    if (playerCtx.VisitedLocations.Count > 0)
                        promptVars.currentLocation = playerCtx.VisitedLocations[^1];
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[NPCDialogueSessionService] Failed to load context for prompt vars: {ex.Message}"
                    );
                }
            }

            string sysPrompt = NPCProfilePromptComposer.BuildSystemPrompt(profile, promptVars);
            if (string.IsNullOrWhiteSpace(sysPrompt))
                sysPrompt = "You are a helpful assistant.";

            // Inject enriched player context (trust, mood, clues, items, locations)
            if (_contextService != null && profile != null && playerCtx.HasContext)
            {
                try
                {
                    sysPrompt += "\n\n" + playerCtx.BuildPromptBlock(slug);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[NPCDialogueSessionService] Failed to load player context: {ex.Message}"
                    );
                }
            }

            messages.Add(
                new NPCOpenAIMessage { role = "system", content = sysPrompt + "\n" + prompt }
            );

            foreach (
                var entry in _historyService?.GetHistoryForSlug(slug)
                    ?? new List<DialogueEntry>()
            )
            {
                string role = string.Equals(
                    entry.Role,
                    "assistant",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "assistant"
                    : "user";
                messages.Add(new NPCOpenAIMessage { role = role, content = entry.Content });
            }

            messages.Add(new NPCOpenAIMessage { role = "user", content = playerMessage });

            var localAiSw = System.Diagnostics.Stopwatch.StartNew();
            using var localAiSpan = DatadogTracer.StartSpan(
                "dialogue.localai.request",
                service: "unity-dedicated-server",
                resource: $"LocalAI/{ResolvedModelName}",
                type: "llm",
                tags: new[]
                {
                    $"npc:{slug}",
                    $"model:{ResolvedModelName}",
                }
            );
            bool requestSucceeded = false;

            if (_chatClient != null)
            {
                Debug.Log(
                    $"[NPCDialogueSessionService] Calling LocalAI — model='{ResolvedModelName}' profile='{profile?.GetNpcSlug()}' endpoint={DirectLocalAiEndpointPreview}"
                );
                dialogueMessage = await _chatClient.ChatAsync(
                    messages.ToArray(),
                    profile != null ? profile.Temperature : (float?)null,
                    modelOverride: ResolvedModelName
                );
                requestSucceeded = !string.IsNullOrEmpty(dialogueMessage);
            }

            if (dialogueMessage != null)
            {
                // Strip <think>...</think> block if present
                dialogueMessage = System
                    .Text.RegularExpressions.Regex.Replace(
                        dialogueMessage,
                        @"<think>.*?</think>",
                        "",
                        System.Text.RegularExpressions.RegexOptions.Singleline
                    )
                    .Trim();
            }

            localAiSw.Stop();
            localAiSpan.SetTag("status", requestSucceeded ? "success" : "empty");
            DatadogMetricsService.Timer("dialogue.localai.request.duration", localAiSw.ElapsedMilliseconds, tags: new[]
            {
                $"npc:{slug}",
                $"model:{ResolvedModelName}",
                $"status:{(requestSucceeded ? "success" : "empty")}",
            });
            DatadogMetricsService.Increment("dialogue.localai.request.count", tags: new[]
            {
                $"npc:{slug}",
                $"model:{ResolvedModelName}",
                $"status:{(requestSucceeded ? "success" : "empty")}",
            });

            scope.Success(
                "LocalAI response received.",
                new Dictionary<string, object>
                {
                    ["responseLength"] = dialogueMessage?.Length ?? 0,
                    ["playerName"] = playerName,
                    ["playerClientId"] = _activePlayerClientIdOverride.HasValue
                        ? _activePlayerClientIdOverride.Value
                        : 0ul,
                }
            );

            return dialogueMessage ?? string.Empty;
        }

        // ────────────────────────────── RAG Prompt Build ────

        async Task<string> BuildRAGPromptAsync(
            NPCProfile profile,
            string playerMessage,
            string reqId = null
        )
        {
            using var scope = NPCFlowScope.Start(
                NPCFlowLogger.FindOrCreate(),
                NPCFlowStage.ContextRetrieval,
                source: nameof(NPCDialogueSessionService),
                requestId: reqId,
                npcSlug: profile?.GetNpcSlug()
            );

            string ragKnowledge = string.Empty;
            bool isTechnicalQuestion = IsTechnicalCodebaseQuestion(playerMessage);

            try
            {
                if (_retrievalService != null)
                {
                    ragKnowledge = await _retrievalService.SearchAsync(
                        profile,
                        playerMessage,
                        reqId
                    );
                }
            }
            catch (Exception ex)
            {
                scope.Error(ex, "Failed during ContextRetrieval.");
            }

            if (string.IsNullOrWhiteSpace(ragKnowledge))
            {
                scope.Skipped(
                    "No retrieval context found.",
                    new Dictionary<string, object>
                    {
                        ["technicalQuestion"] = isTechnicalQuestion,
                    }
                );

                if (isTechnicalQuestion)
                {
                    return $"Technical question from the player: {playerMessage}\n\nNo retrieved codebase context was found for this request. Be honest about uncertainty. Do not invent file names, classes, methods, or architecture details. If you cannot answer from known context, say that you do not have enough retrieved project information yet.";
                }

                return playerMessage;
            }

            string combinedKnowledge = $"Relevant knowledge for {profile.GetDisplayName()}:\n";
            if (!string.IsNullOrWhiteSpace(ragKnowledge))
                combinedKnowledge += $"{ragKnowledge}\n";

            scope.Success(
                "Retrieval context added to prompt.",
                new Dictionary<string, object>
                {
                    ["ragKnowledgeLength"] = ragKnowledge.Length,
                    ["ragCategory"] = profile.GetRagCategory(),
                    ["technicalQuestion"] = isTechnicalQuestion,
                }
            );

            string basePrompt;
            if (isTechnicalQuestion)
            {
                basePrompt =
                    $"{combinedKnowledge}\n"
                    + $"Player technical question: {playerMessage}\n\n"
                    + "This turn is a technical codebase/support question, not a lore exchange. Prioritize factual accuracy over roleplay style. "
                    + "Answer using the retrieved knowledge above. Mention concrete script/class names and file paths when the context includes them. "
                    + "If the retrieved context is insufficient, say exactly what is missing instead of guessing. Avoid inventing project details.";
            }
            else
            {
                basePrompt =
                    $"{combinedKnowledge}\nPlayer message: {playerMessage}\n\nReply in character. Use the knowledge above only if it is relevant and avoid mentioning this instruction block.";
            }

            // Inject evidence state context so the NPC remembers what it has shared
            if (_evidenceState != null && profile != null)
            {
                string npcStateLine = _evidenceState.BuildNpcStateLine(profile.GetNpcSlug());
                string stateContext = _evidenceState.BuildStateContextString();
                if (
                    !string.IsNullOrWhiteSpace(npcStateLine)
                    || !string.IsNullOrWhiteSpace(stateContext)
                )
                {
                    basePrompt +=
                        $"\n\n{Environment.NewLine}{npcStateLine}{Environment.NewLine}{stateContext}";
                }
            }

            return basePrompt;
        }

        // ────────────────────────────────── Player Name ────

        string ResolveActivePlayerName()
        {
            if (!string.IsNullOrWhiteSpace(_activePlayerNameOverride))
            {
                return _activePlayerNameOverride;
            }

            return AuthNetworkBridge.ActivePlayerName;
        }

        /// <summary>
        /// Resolve the current trust score for a given NPC slug from the context
        /// service, if available. Falls back to 50 (neutral) if no context exists.
        /// </summary>
        int ResolveTrustScore(string npcSlug)
        {
            if (_contextService != null && !string.IsNullOrWhiteSpace(npcSlug))
            {
                var ctx = _contextService.GetCachedContext(npcSlug);
                if (ctx.HasValue)
                    return ctx.Value.TrustScore;
            }
            return 50;
        }

        // ─────────────────────────────── Static Helpers ────

        public static bool IsTechnicalCodebaseQuestion(string playerMessage)
        {
            if (string.IsNullOrWhiteSpace(playerMessage))
                return false;

            string normalized = playerMessage.Trim().ToLowerInvariant();
            string[] technicalMarkers =
            {
                "codebase",
                "script",
                "scripts",
                "class",
                "method",
                "function",
                "file",
                "path",
                "namespace",
                "asmdef",
                "qdrant",
                "rag",
                "collection",
                "embedding",
                "localai",
                "npcdialoguemanager",
                "qdrantragservice",
                "where is",
                "which script",
                "which file",
                "how does",
                "implemented",
                "implementation",
                "search",
                "retrieval",
                "runtime",
                "network",
            };

            foreach (string marker in technicalMarkers)
            {
                if (normalized.Contains(marker))
                    return true;
            }

            return false;
        }
    }
}
