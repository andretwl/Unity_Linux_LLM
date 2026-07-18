using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EditorAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace NPCSystem
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
            nameof(CharacterSelect),
            nameof(PlayerInput),
            nameof(AiText),
            nameof(StopButton)
        )]
        [SerializeField]
        EditorAttributes.Void dialogueUiGroup;

        [FormerlySerializedAs("characterSelect")]
        [SerializeField, HideProperty, Required]
        public TMP_Dropdown CharacterSelect;

        [FormerlySerializedAs("playerInput")]
        [SerializeField, HideProperty, Required]
        public TMP_InputField PlayerInput;

        [FormerlySerializedAs("aiText")]
        [SerializeField, HideProperty, Required]
        public TMP_Text AiText;

        [FormerlySerializedAs("stopButton")]
        [SerializeField, HideProperty]
        public Button StopButton;

        [FoldoutGroup("Portraits", true, nameof(ButlerImage), nameof(MaidImage), nameof(ChefImage))]
        [SerializeField]
        EditorAttributes.Void portraitsGroup;

        [FormerlySerializedAs("butlerImage")]
        [SerializeField, HideProperty]
        public RawImage ButlerImage;

        [FormerlySerializedAs("maidImage")]
        [SerializeField, HideProperty]
        public RawImage MaidImage;

        [FormerlySerializedAs("chefImage")]
        [SerializeField, HideProperty]
        public RawImage ChefImage;

        [FoldoutGroup("Notebook / Panels", true, nameof(NotebookController))]
        [SerializeField]
        EditorAttributes.Void notebookGroup;

        [FormerlySerializedAs("notebookController")]
        [SerializeField, HideProperty]
        public NotebookUIController NotebookController;

        [FoldoutGroup("Relationship UI", true, nameof(RelationshipUI))]
        [SerializeField]
        EditorAttributes.Void relationshipGroup;

        [FormerlySerializedAs("relationshipUI")]
        [SerializeField, HideProperty]
        public NPCRelationshipUIController RelationshipUI;

        [FoldoutGroup("Exit and Startup", true, nameof(ExitButton), nameof(InitializeOnStart))]
        [SerializeField]
        EditorAttributes.Void exitStartupGroup;

        [FormerlySerializedAs("exitButton")]
        [SerializeField, HideProperty]
        public Button ExitButton;

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
            if (PlayerInput != null)
                PlayerInput.interactable = enabled;
            if (StopButton != null)
                StopButton.interactable = enabled;
        }

        public void SetAIText(string text)
        {
            if (AiText != null)
                AiText.text = text;
        }

        public NPCProfile GetActiveProfile()
        {
            return DialogueManager != null ? DialogueManager.CurrentProfile : null;
        }

        public void ToggleNotebook()
        {
            if (NotebookController != null)
                NotebookController.ToggleNotebook();
        }

        public bool IsAnyPanelOpen()
        {
            return NotebookController != null && NotebookController.IsOpen;
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
            if (CharacterSelect != null)
                CharacterSelect.onValueChanged.AddListener(OnCharacterSelectionChanged);
            if (PlayerInput != null)
                PlayerInput.onSubmit.AddListener(OnInputFieldSubmit);
            if (StopButton != null)
                StopButton.onClick.AddListener(OnStopPressed);
            if (ExitButton != null)
                ExitButton.onClick.AddListener(OnExitPressed);
        }

        void OnExitPressed()
        {
            if (NotebookController != null && NotebookController.IsOpen)
            {
                NotebookController.ToggleNotebook();
                return;
            }

            _readyForInput = false;
            SetInputEnabled(false);
            SetAIText("Goodbye!");
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
            if (CharacterSelect != null)
                CharacterSelect.ClearOptions();
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
                            "Neither NPCDialogueManager nor NPCDialogueNetworkBridge is available.",
                            source: nameof(NPCDialogueUIController)
                        );
                    return;
                }

                if (NetworkBridge != null)
                    await NetworkBridge.InitializeAsync();
                else
                    await DialogueManager.InitializeAsync();

                BindRuntimeEvents();
                PopulateDropdown();
                await SyncDropdownToCurrentProfileAsync();
            }
            catch (System.Exception ex)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    .Log(
                        NPCFlowStage.SceneBootstrap,
                        NPCFlowStatus.Fallback,
                        NPCFlowLogLevel.Warning,
                        $"Dialogue bootstrap init failed: {ex.Message}. Input still enabled.",
                        source: nameof(NPCDialogueUIController)
                    );
            }
            finally
            {
                _readyForInput = true;
                SetInputEnabled(true);
                if (PlayerInput != null)
                {
                    PlayerInput.Select();
                    PlayerInput.ActivateInputField();
                }
            }

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

        void PopulateDropdown()
        {
            NPCProfile[] availableProfiles =
                NetworkBridge != null ? NetworkBridge.Profiles : DialogueManager.Profiles;
            _profiles = availableProfiles.Where(profile => profile != null).ToList();
            if (CharacterSelect == null)
                return;

            CharacterSelect.ClearOptions();
            CharacterSelect.AddOptions(
                _profiles.Select(profile => profile.GetDisplayName()).ToList()
            );
        }

        async Task SyncDropdownToCurrentProfileAsync()
        {
            if (
                (DialogueManager == null && NetworkBridge == null)
                || CharacterSelect == null
                || _profiles.Count == 0
            )
                return;

            NPCProfile activeProfile = GetActiveProfile();
            if (activeProfile == null)
            {
                CharacterSelect.SetValueWithoutNotify(0);
                await SelectProfileAsync(0);
                return;
            }

            int index = _profiles.FindIndex(profile => profile == activeProfile);
            if (index < 0)
                index = 0;
            CharacterSelect.SetValueWithoutNotify(index);
            UpdatePortrait(activeProfile);
            SetInputEnabled(true);
            _readyForInput = true;
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
            UpdatePortrait(GetActiveProfile());

            NPCProfile profile = GetActiveProfile();
            if (RelationshipUI != null && profile != null && DialogueManager != null)
            {
                string slug = profile.GetNpcSlug();
                int count = DialogueManager.GetHistory(slug).Count;
                RelationshipUI.Refresh(DialogueManager._evidenceState, slug, count);
            }

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

            if (RelationshipUI != null && DialogueManager != null)
            {
                int count = DialogueManager.GetHistory(npcName).Count;
                RelationshipUI.Refresh(DialogueManager._evidenceState, npcName, count);
            }
        }

        void HandleError(string error)
        {
            string normalizedError = string.IsNullOrWhiteSpace(error)
                ? "Unknown dialogue error."
                : error.Trim();
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
            SetAIText($"Error: {normalizedError}");
            if (GetActiveProfile() != null)
            {
                _readyForInput = true;
                SetInputEnabled(true);
            }
        }

        void HandleResponseUpdated(string partialResponse)
        {
            if (AiText != null)
                AiText.text = partialResponse;
        }

        void OnCharacterSelectionChanged(int selection)
        {
            _ = SelectProfileAsync(selection);
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

        void UpdatePortrait(NPCProfile profile)
        {
            RawImage[] portraits = new[] { ButlerImage, MaidImage, ChefImage };
            if (profile == null)
            {
                foreach (RawImage img in portraits)
                {
                    if (img != null)
                        img.CrossFadeAlpha(0f, 0.15f, true);
                }
                return;
            }

            string slug = profile.GetNpcSlug();
            for (int i = 0; i < portraits.Length; i++)
            {
                if (portraits[i] == null)
                    continue;

                if (
                    (slug == "butler" && i == 0)
                    || (slug == "maid" && i == 1)
                    || (slug == "chef" && i == 2)
                )
                {
                    if (profile.PortraitTexture != null)
                        portraits[i].texture = profile.PortraitTexture;
                    portraits[i].CrossFadeAlpha(1f, 0.15f, true);
                }
                else
                {
                    portraits[i].CrossFadeAlpha(0f, 0.15f, true);
                }
            }
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

            CharacterSelect =
                CharacterSelect != null
                    ? CharacterSelect
                    : FindComponent<TMP_Dropdown>(this, "Canvas/Dropdown");
            PlayerInput =
                PlayerInput != null
                    ? PlayerInput
                    : FindComponent<TMP_InputField>(this, "Canvas/PlayerInput");
            AiText = AiText != null ? AiText : FindComponent<TMP_Text>(this, "Canvas/AIImage/AIText");
            StopButton =
                StopButton != null ? StopButton : FindComponent<Button>(this, "Canvas/StopButton");

            ButlerImage =
                ButlerImage != null ? ButlerImage : FindComponent<RawImage>(this, "Canvas/ButlerImage");
            MaidImage = MaidImage != null ? MaidImage : FindComponent<RawImage>(this, "Canvas/MaidImage");
            ChefImage = ChefImage != null ? ChefImage : FindComponent<RawImage>(this, "Canvas/ChefImage");

            NotebookController =
                NotebookController != null
                    ? NotebookController
                    : FindAnyObjectByType<NotebookUIController>(FindObjectsInactive.Include);
            ExitButton =
                ExitButton != null ? ExitButton : FindComponent<Button>(this, "Canvas/ExitButton");
        }
    }
}
