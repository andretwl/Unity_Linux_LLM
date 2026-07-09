using System.Collections.Generic;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
{
    [DefaultExecutionOrder(500)]
    public class NPCDialogueSmokeValidator : MonoBehaviour
    {
        [FoldoutGroup("References", true, nameof(DialogueManager), nameof(ChatClient), nameof(LocalRag))]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [FormerlySerializedAs("dialogueManager")]
        [SerializeField, HideProperty, Required]
        public NPCDialogueManager DialogueManager;

        [FormerlySerializedAs("chatClient")]
        [SerializeField, HideProperty, Required]
        public NPCLocalAIClient ChatClient;

        [FormerlySerializedAs("localRag")]
        [SerializeField, HideProperty]
        public NPCLocalRAG LocalRag;

        [FoldoutGroup("Smoke Test Settings", true, nameof(validateOnStart), nameof(runFirstQuestionSmokeOnStart), nameof(smokeQuestion), nameof(smokeTimeoutSeconds))]
        [SerializeField]
        EditorAttributes.Void smokeSettingsGroup;

        [SerializeField, HideProperty]
        public bool validateOnStart = true;

        [SerializeField, HideProperty]
        public bool runFirstQuestionSmokeOnStart = false;

        [TextArea(2, 4)]
        [SerializeField, HideProperty]
        public string smokeQuestion = "What should I investigate first?";

        [SerializeField, HideProperty, Suffix("s")]
        public float smokeTimeoutSeconds = 60f;

        [Title("Runtime Status")]
        [ShowInInspector, ReadOnly]
        bool HasAllReferences =>
            DialogueManager != null && ChatClient != null;

        bool _responseCompleted;
        string _lastResponse = string.Empty;

        async void Start()
        {
            ResolveReferences();

            if (validateOnStart)
            {
                if (DialogueManager != null && !DialogueManager.IsInitialized)
                {
                    NPCFlowLogger
                        .FindOrCreate()
                        .Log(
                            NPCFlowStage.SmokeValidation,
                            NPCFlowStatus.Skipped,
                            NPCFlowLogLevel.Info,
                            "Skipping Start validation because NPCDialogueManager is not initialized yet (deferred loading active).",
                            source: nameof(NPCDialogueSmokeValidator)
                        );
                }
                else
                {
                    ValidateConfiguration();
                }
            }

            if (runFirstQuestionSmokeOnStart)
            {
                await RunFirstQuestionSmokeAsync();
            }
        }

        [Button("Validate Configuration")]
        public void ValidateConfiguration()
        {
            ResolveReferences();

            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            bool ok = true;
            ok &= Require(logger, DialogueManager != null, "NPCDialogueManager is assigned.");
            ok &= Require(logger, ChatClient != null, "NPCLocalAIClient is assigned.");
            ok &= Require(logger, LocalRag != null, "NPCLocalRAG is assigned.");

            if (DialogueManager != null)
            {
                ok &= Require(
                    logger,
                    DialogueManager.ChatClient == ChatClient,
                    "NPCDialogueManager.ChatClient points to the chat client."
                );
                ok &= Require(
                    logger,
                    DialogueManager.LocalRag == LocalRag,
                    "NPCDialogueManager.LocalRag points to the local RAG."
                );
                ok &= Require(
                    logger,
                    !string.IsNullOrWhiteSpace(DialogueManager.RagEmbeddingPath),
                    "NPCDialogueManager.RagEmbeddingPath is set."
                );
            }

            logger.Log(
                NPCFlowStage.SmokeValidation,
                ok ? NPCFlowStatus.Success : NPCFlowStatus.Error,
                ok ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Error,
                ok
                    ? "NPC dialogue configuration validation passed."
                    : "NPC dialogue configuration validation failed.",
                source: nameof(NPCDialogueSmokeValidator)
            );
        }

        [Button("Run First Question Smoke")]
        public async void RunFirstQuestionSmoke()
        {
            await RunFirstQuestionSmokeAsync();
        }

        public async Task RunFirstQuestionSmokeAsync()
        {
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            using var scope = NPCFlowScope.Start(
                logger,
                NPCFlowStage.SmokeValidation,
                nameof(NPCDialogueSmokeValidator),
                data: new Dictionary<string, object> { ["timeoutSeconds"] = smokeTimeoutSeconds }
            );
            ResolveReferences();
            ValidateConfiguration();

            if (DialogueManager == null)
            {
                logger.Log(
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

            await DialogueManager.InitializeAsync();
            if (DialogueManager.currentProfile == null)
            {
                string defaultSlug = DialogueManager.GetDefaultProfileSlug();
                if (!string.IsNullOrWhiteSpace(defaultSlug))
                {
                    await DialogueManager.SwitchToNPCAsync(defaultSlug);
                }
            }

            if (DialogueManager.currentProfile == null)
            {
                logger.Log(
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
            DialogueManager.OnResponseComplete.AddListener(HandleSmokeResponseComplete);

            try
            {
                DialogueManager.SendMessage(smokeQuestion);
                float startTime = Time.realtimeSinceStartup;
                while (
                    !_responseCompleted
                    && Time.realtimeSinceStartup - startTime < smokeTimeoutSeconds
                )
                {
                    await Task.Yield();
                }

                if (!_responseCompleted)
                {
                    logger.Log(
                        NPCFlowStage.SmokeValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        "Smoke failed: timed out waiting for NPC response.",
                        source: nameof(NPCDialogueSmokeValidator),
                        data: new Dictionary<string, object>
                        {
                            ["timeoutSeconds"] = smokeTimeoutSeconds,
                            ["npcSlug"] = DialogueManager.currentProfile.GetNpcSlug(),
                        }
                    );
                    scope.Error(
                        null,
                        "Smoke failed: timed out waiting for NPC response.",
                        new Dictionary<string, object>
                        {
                            ["timeoutSeconds"] = smokeTimeoutSeconds,
                            ["npcSlug"] = DialogueManager.currentProfile.GetNpcSlug(),
                        }
                    );
                    Application.Quit(1);
                    return;
                }

                if (string.IsNullOrWhiteSpace(_lastResponse))
                {
                    logger.Log(
                        NPCFlowStage.SmokeValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        "Smoke failed: NPC response was empty.",
                        source: nameof(NPCDialogueSmokeValidator),
                        data: new Dictionary<string, object>
                        {
                            ["npcSlug"] = DialogueManager.currentProfile.GetNpcSlug(),
                        }
                    );
                    scope.Error(
                        null,
                        "Smoke failed: NPC response was empty.",
                        new Dictionary<string, object>
                        {
                            ["npcSlug"] = DialogueManager.currentProfile.GetNpcSlug(),
                        }
                    );
                    Application.Quit(1);
                    return;
                }

                string logMsg =
                    $"Smoke test passed with {DialogueManager.currentProfile.GetDisplayName()}. Response: {_lastResponse}";
                logger.Log(
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
                            ["npcSlug"] = DialogueManager.currentProfile.GetNpcSlug(),
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
                Application.Quit(0);
            }
            finally
            {
                DialogueManager.OnResponseComplete.RemoveListener(HandleSmokeResponseComplete);
            }
        }

        void ResolveReferences()
        {
            if (DialogueManager == null)
                DialogueManager = FindAnyObjectByType<NPCDialogueManager>(
                    FindObjectsInactive.Include
                );
            if (ChatClient == null && DialogueManager != null)
                ChatClient = DialogueManager.ChatClient;
            if (ChatClient == null)
                ChatClient = FindAnyObjectByType<NPCLocalAIClient>(FindObjectsInactive.Include);
            if (LocalRag == null)
                LocalRag = FindAnyObjectByType<NPCLocalRAG>(FindObjectsInactive.Include);
        }

        void HandleSmokeResponseComplete(string npcName, string response)
        {
            _lastResponse = response;
            _responseCompleted = true;
        }

        static bool Require(NPCFlowLogger logger, bool condition, string message)
        {
            if (condition)
            {
                logger.Log(
                    NPCFlowStage.SmokeValidation,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    message,
                    source: nameof(NPCDialogueSmokeValidator)
                );
                return true;
            }

            logger.Log(
                NPCFlowStage.SmokeValidation,
                NPCFlowStatus.Error,
                NPCFlowLogLevel.Error,
                message,
                source: nameof(NPCDialogueSmokeValidator)
            );
            return false;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/NPC System/Run CLI Smoke Test")]
        public static void RunSmokeTestCLI()
        {
            var validator = FindAnyObjectByType<NPCDialogueSmokeValidator>(
                FindObjectsInactive.Include
            );
            if (validator == null)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    .Log(
                        NPCFlowStage.SmokeValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        "No validator found in scene.",
                        source: nameof(NPCDialogueSmokeValidator)
                    );
                Application.Quit(1);
                return;
            }
            validator.RunFirstQuestionSmoke();
        }
#endif
    }
}
