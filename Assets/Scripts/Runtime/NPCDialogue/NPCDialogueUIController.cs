using System.Collections.Generic;
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
                DialogueManager.OnResponseUpdated.RemoveListener(HandleResponseUpdated);
                DialogueManager.OnResponseComplete.RemoveListener(HandleResponseComplete);
                DialogueManager.OnError.RemoveListener(HandleError);
                _listenersBound = false;
            }
        }

        void OnDisable()
        {
            if (DialogueManager != null && _listenersBound)
            {
                DialogueManager.OnNpcChanged.RemoveListener(HandleNpcChanged);
                DialogueManager.OnResponseStart.RemoveListener(HandleResponseStart);
                DialogueManager.OnResponseUpdated.RemoveListener(HandleResponseUpdated);
                DialogueManager.OnResponseComplete.RemoveListener(HandleResponseComplete);
                DialogueManager.OnError.RemoveListener(HandleError);
                _listenersBound = false;
            }
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
            return DialogueManager != null ? DialogueManager.currentProfile : null;
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

        // ── UI listeners ───────────────────────────────────────────────-

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
                DialogueManager.OnResponseUpdated.AddListener(HandleResponseUpdated);
                DialogueManager.OnResponseComplete.AddListener(HandleResponseComplete);
                DialogueManager.OnError.AddListener(HandleError);
            }
            _listenersBound = true;
        }

        public GameObject GetGameplayCanvas()
        {
            // Find the top-level gameplay canvas; may be inactive initially
            var canvas = GetComponentInChildren<Canvas>(includeInactive: true);
            if (canvas != null)
                return canvas.gameObject;
            return GameObject.Find("GameplayCanvas");
        }

        public async System.Threading.Tasks.Task InitializeOnDemandAsync()
        {
            await InitializeOnDemandInternalAsync();
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
    }
}
