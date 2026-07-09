using System;
using System.Collections.Generic;
using System.Linq;
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
        string _remoteHost;
        int _remotePort;
        NPCProfile[] _profiles;

        // ────────────────────────────────────────────── Events ────
        public event Action<string> onResponseStart;
        public event Action<string, string> onResponseComplete;
        public event Action<string> onError;

        // ────────────────────────────────────────── Properties ────
        public bool isResponding => _isResponding;
        public string activePlayerName => ResolveActivePlayerName();

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
            string remoteHost,
            int remotePort,
            NPCProfile[] profiles
        )
        {
            _chatClient = chatClient;
            _historyService = historyService;
            _retrievalService = retrievalService;
            _actionPlanner = actionPlanner;
            _evidenceState = evidenceState;
            _remoteHost = remoteHost ?? "localhost";
            _remotePort = remotePort;
            _profiles = profiles;
        }

        // ────────────────────────────────────────── Public API ────

        /// <summary>
        /// Start a dialogue turn for the currently active NPC.
        /// Validates state, sets responding flag, and fires the async LLM chain.
        /// </summary>
        public void SendMessage(string playerMessage, NPCProfile currentNpc)
        {
            if (_isResponding || string.IsNullOrWhiteSpace(playerMessage))
                return;

            string trimmedMessage = playerMessage.Trim();
            _isResponding = true;
            _responseNPC = currentNpc;
            onResponseStart?.Invoke(trimmedMessage);

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

            try
            {
                string prompt = await BuildRAGPromptAsync(respondingProfile, playerMessage, reqId);
                string dialogueMessage = string.Empty;

                NPCDialogueActionPlan actionPlan =
                    _actionPlanner != null
                        ? _actionPlanner.Plan(playerMessage, respondingProfile)
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

                onResponseComplete?.Invoke(respondingProfile.GetDisplayName(), dialogueMessage);

                var logData = new Dictionary<string, object>
                {
                    ["playerMessage"] = logger.SummarizeText("player", playerMessage),
                    ["npcResponse"] = logger.SummarizeText("npc", dialogueMessage),
                    ["plannedAction"] = actionPlan.actionType.ToString(),
                    ["actionReason"] = actionPlan.reason,
                };
                scope.Success("Dialogue generation complete.", logData);
            }
            catch (Exception ex)
            {
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
                onError?.Invoke(ex.Message);
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

        /// <summary>Model name is owned by NPCDialogueManager and synced to chatClient.</summary>
        string ResolvedModelName =>
            _chatClient != null && !string.IsNullOrWhiteSpace(_chatClient.model)
                ? _chatClient.model.Trim()
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
            string sysPrompt = NPCProfilePromptComposer.BuildSystemPrompt(profile);
            if (string.IsNullOrWhiteSpace(sysPrompt))
                sysPrompt = "You are a helpful assistant.";

            // Inject the authenticated player name so NPCs can personalise responses
            string playerName = ResolveActivePlayerName();
            if (
                !string.IsNullOrEmpty(playerName)
                && !string.Equals(playerName, "Player", StringComparison.OrdinalIgnoreCase)
            )
            {
                sysPrompt +=
                    $"\n\nThe player who is speaking to you is named '{playerName}'. This is a factual part of the current conversation context. If the player asks what their name is, answer that their name is '{playerName}'. Address them by name naturally when appropriate.";
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
                    entry.role,
                    "assistant",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "assistant"
                    : "user";
                messages.Add(new NPCOpenAIMessage { role = role, content = entry.content });
            }

            messages.Add(new NPCOpenAIMessage { role = "user", content = playerMessage });

            if (_chatClient != null)
            {
                dialogueMessage = await _chatClient.ChatAsync(
                    messages.ToArray(),
                    profile != null ? profile.temperature : (float?)null
                );
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
