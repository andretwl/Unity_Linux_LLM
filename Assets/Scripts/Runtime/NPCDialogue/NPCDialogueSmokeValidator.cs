using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
{
    /// <summary>
    /// Standalone smoke-test validator for NPC dialogue configuration.
    /// Attach to the NPCDialogueSystem GameObject. It runs during
    /// Start() and validates that all wired components are present.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class NPCDialogueSmokeValidator : MonoBehaviour
    {
        [FormerlySerializedAs("dialogueManager")]
        [SerializeField]
        NPCDialogueManager _dialogueManager;

        [FormerlySerializedAs("chatClient")]
        [SerializeField]
        NPCLocalAIClient _chatClient;

        [FormerlySerializedAs("localRag")]
        [SerializeField]
        NPCLocalRAG _localRag;

        [FormerlySerializedAs("logger")]
        [SerializeField]
        NPCFlowLogger _logger;

        bool _responseCompleted;
        string _lastResponse;

        static NPCFlowLogger Logger => NPCFlowLogger.FindOrCreate();

        public async void Start()
        {
            await ValidateConfiguration();
        }

        public async Task ValidateConfiguration()
        {
            using var scope = NPCFlowScope.Start(
                Logger,
                NPCFlowStage.SmokeValidation,
                "NPC Dialogue Smoke Test"
            );

            bool ok = ValidateComponentReferences();

            if (_dialogueManager == null)
            {
                Logger.Log(
                    NPCFlowStage.SmokeValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "Cannot run smoke test without NPCDialogueManager.",
                    source: nameof(NPCDialogueSmokeValidator)
                );
                scope.Error(null, "Smoke failed: missing NPCDialogueManager.");
                Application.Quit(1);
                return;
            }

            await _dialogueManager.InitializeAsync();
            if (_dialogueManager.CurrentProfile == null)
            {
                string defaultSlug = _dialogueManager.GetDefaultProfileSlug();
                if (!string.IsNullOrWhiteSpace(defaultSlug))
                {
                    await _dialogueManager.SwitchToNPCAsync(defaultSlug);
                }
            }

            if (_dialogueManager.CurrentProfile == null)
            {
                Logger.Log(
                    NPCFlowStage.SmokeValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "Smoke failed: no default NPC could be selected.",
                    source: nameof(NPCDialogueSmokeValidator)
                );
                scope.Error(null, "Smoke failed: no default NPC selected.");
                Application.Quit(1);
                return;
            }

            _responseCompleted = false;
            _lastResponse = string.Empty;
            _dialogueManager.OnResponseComplete.AddListener(HandleSmokeResponseComplete);

            try
            {
                string smokeQuestion = "Hello, what can you tell me?";
                _dialogueManager.SendDialogueMessage(smokeQuestion);
                float startTime = Time.realtimeSinceStartup;
                float smokeTimeoutSeconds = 60f;

                while (
                    !_responseCompleted
                    && Time.realtimeSinceStartup - startTime < smokeTimeoutSeconds
                )
                {
                    await Task.Yield();
                }

                if (!_responseCompleted)
                {
                    Logger.Log(
                        NPCFlowStage.SmokeValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        "Smoke failed: timed out waiting for NPC response.",
                        source: nameof(NPCDialogueSmokeValidator),
                        data: new Dictionary<string, object>
                        {
                            ["timeoutSeconds"] = smokeTimeoutSeconds,
                            ["npcSlug"] = _dialogueManager.CurrentProfile.GetNpcSlug(),
                        }
                    );
                    scope.Error(
                        null,
                        "Smoke failed: timed out waiting for NPC response.",
                        new Dictionary<string, object>
                        {
                            ["timeoutSeconds"] = smokeTimeoutSeconds,
                            ["npcSlug"] = _dialogueManager.CurrentProfile.GetNpcSlug(),
                        }
                    );
                    Application.Quit(1);
                    return;
                }

                if (string.IsNullOrWhiteSpace(_lastResponse))
                {
                    Logger.Log(
                        NPCFlowStage.SmokeValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        "Smoke failed: NPC response was empty.",
                        source: nameof(NPCDialogueSmokeValidator),
                        data: new Dictionary<string, object>
                        {
                            ["npcSlug"] = _dialogueManager.CurrentProfile.GetNpcSlug(),
                        }
                    );
                    scope.Error(
                        null,
                        "Smoke failed: NPC response was empty.",
                        new Dictionary<string, object>
                        {
                            ["npcSlug"] = _dialogueManager.CurrentProfile.GetNpcSlug(),
                        }
                    );
                    Application.Quit(1);
                    return;
                }

                string logMsg =
                    $"Smoke test passed with {_dialogueManager.CurrentProfile.GetDisplayName()}. Response: {_lastResponse}";
                Logger.Log(
                    NPCFlowStage.SmokeValidation,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    logMsg,
                    source: nameof(NPCDialogueSmokeValidator)
                );
                System.IO.File.WriteAllText("dialogue_log.txt", logMsg);
                scope.Success(
                    "First-question smoke passed.",
                    NPCFlowTextSanitizer.MergeSummary(
                        new Dictionary<string, object>
                        {
                            ["npcSlug"] = _dialogueManager.CurrentProfile.GetNpcSlug(),
                        },
                        "response",
                        _lastResponse,
                        includeSnippet: false,
                        maxSnippetChars: 0
                    )
                );

#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
            finally
            {
                _dialogueManager.OnResponseComplete.RemoveListener(HandleSmokeResponseComplete);
            }
        }

        bool ValidateComponentReferences()
        {
            using var scope = NPCFlowScope.Start(
                Logger,
                NPCFlowStage.SmokeValidation,
                "Component validation"
            );

            bool ok = true;

            if (_dialogueManager != null)
            {
                ok &= Require(
                    logger: Logger,
                    condition: _dialogueManager._chatClient == _chatClient,
                    message: "NPCDialogueManager.ChatClient points to the chat client."
                );

                ok &= Require(
                    logger: Logger,
                    condition: _dialogueManager._localRag == _localRag,
                    message: "NPCDialogueManager.LocalRag points to the local RAG."
                );

                ok &= Require(
                    logger: Logger,
                    condition: !string.IsNullOrWhiteSpace(_dialogueManager.RagEmbeddingPath),
                    message: "NPCDialogueManager.RagEmbeddingPath is set."
                );
            }

            Logger.Log(
                NPCFlowStage.SmokeValidation,
                ok ? NPCFlowStatus.Success : NPCFlowStatus.Error,
                ok ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Error,
                ok
                    ? "NPC dialogue configuration validation passed."
                    : "NPC dialogue configuration validation FAILED — check logs for details.",
                source: nameof(NPCDialogueSmokeValidator)
            );

            return ok;
        }

        void HandleSmokeResponseComplete(string npcName, string response)
        {
            _responseCompleted = true;
            _lastResponse = response;
        }

        static bool Require(NPCFlowLogger logger, bool condition, string message)
        {
            if (!condition)
            {
                logger.Log(
                    NPCFlowStage.SmokeValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    message,
                    source: nameof(NPCDialogueSmokeValidator)
                );
            }
            return condition;
        }
    }
}
