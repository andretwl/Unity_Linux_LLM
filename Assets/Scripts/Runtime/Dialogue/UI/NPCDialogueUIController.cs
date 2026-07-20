using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EditorAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;


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
using NPCSystem.Network.Bridges;
using NPCSystem.Character.Animation;
using NPCSystem.Dialogue.RAG;
using NPCSystem.Dialogue.Persistence;
namespace NPCSystem.Dialogue.UI
{
    [DefaultExecutionOrder(-400)]
    public partial class NPCDialogueUIController : MonoBehaviour
    {
        [HelpBox(
            "Binds UI elements (dropdown, input, text, buttons) to the NPCDialogueManager or NPCDialogueNetworkBridge. Routes runtime events and handles portrait updates.",
            MessageMode.Log,
            drawAbove: true
        )]
        [SerializeField]
        EditorAttributes.Void _docsGroup;

        [FoldoutGroup(
            "References",
            true,
            nameof(DialogueManager),
            nameof(NetworkBridge),
            nameof(LegacyKnowledgeBaseController)
        )]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [FormerlySerializedAs("dialogueManager")]
        [SerializeField, HideProperty, Required]
        public NPCDialogueManager DialogueManager;

        [FormerlySerializedAs("networkBridge")]
        [SerializeField, HideProperty]
        public NPCDialogueNetworkBridge NetworkBridge;

        [FormerlySerializedAs("legacyKnowledgeBaseController")]
        [SerializeField, HideProperty]
        public Behaviour LegacyKnowledgeBaseController;

        [FoldoutGroup(
            "Dialogue UI",
            true,
            nameof(PlayerInput),
            nameof(AiText),
            nameof(StopButton)
        )]
        [SerializeField]
        EditorAttributes.Void dialogueUiGroup;

        [FormerlySerializedAs("playerInput")]
        [SerializeField, HideProperty, Required]
        public TMP_InputField PlayerInput;

        [FormerlySerializedAs("aiText")]
        [SerializeField, HideProperty, Required]
        public TMP_Text AiText;

        [FormerlySerializedAs("stopButton")]
        [SerializeField, HideProperty]
        public Button StopButton;

        [Tooltip(
            "If true, UI and dialogue systems initialize on Start. If false, they initialize deferred post-login."
        )]
        [FormerlySerializedAs("initializeOnStart")]
        [SerializeField, HideProperty]
        public bool InitializeOnStart = false;

        [Title("Runtime Status")]
        [ShowInInspector, ReadOnly]
        string ActiveProfilePreview => GetActiveProfile()?.GetDisplayName() ?? "<none>";

        [ShowInInspector, ReadOnly]
        string ActiveSlugPreview => GetActiveProfile()?.GetNpcSlug() ?? "<none>";

        [ShowInInspector, ReadOnly]
        bool HasDialogueManager => DialogueManager != null;

        [ShowInInspector, ReadOnly]
        bool IsInitialized =>
            _onDemandInitTask != null
            && (_onDemandInitTask.IsCompletedSuccessfully || _managerBound);

        Task _onDemandInitTask;
        bool _listenersBound;
        bool _managerBound;
        bool _readyForInput;
        List<NPCProfile> _profiles = new List<NPCProfile>();

        // ── Lifecycle ──────────────────────────────────────────────────

        void Awake()
        {
            ResolveReferences();
        }

        void Start()
        {
            if (InitializeOnStart)
            {
                _ = InitializeOnDemandInternalAsync();
            }
        }

        void OnDestroy()
        {
            if (DialogueManager != null && _listenersBound)
            {
                DialogueManager.OnNpcChanged.RemoveListener(HandleNpcChanged);
                DialogueManager.OnResponseStart.RemoveListener(HandleResponseStart);
                DialogueManager.OnResponseComplete.RemoveListener(HandleResponseComplete);
                DialogueManager.OnError.RemoveListener(HandleError);
            }
            if (NetworkBridge != null && _listenersBound)
            {
                NetworkBridge.OnNpcChanged.RemoveListener(HandleNpcChanged);
                NetworkBridge.OnResponseStart.RemoveListener(HandleResponseStart);
                NetworkBridge.OnResponseUpdated.RemoveListener(HandleResponseUpdated);
                NetworkBridge.OnResponseComplete.RemoveListener(HandleResponseComplete);
                NetworkBridge.OnError.RemoveListener(HandleError);
            }
            _listenersBound = false;
        }

        void OnDisable()
        {
            if (DialogueManager != null && _listenersBound)
            {
                DialogueManager.OnNpcChanged.RemoveListener(HandleNpcChanged);
                DialogueManager.OnResponseStart.RemoveListener(HandleResponseStart);
                DialogueManager.OnResponseComplete.RemoveListener(HandleResponseComplete);
                DialogueManager.OnError.RemoveListener(HandleError);
            }
            if (NetworkBridge != null && _listenersBound)
            {
                NetworkBridge.OnNpcChanged.RemoveListener(HandleNpcChanged);
                NetworkBridge.OnResponseStart.RemoveListener(HandleResponseStart);
                NetworkBridge.OnResponseUpdated.RemoveListener(HandleResponseUpdated);
                NetworkBridge.OnResponseComplete.RemoveListener(HandleResponseComplete);
                NetworkBridge.OnError.RemoveListener(HandleError);
            }
            _listenersBound = false;
        }

        // ── Public API ──────────────────────────────────────────────────

        public void SetInputEnabled(bool enabled)
        {
            DialogueDisplayHelper.SetInputEnabled(PlayerInput, StopButton, enabled);
        }

        public void SetAIText(string text)
        {
            DialogueDisplayHelper.SetAIText(AiText, text);
        }

        public NPCProfile GetActiveProfile()
        {
            return DialogueManager != null ? DialogueManager.CurrentProfile : null;
        }

        public async Task InitializeOnDemandAsync()
        {
            await InitializeOnDemandInternalAsync();
        }

        public GameObject GetGameplayCanvas()
        {
            var canvas = GetComponentInChildren<Canvas>(includeInactive: true);
            if (canvas != null)
                return canvas.gameObject;
            return GameObject.Find("GameplayCanvas");
        }

        // ── UI listeners ───────────────────────────────────────────────

        void BindUiListeners()
        {
            if (PlayerInput != null)
                PlayerInput.onSubmit.AddListener(OnInputFieldSubmit);
            if (StopButton != null)
                StopButton.onClick.AddListener(OnStopPressed);
        }

        // ── Internal helpers ───────────────────────────────────────────

        void BindRuntimeEvents()
        {
            if (_listenersBound)
                return;
            if (DialogueManager != null)
            {
                DialogueManager.OnNpcChanged.AddListener(HandleNpcChanged);
                DialogueManager.OnResponseStart.AddListener(HandleResponseStart);
                DialogueManager.OnResponseComplete.AddListener(HandleResponseComplete);
                DialogueManager.OnError.AddListener(HandleError);
            }
            // Also bind to NetworkBridge events for networked client scenarios
            // where the dialogue runs on the server and responses come via RPC.
            if (NetworkBridge != null)
            {
                NetworkBridge.OnNpcChanged.AddListener(HandleNpcChanged);
                NetworkBridge.OnResponseStart.AddListener(HandleResponseStart);
                NetworkBridge.OnResponseUpdated.AddListener(HandleResponseUpdated);
                NetworkBridge.OnResponseComplete.AddListener(HandleResponseComplete);
                NetworkBridge.OnError.AddListener(HandleError);
            }
            _listenersBound = true;
        }

        void ClearTemporaryProfiles()
        {
            _profiles.Clear();
        }

        /// <summary>
        /// Finds a component of type <typeparamref name="T"/> on this object or its children,
        /// logging a warning if not found.
        /// </summary>
        static T FindComponent<T>(Component host, string label) where T : Component
        {
            var component = host.GetComponentInChildren<T>(includeInactive: true);
            if (component == null)
                Debug.LogWarning($"[{nameof(NPCDialogueUIController)}] {label} not found.");
            return component;
        }

        // ── Initialization ─────────────────────────────────────────────

        async Task InitializeOnDemandInternalAsync()
        {
            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Debug,
                    "NPCDialogueUIController starting on demand.",
                    source: nameof(NPCDialogueUIController)
                );

            ResolveReferences();
            DisableLegacyController();
            SetInputEnabled(false);
            BindUiListeners();

            try
            {
                if (NetworkBridge == null && DialogueManager == null)
                {
                    NPCFlowLogger
                        .FindOrCreate()
                        .Log(
                            NPCFlowStage.SceneBootstrap,
                            NPCFlowStatus.Error,
                            NPCFlowLogLevel.Error,
                            "Neither NPCDialogueManager nor NPCDialogueNetworkBridge is available. Input disabled.",
                            source: nameof(NPCDialogueUIController)
                        );
                    return;
                }

                if (NetworkBridge != null)
                    await NetworkBridge.InitializeAsync();
                else
                    await DialogueManager.InitializeAsync();

                BindRuntimeEvents();
                AutoSelectFirstProfile();

                // Only enable input after successful initialization
                _readyForInput = true;
                SetInputEnabled(true);
                if (PlayerInput != null)
                {
                    PlayerInput.Select();
                    PlayerInput.ActivateInputField();
                }
            }
            catch (System.Exception ex)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    .Log(
                        NPCFlowStage.SceneBootstrap,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Dialogue bootstrap init failed: {ex.Message}. Input disabled.",
                        source: nameof(NPCDialogueUIController),
                        data: new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["exception"] = ex.ToString(),
                        }
                    );
                // Do NOT enable input — user would see a functional-looking field
                // that silently drops messages because no NPC is loaded.
                _readyForInput = false;
                SetInputEnabled(false);
                return;
            }

            DatadogMetricsService.Increment(
                "dialogue.ui.initialized",
                tags: new[]
                {
                    NetworkBridge != null ? "mode:network_bridge" : "mode:direct",
                }
            );

            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    "NPCDialogueUIController ready and initialized.",
                    source: nameof(NPCDialogueUIController)
                );
        }

        void AutoSelectFirstProfile()
        {
            NPCProfile activeProfile = GetActiveProfile();
            if (activeProfile == null)
            {
                NPCProfile[] availableProfiles =
                    NetworkBridge != null ? NetworkBridge.Profiles : DialogueManager.Profiles;
                _profiles = availableProfiles.Where(profile => profile != null).ToList();
                if (_profiles.Count > 0)
                {
                    // Auto-select first available profile
                    _ = SelectProfileAsync(0);
                }
            }
            else
            {
                SetInputEnabled(true);
                _readyForInput = true;
            }
        }

        void DisableLegacyController()
        {
            if (LegacyKnowledgeBaseController != null)
            {
                LegacyKnowledgeBaseController.enabled = false;
            }
        }

        // ── Event handlers ────────────────────────────────────────────

        void HandleNpcChanged(string displayName)
        {
            _readyForInput = true;
            SetInputEnabled(true);
        }

        void HandleResponseStart(string playerMessage)
        {
            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.UIInput,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "UI received response-start event.",
                    source: nameof(NPCDialogueUIController),
                    npcSlug: GetActiveProfile() != null ? GetActiveProfile().GetNpcSlug() : null,
                    data: NPCFlowTextSanitizer.MergeSummary(
                        new Dictionary<string, object>(),
                        "player",
                        playerMessage,
                        false,
                        0
                    )
                );
            if (PlayerInput != null)
                PlayerInput.interactable = false;
            SetAIText("...");
        }

        void HandleResponseComplete(string npcName, string response)
        {
            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.ResponseComplete,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "UI received response-complete event.",
                    source: nameof(NPCDialogueUIController),
                    npcSlug: npcName,
                    data: NPCFlowTextSanitizer.MergeSummary(
                        new Dictionary<string, object>(),
                        "response",
                        response,
                        false,
                        0
                    )
                );
            SetAIText(response);
            SetInputEnabled(true);
            if (PlayerInput != null)
                PlayerInput.text = "";
        }

        void HandleError(string error)
        {
            string normalizedError = DialogueDisplayHelper.NormalizeError(error);
            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.UIInput,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"UI received dialogue error: {normalizedError}",
                    source: nameof(NPCDialogueUIController),
                    data: new Dictionary<string, object> { ["error"] = normalizedError }
                );
            SetAIText(DialogueDisplayHelper.FormatErrorForDisplay(error));
            if (GetActiveProfile() != null)
            {
                _readyForInput = true;
                SetInputEnabled(true);
            }
        }

        void HandleResponseUpdated(string partialResponse)
        {
            DialogueDisplayHelper.SetAIText(AiText, partialResponse);
        }


        async Task SelectProfileAsync(int selection)
        {
            if (selection < 0 || selection >= _profiles.Count)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    .Log(
                        NPCFlowStage.NPCSwitch,
                        NPCFlowStatus.Skipped,
                        NPCFlowLogLevel.Warning,
                        "UI profile selection index out of range.",
                        source: nameof(NPCDialogueUIController),
                        data: new Dictionary<string, object>
                        {
                            ["selection"] = selection,
                            ["profileCount"] = _profiles.Count,
                        }
                    );
                return;
            }

            string npcSlug = _profiles[selection].GetNpcSlug();
            if (ShouldUseNetworkBridge())
                await NetworkBridge.RequestNpcSelectionAsync(npcSlug);
            else
                await DialogueManager.SwitchToNPCAsync(npcSlug);
        }


        void OnInputFieldSubmit(string text)
        {
            if (!_readyForInput)
                return;

            string message = (text ?? string.Empty).Trim();
            if (message.Length == 0)
                return;

            if (PlayerInput != null)
                PlayerInput.interactable = false;

            if (ShouldUseNetworkBridge())
                NetworkBridge.SubmitPlayerMessage(message);
            else
                DialogueManager.SendDialogueMessage(message);
        }

        void OnStopPressed()
        {
            if (ShouldUseNetworkBridge())
                NetworkBridge.CancelActiveRequest();
            else
                DialogueManager.CancelRequests();
            SetAIText(string.Empty);
            SetInputEnabled(true);
        }

        /// <summary>
        /// Returns true when the network bridge should be the primary routing path.
        /// Requires the bridge to exist, its NetworkObject to be spawned,
        /// NetworkManager to be listening, and local peer to be client or server.
        /// Falls back to direct manager calls otherwise.
        /// </summary>
        bool ShouldUseNetworkBridge()
        {
            return NetworkBridge != null && NetworkBridge.IsNetworkReady;
        }

        // ── Reference resolution ──────────────────────────────────────

        [Button("Auto-Assign References")]
        void AutoAssignReferences()
        {
            ResolveReferences();
        }

        [Button("Validate UI References")]
        void ValidateUiReferences()
        {
            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.ConfigurationValidation,
                    DialogueManager != null ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                    DialogueManager != null ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                    DialogueManager != null
                        ? "NPCDialogueManager is assigned."
                        : "NPCDialogueManager is NOT assigned — runtime will not function.",
                    source: nameof(NPCDialogueUIController)
                );
        }

        void ResolveReferences()
        {
            if (DialogueManager == null)
            {
                DialogueManager = GetComponent<NPCDialogueManager>();
                if (DialogueManager == null)
                {
                    DialogueManager = FindAnyObjectByType<NPCDialogueManager>(
                        FindObjectsInactive.Include
                    );
                }
            }

            if (NetworkBridge == null)
            {
                NetworkBridge = GetComponent<NPCDialogueNetworkBridge>();
                if (NetworkBridge == null)
                {
                    NetworkBridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(
                        FindObjectsInactive.Include
                    );
                }
            }

            if (LegacyKnowledgeBaseController == null)
            {
                LegacyKnowledgeBaseController = FindObjectsByType<Behaviour>(
                        FindObjectsInactive.Include
                    )
                    .FirstOrDefault(behaviour =>
                        behaviour != null
                        && behaviour.GetType().FullName == "LLMUnitySamples.KnowledgeBaseGame"
                    );
            }

            PlayerInput =
                PlayerInput != null
                    ? PlayerInput
                    : FindComponent<TMP_InputField>(this, "Canvas/PlayerInput");
            AiText = AiText != null ? AiText : FindComponent<TMP_Text>(this, "Canvas/AIImage/AIText");
            StopButton =
                StopButton != null ? StopButton : FindComponent<Button>(this, "Canvas/StopButton");
        }
    }
}
