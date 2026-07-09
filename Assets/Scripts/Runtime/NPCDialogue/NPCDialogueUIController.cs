using System.Collections.Generic;
using System.Linq;
using EditorAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NPCSystem
{
    [DefaultExecutionOrder(-400)]
    public class NPCDialogueUIController : MonoBehaviour
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
            nameof(dialogueManager),
            nameof(networkBridge),
            nameof(legacyKnowledgeBaseController)
        )]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [SerializeField, HideProperty, Required]
        public NPCDialogueManager dialogueManager;

        [SerializeField, HideProperty]
        public NPCDialogueNetworkBridge networkBridge;

        [SerializeField, HideProperty]
        public Behaviour legacyKnowledgeBaseController;

        [FoldoutGroup(
            "Dialogue UI",
            true,
            nameof(characterSelect),
            nameof(playerInput),
            nameof(aiText),
            nameof(stopButton)
        )]
        [SerializeField]
        EditorAttributes.Void dialogueUiGroup;

        [SerializeField, HideProperty, Required]
        public TMP_Dropdown characterSelect;

        [SerializeField, HideProperty, Required]
        public TMP_InputField playerInput;

        [SerializeField, HideProperty, Required]
        public TMP_Text aiText;

        [SerializeField, HideProperty]
        public Button stopButton;

        [FoldoutGroup("Portraits", true, nameof(butlerImage), nameof(maidImage), nameof(chefImage))]
        [SerializeField]
        EditorAttributes.Void portraitsGroup;

        [SerializeField, HideProperty]
        public RawImage butlerImage;

        [SerializeField, HideProperty]
        public RawImage maidImage;

        [SerializeField, HideProperty]
        public RawImage chefImage;

        [FoldoutGroup("Notebook / Panels", true, nameof(notebookController))]
        [SerializeField]
        EditorAttributes.Void notebookGroup;

        [SerializeField, HideProperty]
        public NotebookUIController notebookController;

        [FoldoutGroup("Exit and Startup", true, nameof(exitButton), nameof(initializeOnStart))]
        [SerializeField]
        EditorAttributes.Void exitStartupGroup;

        [SerializeField, HideProperty]
        public Button exitButton;

        [Tooltip(
            "If true, UI and dialogue systems initialize on Start. If false, they initialize deferred post-login."
        )]
        [SerializeField, HideProperty]
        public bool initializeOnStart = false;

        [Title("Runtime Status")]
        [ShowInInspector, ReadOnly]
        string ActiveProfilePreview => GetActiveProfile()?.GetDisplayName() ?? "<none>";

        [ShowInInspector, ReadOnly]
        string ActiveSlugPreview => GetActiveProfile()?.GetNpcSlug() ?? "<none>";

        [ShowInInspector, ReadOnly]
        bool HasDialogueManager => dialogueManager != null;

        [ShowInInspector, ReadOnly]
        bool IsInitialized =>
            _onDemandInitTask != null
            && (_onDemandInitTask.IsCompletedSuccessfully || _managerBound);

        private System.Threading.Tasks.Task _onDemandInitTask;
        bool _listenersBound;
        bool _managerBound;
        bool _readyForInput;
        List<NPCProfile> _profiles = new List<NPCProfile>();

        void Awake()
        {
            ResolveReferences();
        }

        async void Start()
        {
            if (initializeOnStart)
            {
                await InitializeOnDemandAsync();
            }
            else
            {
                GameObject gameplayCanvas = GetGameplayCanvas();
                if (gameplayCanvas != null)
                {
                    gameplayCanvas.SetActive(false);
                }
            }
        }

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
                    dialogueManager != null ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                    dialogueManager != null ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                    dialogueManager != null
                        ? "NPCDialogueManager is assigned."
                        : "NPCDialogueManager is NOT assigned — runtime will not function.",
                    source: nameof(NPCDialogueUIController)
                );
        }

        public System.Threading.Tasks.Task InitializeOnDemandAsync()
        {
            _onDemandInitTask ??= InitializeOnDemandInternalAsync();
            return _onDemandInitTask;
        }

        async System.Threading.Tasks.Task InitializeOnDemandInternalAsync()
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
                // Enforce that the bootstrapper completes its initialization first
                var bootstrapper = FindAnyObjectByType<NPCDialogueBootstrapper>(
                    FindObjectsInactive.Include
                );
                if (bootstrapper != null)
                {
                    await bootstrapper.InitializeOnDemandAsync();
                }
                else
                {
                    if (networkBridge == null && dialogueManager == null)
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

                    if (networkBridge != null)
                        await networkBridge.InitializeAsync();
                    else
                        await dialogueManager.InitializeAsync();
                }

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
                if (playerInput != null)
                {
                    playerInput.Select();
                    playerInput.ActivateInputField();
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

        public GameObject GetGameplayCanvas()
        {
            if (characterSelect != null)
            {
                Canvas canvas = characterSelect.GetComponentInParent<Canvas>(true);
                if (canvas != null)
                {
                    return canvas.gameObject;
                }
            }

            // Robust fallback searching active and inactive canvases across loaded scenes
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            foreach (var canvas in canvases)
            {
                if (canvas.gameObject.name == "Canvas" && canvas.gameObject.scene.isLoaded)
                {
                    return canvas.gameObject;
                }
            }
            return GameObject.Find("Canvas");
        }

        void OnExitPressed()
        {
            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.UIInput,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "UI exit pressed. Exiting game/play mode.",
                    source: nameof(NPCDialogueUIController)
                );

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            NPCPlayerCharacterController.LocalInstance?.SetUIActive(false);
            UnbindRuntimeEvents();
            UnbindUiListeners();
        }

        void OnDisable()
        {
            NPCPlayerCharacterController.LocalInstance?.SetUIActive(false);
        }

        void ResolveReferences()
        {
            if (dialogueManager == null)
            {
                dialogueManager = GetComponent<NPCDialogueManager>();
                if (dialogueManager == null)
                {
                    dialogueManager = FindAnyObjectByType<NPCDialogueManager>(
                        FindObjectsInactive.Include
                    );
                }
            }

            if (networkBridge == null)
            {
                networkBridge = GetComponent<NPCDialogueNetworkBridge>();
                if (networkBridge == null)
                {
                    networkBridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(
                        FindObjectsInactive.Include
                    );
                }
            }

            if (legacyKnowledgeBaseController == null)
            {
                legacyKnowledgeBaseController = FindObjectsByType<Behaviour>(
                        FindObjectsInactive.Include
                    )
                    .FirstOrDefault(behaviour =>
                        behaviour != null
                        && behaviour.GetType().FullName == "LLMUnitySamples.KnowledgeBaseGame"
                    );
            }

            characterSelect =
                characterSelect != null
                    ? characterSelect
                    : FindComponent<TMP_Dropdown>("Canvas/Dropdown");
            playerInput =
                playerInput != null
                    ? playerInput
                    : FindComponent<TMP_InputField>("Canvas/PlayerInput");
            aiText = aiText != null ? aiText : FindComponent<TMP_Text>("Canvas/AIImage/AIText");
            stopButton =
                stopButton != null ? stopButton : FindComponent<Button>("Canvas/StopButton");

            butlerImage =
                butlerImage != null ? butlerImage : FindComponent<RawImage>("Canvas/ButlerImage");
            maidImage = maidImage != null ? maidImage : FindComponent<RawImage>("Canvas/MaidImage");
            chefImage = chefImage != null ? chefImage : FindComponent<RawImage>("Canvas/ChefImage");

            notebookController =
                notebookController != null
                    ? notebookController
                    : FindAnyObjectByType<NotebookUIController>(FindObjectsInactive.Include);
            exitButton =
                exitButton != null ? exitButton : FindComponent<Button>("Canvas/ExitButton");
        }

        void DisableLegacyController()
        {
            if (legacyKnowledgeBaseController != null)
            {
                legacyKnowledgeBaseController.enabled = false;
            }
        }

        void BindUiListeners()
        {
            if (_listenersBound)
                return;

            if (characterSelect != null)
                characterSelect.onValueChanged.AddListener(OnCharacterSelectionChanged);
            if (playerInput != null)
            {
                playerInput.onSubmit.AddListener(OnInputFieldSubmit);
                playerInput.onValueChanged.AddListener(OnValueChanged);
            }
            if (stopButton != null)
                stopButton.onClick.AddListener(OnStopPressed);
            if (exitButton != null)
                exitButton.onClick.AddListener(OnExitPressed);
            _listenersBound = true;
        }

        void UnbindUiListeners()
        {
            if (!_listenersBound)
                return;

            if (characterSelect != null)
                characterSelect.onValueChanged.RemoveListener(OnCharacterSelectionChanged);
            if (playerInput != null)
            {
                playerInput.onSubmit.RemoveListener(OnInputFieldSubmit);
                playerInput.onValueChanged.RemoveListener(OnValueChanged);
            }
            if (stopButton != null)
                stopButton.onClick.RemoveListener(OnStopPressed);
            if (exitButton != null)
                exitButton.onClick.RemoveListener(OnExitPressed);
            _listenersBound = false;
        }

        void BindRuntimeEvents()
        {
            if (_managerBound)
                return;

            if (networkBridge != null)
            {
                networkBridge.onResponseStart.AddListener(HandleResponseStart);
                networkBridge.onResponseUpdated.AddListener(SetAIText);
                networkBridge.onResponseComplete.AddListener(HandleResponseComplete);
                networkBridge.onNPCChanged.AddListener(HandleNpcChanged);
                networkBridge.onError.AddListener(HandleError);
            }
            else if (dialogueManager != null)
            {
                dialogueManager.onResponseStart.AddListener(HandleResponseStart);
                dialogueManager.onResponseUpdated.AddListener(SetAIText);
                dialogueManager.onResponseComplete.AddListener(HandleResponseComplete);
                dialogueManager.onNPCChanged.AddListener(HandleNpcChanged);
                dialogueManager.onError.AddListener(HandleError);
            }
            else
            {
                return;
            }

            _managerBound = true;
        }

        void UnbindRuntimeEvents()
        {
            if (!_managerBound)
                return;

            if (networkBridge != null)
            {
                networkBridge.onResponseStart.RemoveListener(HandleResponseStart);
                networkBridge.onResponseUpdated.RemoveListener(SetAIText);
                networkBridge.onResponseComplete.RemoveListener(HandleResponseComplete);
                networkBridge.onNPCChanged.RemoveListener(HandleNpcChanged);
                networkBridge.onError.RemoveListener(HandleError);
            }

            if (dialogueManager != null)
            {
                dialogueManager.onResponseStart.RemoveListener(HandleResponseStart);
                dialogueManager.onResponseUpdated.RemoveListener(SetAIText);
                dialogueManager.onResponseComplete.RemoveListener(HandleResponseComplete);
                dialogueManager.onNPCChanged.RemoveListener(HandleNpcChanged);
                dialogueManager.onError.RemoveListener(HandleError);
            }

            _managerBound = false;
        }

        void PopulateDropdown()
        {
            NPCProfile[] availableProfiles =
                networkBridge != null ? networkBridge.Profiles : dialogueManager.Profiles;
            _profiles = availableProfiles.Where(profile => profile != null).ToList();
            if (characterSelect == null)
                return;

            characterSelect.ClearOptions();
            characterSelect.AddOptions(
                _profiles.Select(profile => profile.GetDisplayName()).ToList()
            );
        }

        async System.Threading.Tasks.Task SyncDropdownToCurrentProfileAsync()
        {
            if (
                (dialogueManager == null && networkBridge == null)
                || characterSelect == null
                || _profiles.Count == 0
            )
                return;

            NPCProfile activeProfile = GetActiveProfile();
            if (activeProfile == null)
            {
                characterSelect.SetValueWithoutNotify(0);
                await SelectProfileAsync(0);
                return;
            }

            int index = _profiles.FindIndex(profile => profile == activeProfile);
            if (index < 0)
                index = 0;
            characterSelect.SetValueWithoutNotify(index);
            UpdatePortrait(activeProfile);
            SetInputEnabled(true);
            _readyForInput = true;
        }

        void HandleNpcChanged(string displayName)
        {
            UpdatePortrait(GetActiveProfile());
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
            if (playerInput != null)
                playerInput.interactable = false;
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
            if (playerInput != null)
                playerInput.text = "";
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

        void SetAIText(string text)
        {
            if (aiText != null)
                aiText.text = text;
        }

        void OnCharacterSelectionChanged(int selection)
        {
            _ = SelectProfileAsync(selection);
        }

        async System.Threading.Tasks.Task SelectProfileAsync(int selection)
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
            if (networkBridge != null)
                await networkBridge.RequestNpcSelectionAsync(npcSlug);
            else
                await dialogueManager.SwitchToNPCAsync(npcSlug);
        }

        NPCProfile GetActiveProfile()
        {
            if (networkBridge != null)
                return networkBridge.currentProfile;
            if (dialogueManager != null)
                return dialogueManager.currentProfile;
            return null;
        }

        void UpdatePortrait(NPCProfile profile)
        {
            RawImage[] portraits = new[] { butlerImage, maidImage, chefImage };
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
                    if (profile.portraitTexture != null)
                        portraits[i].texture = profile.portraitTexture;
                    portraits[i].CrossFadeAlpha(1f, 0.15f, true);
                }
                else
                {
                    portraits[i].CrossFadeAlpha(0f, 0.15f, true);
                }
            }
        }

        void SetInputEnabled(bool enabled)
        {
            if (playerInput != null)
            {
                playerInput.interactable = enabled && _readyForInput;
            }
        }

        void OnInputFieldSubmit(string text)
        {
            if (!_readyForInput)
                return;

            string message = (text ?? string.Empty).Trim();
            if (message.Length == 0)
                return;

            if (playerInput != null)
                playerInput.interactable = false;

            if (networkBridge != null)
                networkBridge.SubmitPlayerMessage(message);
            else
                dialogueManager.SendMessage(message);
        }

        void OnStopPressed()
        {
            if (networkBridge != null)
                networkBridge.CancelActiveRequest();
            else
                dialogueManager.CancelRequests();
            SetAIText(string.Empty);
            SetInputEnabled(true);
        }

        void OnValueChanged(string text)
        {
            // NOP — callback required by InputSystem wiring, intentionally empty
        }

        static T FindComponent<T>(string path)
            where T : Component
        {
            GameObject go = GameObject.Find(path);
            return go != null ? go.GetComponent<T>() : null;
        }
    }
}
