using System.Collections.Generic;
using EditorAttributes;
using TMPro;
using UnityEngine;
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

        System.Threading.Tasks.Task _onDemandInitTask;
        bool _listenersBound;
        bool _managerBound;
        bool _readyForInput;
        List<NPCProfile> _profiles = new List<NPCProfile>();

        // ── Lifecycle ──────────────────────────────────────────────────

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

        // ── Public API ──────────────────────────────────────────────────

        public System.Threading.Tasks.Task InitializeOnDemandAsync()
        {
            _onDemandInitTask ??= InitializeOnDemandInternalAsync();
            return _onDemandInitTask;
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

        // ── UI Listener Binding ─────────────────────────────────────────

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

        // ── Runtime Event Binding ──────────────────────────────────────

        void BindRuntimeEvents()
        {
            if (_managerBound)
                return;

            if (networkBridge != null)
            {
                networkBridge.OnResponseStart.AddListener(HandleResponseStart);
                networkBridge.OnResponseUpdated.AddListener(SetAIText);
                networkBridge.OnResponseComplete.AddListener(HandleResponseComplete);
                networkBridge.OnNpcChanged.AddListener(HandleNpcChanged);
                networkBridge.OnError.AddListener(HandleError);
            }
            else if (dialogueManager != null)
            {
                dialogueManager.OnResponseStart.AddListener(HandleResponseStart);
                dialogueManager.OnResponseUpdated.AddListener(SetAIText);
                dialogueManager.OnResponseComplete.AddListener(HandleResponseComplete);
                dialogueManager.OnNpcChanged.AddListener(HandleNpcChanged);
                dialogueManager.OnError.AddListener(HandleError);
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
                networkBridge.OnResponseStart.RemoveListener(HandleResponseStart);
                networkBridge.OnResponseUpdated.RemoveListener(SetAIText);
                networkBridge.OnResponseComplete.RemoveListener(HandleResponseComplete);
                networkBridge.OnNpcChanged.RemoveListener(HandleNpcChanged);
                networkBridge.OnError.RemoveListener(HandleError);
            }

            if (dialogueManager != null)
            {
                dialogueManager.OnResponseStart.RemoveListener(HandleResponseStart);
                dialogueManager.OnResponseUpdated.RemoveListener(SetAIText);
                dialogueManager.OnResponseComplete.RemoveListener(HandleResponseComplete);
                dialogueManager.OnNpcChanged.RemoveListener(HandleNpcChanged);
                dialogueManager.OnError.RemoveListener(HandleError);
            }

            _managerBound = false;
        }

        // ── Helpers ────────────────────────────────────────────────────

        NPCProfile GetActiveProfile()
        {
            if (networkBridge != null)
                return networkBridge.currentProfile;
            if (dialogueManager != null)
                return dialogueManager.currentProfile;
            return null;
        }

        void SetInputEnabled(bool enabled)
        {
            if (playerInput != null)
            {
                playerInput.interactable = enabled && _readyForInput;
            }
        }

        void SetAIText(string text)
        {
            if (aiText != null)
                aiText.text = text;
        }

        void OnValueChanged(string text)
        {
            // NOP — callback required by InputSystem wiring, intentionally empty
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

        static T FindComponent<T>(string path)
            where T : Component
        {
            GameObject go = GameObject.Find(path);
            return go != null ? go.GetComponent<T>() : null;
        }
    }
}
