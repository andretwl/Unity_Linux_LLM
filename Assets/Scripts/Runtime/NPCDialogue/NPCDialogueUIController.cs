#if !UNITY_SERVER
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NPCSystem
{
    [DefaultExecutionOrder(-400)]
    public class NPCDialogueUIController : MonoBehaviour
    {
        [Header("Runtime")]
        public NPCDialogueManager dialogueManager;
        public NPCDialogueNetworkBridge networkBridge;
        public Behaviour legacyKnowledgeBaseController;

        [Header("Dialogue UI")]
        public Dropdown characterSelect;
        public InputField playerInput;
        public Text aiText;
        public Button stopButton;

        [Header("Portraits")]
        public RawImage butlerImage;
        public RawImage maidImage;
        public RawImage chefImage;

        [Header("Notebook / Panels")]
        public NotebookUIController notebookController;

        [Header("Exit and Startup")]
        public Button exitButton;
        [Tooltip("If true, UI and dialogue systems initialize on Start. If false, they initialize deferred post-login.")]
        public bool initializeOnStart = false;

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

        public System.Threading.Tasks.Task InitializeOnDemandAsync()
        {
            _onDemandInitTask ??= InitializeOnDemandInternalAsync();
            return _onDemandInitTask;
        }

        async System.Threading.Tasks.Task InitializeOnDemandInternalAsync()
        {
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Start,
                NPCFlowLogLevel.Debug, "NPCDialogueUIController starting on demand.",
                source: nameof(NPCDialogueUIController));

            ResolveReferences();
            DisableLegacyController();
            SetInputEnabled(false);
            BindUiListeners();

            // Enforce that the bootstrapper completes its initialization first
            var bootstrapper = FindAnyObjectByType<NPCDialogueBootstrapper>(FindObjectsInactive.Include);
            if (bootstrapper != null)
            {
                await bootstrapper.InitializeOnDemandAsync();
            }
            else
            {
                if (networkBridge == null && dialogueManager == null)
                {
                    NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error, "Neither NPCDialogueManager nor NPCDialogueNetworkBridge is available.",
                        source: nameof(NPCDialogueUIController));
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

            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Success,
                NPCFlowLogLevel.Debug, "NPCDialogueUIController ready and initialized.",
                source: nameof(NPCDialogueUIController));
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
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.UIInput, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "UI exit pressed. Exiting game/play mode.", source: nameof(NPCDialogueUIController));

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            UnbindRuntimeEvents();
            UnbindUiListeners();
        }

        void ResolveReferences()
        {
            if (dialogueManager == null)
            {
                dialogueManager = GetComponent<NPCDialogueManager>();
                if (dialogueManager == null)
                {
                    dialogueManager = FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
                }
            }

            if (networkBridge == null)
            {
                networkBridge = GetComponent<NPCDialogueNetworkBridge>();
                if (networkBridge == null)
                {
                    networkBridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(FindObjectsInactive.Include);
                }
            }

            if (legacyKnowledgeBaseController == null)
            {
                legacyKnowledgeBaseController = FindObjectsByType<Behaviour>(FindObjectsInactive.Include)
                    .FirstOrDefault(behaviour => behaviour != null && behaviour.GetType().FullName == "LLMUnitySamples.KnowledgeBaseGame");
            }

            characterSelect = characterSelect != null ? characterSelect : FindComponent<Dropdown>("Canvas/Dropdown");
            playerInput = playerInput != null ? playerInput : FindComponent<InputField>("Canvas/PlayerInput");
            aiText = aiText != null ? aiText : FindComponent<Text>("Canvas/AIImage/AIText");
            stopButton = stopButton != null ? stopButton : FindComponent<Button>("Canvas/StopButton");

            butlerImage = butlerImage != null ? butlerImage : FindComponent<RawImage>("Canvas/ButlerImage");
            maidImage = maidImage != null ? maidImage : FindComponent<RawImage>("Canvas/MaidImage");
            chefImage = chefImage != null ? chefImage : FindComponent<RawImage>("Canvas/ChefImage");

            notebookController = notebookController != null ? notebookController : FindAnyObjectByType<NotebookUIController>(FindObjectsInactive.Include);
            exitButton = exitButton != null ? exitButton : FindComponent<Button>("Canvas/ExitButton");
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
            if (_listenersBound) return;

            if (characterSelect != null) characterSelect.onValueChanged.AddListener(OnCharacterSelectionChanged);
            if (playerInput != null)
            {
                playerInput.onSubmit.AddListener(OnInputFieldSubmit);
                playerInput.onValueChanged.AddListener(OnValueChanged);
            }
            if (stopButton != null) stopButton.onClick.AddListener(OnStopPressed);
            if (exitButton != null) exitButton.onClick.AddListener(OnExitPressed);
            _listenersBound = true;
        }

        void UnbindUiListeners()
        {
            if (!_listenersBound) return;

            if (characterSelect != null) characterSelect.onValueChanged.RemoveListener(OnCharacterSelectionChanged);
            if (playerInput != null)
            {
                playerInput.onSubmit.RemoveListener(OnInputFieldSubmit);
                playerInput.onValueChanged.RemoveListener(OnValueChanged);
            }
            if (stopButton != null) stopButton.onClick.RemoveListener(OnStopPressed);
            if (exitButton != null) exitButton.onClick.RemoveListener(OnExitPressed);
            _listenersBound = false;
        }

        void BindRuntimeEvents()
        {
            if (_managerBound) return;

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
            if (!_managerBound) return;

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
            NPCProfile[] availableProfiles = networkBridge != null ? networkBridge.Profiles : dialogueManager.Profiles;
            _profiles = availableProfiles.Where(profile => profile != null).ToList();
            if (characterSelect == null) return;

            characterSelect.ClearOptions();
            characterSelect.AddOptions(_profiles.Select(profile => profile.GetDisplayName()).ToList());
        }

        async System.Threading.Tasks.Task SyncDropdownToCurrentProfileAsync()
        {
            if ((dialogueManager == null && networkBridge == null) || characterSelect == null || _profiles.Count == 0) return;

            NPCProfile activeProfile = GetActiveProfile();
            if (activeProfile == null)
            {
                characterSelect.SetValueWithoutNotify(0);
                await SelectProfileAsync(0);
                return;
            }

            int index = _profiles.FindIndex(profile => profile == activeProfile);
            if (index < 0) index = 0;
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
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.UIInput, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "UI received response-start event.", source: nameof(NPCDialogueUIController),
                npcSlug: GetActiveProfile() != null ? GetActiveProfile().GetNpcSlug() : null,
                data: NPCFlowTextSanitizer.MergeSummary(new Dictionary<string, object>(), "player", playerMessage, false, 0));
            if (playerInput != null) playerInput.interactable = false;
            SetAIText("...");
        }

        void HandleResponseComplete(string npcName, string response)
        {
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.ResponseComplete, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "UI received response-complete event.", source: nameof(NPCDialogueUIController), npcSlug: npcName,
                data: NPCFlowTextSanitizer.MergeSummary(new Dictionary<string, object>(), "response", response, false, 0));
            SetAIText(response);
            if (playerInput != null)
            {
                playerInput.interactable = true;
                playerInput.text = "";
                playerInput.Select();
            }
        }

        void HandleError(string error)
        {
            string normalizedError = string.IsNullOrWhiteSpace(error) ? "Unknown dialogue error." : error.Trim();
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.UIInput, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                $"UI received dialogue error: {normalizedError}", source: nameof(NPCDialogueUIController), data: new Dictionary<string, object>
                {
                    ["error"] = normalizedError
                });
            SetAIText($"Error: {normalizedError}");
            if (GetActiveProfile() != null)
            {
                _readyForInput = true;
                SetInputEnabled(true);
            }
        }

        void SetAIText(string text)
        {
            if (aiText != null) aiText.text = text;
        }

        void OnCharacterSelectionChanged(int selection)
        {
            _ = SelectProfileAsync(selection);
        }

        async System.Threading.Tasks.Task SelectProfileAsync(int selection)
        {
            if (selection < 0 || selection >= _profiles.Count)
            {
                NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "UI profile selection index out of range.", source: nameof(NPCDialogueUIController), data: new Dictionary<string, object>
                    {
                        ["selection"] = selection,
                        ["profileCount"] = _profiles.Count
                    });
                return;
            }
            NPCProfile profile = _profiles[selection];
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Start, NPCFlowLogLevel.Info,
                "UI requested NPC switch.", source: nameof(NPCDialogueUIController), npcSlug: profile.GetNpcSlug(), data: new Dictionary<string, object>
                {
                    ["selection"] = selection
                });
            _readyForInput = false;
            SetInputEnabled(false);
            UpdatePortrait(profile);
            if (networkBridge != null)
                await networkBridge.RequestNpcSelectionAsync(profile.GetNpcSlug());
            else
                await dialogueManager.SwitchToNPCAsync(profile.GetNpcSlug());
        }

        void OnInputFieldSubmit(string question)
        {
            NPCProfile activeProfile = GetActiveProfile();
            if (!_readyForInput || string.IsNullOrWhiteSpace(question) || activeProfile == null)
            {
                NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.UIInput, NPCFlowStatus.Skipped, NPCFlowLogLevel.Info,
                    "UI input submit ignored.", source: nameof(NPCDialogueUIController), data: new Dictionary<string, object>
                    {
                        ["readyForInput"] = _readyForInput,
                        ["hasText"] = !string.IsNullOrWhiteSpace(question),
                        ["hasManager"] = dialogueManager != null || networkBridge != null,
                        ["hasCurrentProfile"] = activeProfile != null
                    });
                return;
            }
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.UIInput, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "UI submitted player input.", source: nameof(NPCDialogueUIController), npcSlug: activeProfile.GetNpcSlug(),
                data: NPCFlowTextSanitizer.MergeSummary(new Dictionary<string, object>(), "player", question, false, 0));
            if (networkBridge != null)
                networkBridge.SubmitPlayerMessage(question.Trim());
            else
                dialogueManager.SendMessage(question.Trim());
        }

        void SetInputEnabled(bool enabled)
        {
            if (playerInput == null) return;

            playerInput.interactable = enabled;
            if (enabled)
            {
                playerInput.Select();
            }
        }

        void OnStopPressed()
        {
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.UIInput, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "UI stop/cancel pressed.", source: nameof(NPCDialogueUIController),
                npcSlug: GetActiveProfile() != null ? GetActiveProfile().GetNpcSlug() : null);
            if (networkBridge != null)
                networkBridge.CancelActiveRequest();
            else
                dialogueManager?.CancelRequests();
            if (playerInput != null)
            {
                playerInput.interactable = true;
                playerInput.Select();
            }
        }

        void UpdatePortrait(NPCProfile profile)
        {
            RawImage[] portraits = new[] { butlerImage, maidImage, chefImage };
            foreach (RawImage portrait in portraits)
            {
                if (portrait != null) portrait.gameObject.SetActive(false);
            }

            if (profile == null) return;

            RawImage target = ResolvePortraitTarget(profile);
            if (target == null) return;

            if (profile.portraitTexture != null)
            {
                target.texture = profile.portraitTexture;
            }
            target.gameObject.SetActive(true);
        }

        RawImage ResolvePortraitTarget(NPCProfile profile)
        {
            string slug = profile.GetNpcSlug();
            if (slug.Contains("butler")) return butlerImage != null ? butlerImage : maidImage != null ? maidImage : chefImage;
            if (slug.Contains("maid")) return maidImage != null ? maidImage : butlerImage != null ? butlerImage : chefImage;
            if (slug.Contains("chef")) return chefImage != null ? chefImage : butlerImage != null ? butlerImage : maidImage;
            return butlerImage != null ? butlerImage : maidImage != null ? maidImage : chefImage;
        }

        void OnValueChanged(string newText)
        {
            if (!IsSubmitPressed()) return;
            if (playerInput != null && string.IsNullOrWhiteSpace(playerInput.text))
            {
                playerInput.text = string.Empty;
            }
        }

        bool IsSubmitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
#else
            return Input.GetKey(KeyCode.Return);
#endif
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            bool mouseClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            Vector2 mousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            bool mouseClicked = Input.GetMouseButtonDown(0);
            Vector2 mousePosition = Input.mousePosition;
#endif
            if (!mouseClicked) return;

            notebookController?.HandleGlobalClick(mousePosition);
        }

        static T FindComponent<T>(string path) where T : Component
        {
            GameObject gameObject = FindObject(path);
            return gameObject != null ? gameObject.GetComponent<T>() : null;
        }

        NPCProfile GetActiveProfile()
        {
            if (networkBridge != null && networkBridge.currentProfile != null) return networkBridge.currentProfile;
            return dialogueManager != null ? dialogueManager.currentProfile : null;
        }

        static GameObject FindObject(string path)
        {
            return GameObject.Find(path);
        }
    }
}
#endif // !UNITY_SERVER
