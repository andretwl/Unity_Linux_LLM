using EditorAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace NPCSystem
{
    public class NotebookUIController : MonoBehaviour
    {
        [HelpBox(
            "Manages the in-game notebook/map/solve UI panels. Subscribes to NPCDialogueManager or NPCDialogueNetworkBridge to reflect evidence state in notes text.",
            MessageMode.Log,
            drawAbove: true
        )]
        [SerializeField]
        EditorAttributes.Void _docsGroup;

        [FoldoutGroup("References", true, nameof(DialogueManager), nameof(NetworkBridge))]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [FormerlySerializedAs("dialogueManager")]
        [SerializeField, HideProperty, Required]
        public NPCDialogueManager DialogueManager;

        [FormerlySerializedAs("networkBridge")]
        [SerializeField, HideProperty]
        public NPCDialogueNetworkBridge NetworkBridge;

        [FoldoutGroup(
            "Notebook Buttons",
            true,
            nameof(NotesButton),
            nameof(MapButton),
            nameof(SolveButton),
            nameof(HelpButton),
            nameof(SubmitButton)
        )]
        [SerializeField]
        EditorAttributes.Void notebookButtonsGroup;

        [FormerlySerializedAs("notesButton")]
        [SerializeField, HideProperty, Required]
        public Button NotesButton;

        [FormerlySerializedAs("mapButton")]
        [SerializeField, HideProperty]
        public Button MapButton;

        [FormerlySerializedAs("solveButton")]
        [SerializeField, HideProperty]
        public Button SolveButton;

        [FormerlySerializedAs("helpButton")]
        [SerializeField, HideProperty]
        public Button HelpButton;

        [FormerlySerializedAs("submitButton")]
        [SerializeField, HideProperty]
        public Button SubmitButton;

        [FoldoutGroup(
            "Notebook UI Panels",
            true,
            nameof(NotebookImage),
            nameof(NotesPanel),
            nameof(SolvePanel),
            nameof(HelpPanel),
            nameof(MapImage),
            nameof(SuccessImage),
            nameof(FailText)
        )]
        [SerializeField]
        EditorAttributes.Void notebookPanelsGroup;

        [FormerlySerializedAs("notebookImage")]
        [SerializeField, HideProperty, Required]
        public RawImage NotebookImage;

        [FormerlySerializedAs("notesPanel")]
        [SerializeField, HideProperty]
        public GameObject NotesPanel;

        [FormerlySerializedAs("solvePanel")]
        [SerializeField, HideProperty]
        public GameObject SolvePanel;

        [FormerlySerializedAs("helpPanel")]
        [SerializeField, HideProperty]
        public GameObject HelpPanel;

        [FormerlySerializedAs("mapImage")]
        [SerializeField, HideProperty]
        public RawImage MapImage;

        [FormerlySerializedAs("successImage")]
        [SerializeField, HideProperty]
        public RawImage SuccessImage;

        [FormerlySerializedAs("failText")]
        [SerializeField, HideProperty]
        public TMP_Text FailText;

        [FoldoutGroup("Dropdown Answers", true, nameof(Answer1), nameof(Answer2), nameof(Answer3))]
        [SerializeField]
        EditorAttributes.Void dropdownAnswersGroup;

        [FormerlySerializedAs("answer1")]
        [SerializeField, HideProperty]
        public TMP_Dropdown Answer1;

        [FormerlySerializedAs("answer2")]
        [SerializeField, HideProperty]
        public TMP_Dropdown Answer2;

        [FormerlySerializedAs("answer3")]
        [SerializeField, HideProperty]
        public TMP_Dropdown Answer3;

        [FoldoutGroup("Notes Text", true, nameof(NotesText1), nameof(NotesText2))]
        [SerializeField]
        EditorAttributes.Void notesTextGroup;

        [FormerlySerializedAs("notesText1")]
        [SerializeField, HideProperty, Required]
        public TMP_Text NotesText1;

        [FormerlySerializedAs("notesText2")]
        [SerializeField, HideProperty, Required]
        public TMP_Text NotesText2;

        [Title("Correct Solve Answers")]
        [FormerlySerializedAs("correctAnswer1")]
        [SerializeField]
        public string CorrectAnswer1 = "Professor Pluot";

        [FormerlySerializedAs("correctAnswer2")]
        [SerializeField]
        public string CorrectAnswer2 = "Living Room";

        [FormerlySerializedAs("correctAnswer3")]
        [SerializeField]
        public string CorrectAnswer3 = "A Hollow Bible";

        [Title("Runtime Status")]
        [ShowInInspector, ReadOnly]
        string CurrentNpcSlug =>
            NetworkBridge?.currentProfile?.GetNpcSlug()
            ?? DialogueManager?.CurrentProfile?.GetNpcSlug()
            ?? "<none>";

        [ShowInInspector, ReadOnly]
        bool HasEvidence =>
            (DialogueManager?.CaptureEvidenceSnapshot() ?? new NPCEvidenceStateSnapshot())
                .discoveredClues
                .Count > 0
            || (DialogueManager?.CaptureEvidenceSnapshot() ?? new NPCEvidenceStateSnapshot())
                .obtainedItems
                .Count > 0;

        string _defaultNotesPageLeft = string.Empty;
        string _defaultNotesPageRight = string.Empty;
        bool _listenersBound;
        bool _runtimeEventsBound;
        int _overlayOpenedFrame = -1;

        void Awake()
        {
            ResolveReferences();
            CacheDefaultNotebookText();
            BindListeners();
            BindRuntimeEvents();
            RefreshNotebookState();
        }

        void OnDestroy()
        {
            UnbindRuntimeEvents();
            UnbindListeners();
        }

        [Button("Refresh Notebook State")]
        void RefreshNotebookStateButton()
        {
            RefreshNotebookState();
        }

        [Button("Reset Notebook to Defaults")]
        void ResetNotebookToDefaults()
        {
            ApplyNotebookState(
                new NPCNotebookStateMessage
                {
                    notesPageLeft = _defaultNotesPageLeft,
                    notesPageRight = _defaultNotesPageRight,
                }
            );
        }

        void ResolveReferences()
        {
            DialogueManager =
                DialogueManager != null
                    ? DialogueManager
                    : FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
            NetworkBridge =
                NetworkBridge != null
                    ? NetworkBridge
                    : FindAnyObjectByType<NPCDialogueNetworkBridge>(FindObjectsInactive.Include);
            NotesButton =
                NotesButton != null ? NotesButton : FindComponent<Button>("Canvas/NotesButton");
            MapButton = MapButton != null ? MapButton : FindComponent<Button>("Canvas/MapButton");
            SolveButton =
                SolveButton != null ? SolveButton : FindComponent<Button>("Canvas/SolveButton");
            HelpButton =
                HelpButton != null ? HelpButton : FindComponent<Button>("Canvas/HelpButton");
            SubmitButton =
                SubmitButton != null
                    ? SubmitButton
                    : FindComponent<Button>("Canvas/NotebookImage/SolvePanel/SubmitButton");
            NotebookImage =
                NotebookImage != null
                    ? NotebookImage
                    : FindComponent<RawImage>("Canvas/NotebookImage");
            NotesPanel =
                NotesPanel != null ? NotesPanel : FindObject("Canvas/NotebookImage/NotesPanel");
            SolvePanel =
                SolvePanel != null ? SolvePanel : FindObject("Canvas/NotebookImage/SolvePanel");
            HelpPanel =
                HelpPanel != null ? HelpPanel : FindObject("Canvas/NotebookImage/HelpPanel");
            MapImage = MapImage != null ? MapImage : FindComponent<RawImage>("Canvas/MapImage");
            SuccessImage =
                SuccessImage != null
                    ? SuccessImage
                    : FindComponent<RawImage>("Canvas/SuccessImage");
            FailText =
                FailText != null
                    ? FailText
                    : FindComponent<TMP_Text>("Canvas/NotebookImage/SolvePanel/FailText");
            Answer1 =
                Answer1 != null
                    ? Answer1
                    : FindComponent<TMP_Dropdown>("Canvas/NotebookImage/SolvePanel/Answer1");
            Answer2 =
                Answer2 != null
                    ? Answer2
                    : FindComponent<TMP_Dropdown>("Canvas/NotebookImage/SolvePanel/Answer2");
            Answer3 =
                Answer3 != null
                    ? Answer3
                    : FindComponent<TMP_Dropdown>("Canvas/NotebookImage/SolvePanel/Answer3");
            NotesText1 =
                NotesText1 != null
                    ? NotesText1
                    : FindComponent<TMP_Text>("Canvas/NotebookImage/NotesPanel/NotesText1");
            NotesText2 =
                NotesText2 != null
                    ? NotesText2
                    : FindComponent<TMP_Text>("Canvas/NotebookImage/NotesPanel/NotesText2");
        }

        void CacheDefaultNotebookText()
        {
            _defaultNotesPageLeft = NotesText1 != null ? NotesText1.text : string.Empty;
            _defaultNotesPageRight = NotesText2 != null ? NotesText2.text : string.Empty;
        }

        void BindListeners()
        {
            if (_listenersBound)
                return;

            if (NotesButton != null)
                NotesButton.onClick.AddListener(ShowNotes);
            if (MapButton != null)
                MapButton.onClick.AddListener(ShowMap);
            if (SolveButton != null)
                SolveButton.onClick.AddListener(ShowSolve);
            if (HelpButton != null)
                HelpButton.onClick.AddListener(ShowHelp);
            if (SubmitButton != null)
                SubmitButton.onClick.AddListener(SubmitAnswer);
            if (Answer1 != null)
                Answer1.onValueChanged.AddListener(HideFail);
            if (Answer2 != null)
                Answer2.onValueChanged.AddListener(HideFail);
            if (Answer3 != null)
                Answer3.onValueChanged.AddListener(HideFail);

            _listenersBound = true;
        }

        void UnbindListeners()
        {
            if (!_listenersBound)
                return;

            if (NotesButton != null)
                NotesButton.onClick.RemoveListener(ShowNotes);
            if (MapButton != null)
                MapButton.onClick.RemoveListener(ShowMap);
            if (SolveButton != null)
                SolveButton.onClick.RemoveListener(ShowSolve);
            if (HelpButton != null)
                HelpButton.onClick.RemoveListener(ShowHelp);
            if (SubmitButton != null)
                SubmitButton.onClick.RemoveListener(SubmitAnswer);
            if (Answer1 != null)
                Answer1.onValueChanged.RemoveListener(HideFail);
            if (Answer2 != null)
                Answer2.onValueChanged.RemoveListener(HideFail);
            if (Answer3 != null)
                Answer3.onValueChanged.RemoveListener(HideFail);

            _listenersBound = false;
        }

        void BindRuntimeEvents()
        {
            if (_runtimeEventsBound)
                return;

            if (NetworkBridge != null)
            {
                NetworkBridge.OnNotebookStateChanged.AddListener(ApplyNotebookState);
                _runtimeEventsBound = true;
            }
            else if (DialogueManager != null)
            {
                DialogueManager.OnNpcChanged.AddListener(HandleNpcChanged);
                DialogueManager.OnResponseComplete.AddListener(HandleResponseComplete);
                _runtimeEventsBound = true;
            }
        }

        void UnbindRuntimeEvents()
        {
            if (!_runtimeEventsBound)
                return;

            if (NetworkBridge != null)
            {
                NetworkBridge.OnNotebookStateChanged.RemoveListener(ApplyNotebookState);
            }

            if (DialogueManager != null)
            {
                DialogueManager.OnNpcChanged.RemoveListener(HandleNpcChanged);
                DialogueManager.OnResponseComplete.RemoveListener(HandleResponseComplete);
            }

            _runtimeEventsBound = false;
        }

        public void ShowNotes()
        {
            MarkOverlayOpenedThisFrame();
            RefreshNotebookState();
            if (NotesPanel != null)
                NotesPanel.SetActive(true);
            if (HelpPanel != null)
                HelpPanel.SetActive(false);
            if (SolvePanel != null)
                SolvePanel.SetActive(false);
            if (NotebookImage != null)
                NotebookImage.gameObject.SetActive(true);
        }

        public void ShowMap()
        {
            MarkOverlayOpenedThisFrame();
            if (MapImage != null)
                MapImage.gameObject.SetActive(true);
        }

        public void ShowSolve()
        {
            MarkOverlayOpenedThisFrame();
            HideFail(0);
            if (NotesPanel != null)
                NotesPanel.SetActive(false);
            if (HelpPanel != null)
                HelpPanel.SetActive(false);
            if (SolvePanel != null)
                SolvePanel.SetActive(true);
            if (NotebookImage != null)
                NotebookImage.gameObject.SetActive(true);
        }

        public void ShowHelp()
        {
            MarkOverlayOpenedThisFrame();
            if (NotesPanel != null)
                NotesPanel.SetActive(false);
            if (HelpPanel != null)
                HelpPanel.SetActive(true);
            if (SolvePanel != null)
                SolvePanel.SetActive(false);
            if (NotebookImage != null)
                NotebookImage.gameObject.SetActive(true);
        }

        public void HideFail(int selection)
        {
            if (FailText != null)
                FailText.gameObject.SetActive(false);
        }

        public void SubmitAnswer()
        {
            if (Answer1 == null || Answer2 == null || Answer3 == null || SuccessImage == null)
                return;

            if (
                SelectedText(Answer1) == CorrectAnswer1
                && SelectedText(Answer2) == CorrectAnswer2
                && SelectedText(Answer3) == CorrectAnswer3
            )
            {
                MarkOverlayOpenedThisFrame();
                NPCFlowLogger
                    .FindOrCreate()
                    .Log(
                        NPCFlowStage.UIInput,
                        NPCFlowStatus.Success,
                        NPCFlowLogLevel.Info,
                        "Mystery solve answer accepted.",
                        source: nameof(NotebookUIController)
                    );
                if (NotebookImage != null)
                    NotebookImage.gameObject.SetActive(false);
                SuccessImage.gameObject.SetActive(true);
            }
            else if (FailText != null)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    .Log(
                        NPCFlowStage.UIInput,
                        NPCFlowStatus.Warning,
                        NPCFlowLogLevel.Warning,
                        "Mystery solve answer rejected.",
                        source: nameof(NotebookUIController)
                    );
                FailText.gameObject.SetActive(true);
            }
        }

        public void HandleGlobalClick(Vector2 mousePosition)
        {
            // Button onClick is processed by EventSystem.Update before MonoBehaviour.Update.
            // Without this guard, the same click that opens Notes/Map/Solve/Help is seen here
            // as an "outside overlay" click and immediately closes the overlay again.
            if (_overlayOpenedFrame == Time.frameCount)
                return;

            foreach (RawImage image in new[] { NotebookImage, MapImage, SuccessImage })
            {
                if (
                    image != null
                    && image.IsActive()
                    && !RectTransformUtility.RectangleContainsScreenPoint(
                        image.rectTransform,
                        mousePosition
                    )
                )
                {
                    image.gameObject.SetActive(false);
                }
            }
        }

        void MarkOverlayOpenedThisFrame()
        {
            _overlayOpenedFrame = Time.frameCount;
        }

        void HandleNpcChanged(string npcName)
        {
            RefreshNotebookState();
        }

        void HandleResponseComplete(string npcName, string response)
        {
            RefreshNotebookState();
        }

        void RefreshNotebookState()
        {
            if (NetworkBridge != null)
            {
                ApplyNotebookState(NetworkBridge.CurrentNotebookState);
                return;
            }

            if (DialogueManager == null)
            {
                ApplyNotebookState(
                    new NPCNotebookStateMessage
                    {
                        notesPageLeft = _defaultNotesPageLeft,
                        notesPageRight = _defaultNotesPageRight,
                    }
                );
                return;
            }

            NPCNotebookStateMessage message = NPCNotebookStateFormatter.Build(
                DialogueManager.CaptureEvidenceSnapshot(),
                DialogueManager.CurrentProfile != null
                    ? DialogueManager.CurrentProfile.GetNpcSlug()
                    : string.Empty
            );
            ApplyNotebookState(message);
        }

        void ApplyNotebookState(NPCNotebookStateMessage message)
        {
            if (NotesText1 != null)
            {
                NotesText1.text = string.IsNullOrWhiteSpace(message.notesPageLeft)
                    ? _defaultNotesPageLeft
                    : message.notesPageLeft;
            }

            if (NotesText2 != null)
            {
                NotesText2.text = string.IsNullOrWhiteSpace(message.notesPageRight)
                    ? _defaultNotesPageRight
                    : message.notesPageRight;
            }
        }

        static string SelectedText(TMP_Dropdown dropdown)
        {
            if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
                return string.Empty;
            int value = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
            return dropdown.options[value].text;
        }

        static T FindComponent<T>(string path)
            where T : Component
        {
            GameObject gameObject = FindObject(path);
            return gameObject != null ? gameObject.GetComponent<T>() : null;
        }

        static GameObject FindObject(string path)
        {
            return GameObject.Find(path);
        }

        public bool IsOpen =>
            (NotebookImage != null && NotebookImage.IsActive())
            || (MapImage != null && MapImage.IsActive())
            || (SuccessImage != null && SuccessImage.IsActive());

        public void ToggleNotebook()
        {
            if (IsOpen)
            {
                if (NotebookImage != null)
                    NotebookImage.gameObject.SetActive(false);
                if (MapImage != null)
                    MapImage.gameObject.SetActive(false);
                if (SuccessImage != null)
                    SuccessImage.gameObject.SetActive(false);
            }
            else
            {
                ShowNotes();
            }
        }
    }
}
