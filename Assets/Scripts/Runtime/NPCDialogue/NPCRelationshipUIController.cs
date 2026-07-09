using EditorAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace NPCSystem
{
    [DefaultExecutionOrder(-350)]
    public class NPCRelationshipUIController : MonoBehaviour
    {
        [FoldoutGroup("References", true, nameof(_trustSlider), nameof(_moodLabel), nameof(_dialogueCountLabel))]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [SerializeField, HideProperty, FormerlySerializedAs("trustSlider")]
        Slider _trustSlider;

        [SerializeField, HideProperty, FormerlySerializedAs("moodLabel")]
        TMP_Text _moodLabel;

        [SerializeField, HideProperty, FormerlySerializedAs("dialogueCountLabel")]
        TMP_Text _dialogueCountLabel;

        [FoldoutGroup("Behaviour", true, nameof(autoHideWhenNoNpc))]
        [SerializeField]
        EditorAttributes.Void behaviourGroup;

        [SerializeField, HideProperty]
        bool autoHideWhenNoNpc = true;

        [Title("Runtime Status")]
        [ShowInInspector, ReadOnly]
        string ActiveNpcPreview => string.IsNullOrWhiteSpace(_lastNpcSlug) ? "<none>" : _lastNpcSlug;

        [ShowInInspector, ReadOnly]
        int TrustPreview => _lastTrust;

        [ShowInInspector, ReadOnly]
        string MoodPreview => _lastMood;

        CanvasGroup _canvasGroup;
        string _lastNpcSlug;
        int _lastTrust;
        string _lastMood;

        void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public void Refresh(NPCEvidenceState state, string npcSlug, int dialogueCount)
        {
            bool hasNpc = !string.IsNullOrWhiteSpace(npcSlug) && state != null;

            if (hasNpc)
            {
                _lastNpcSlug = npcSlug;
                _lastTrust = state.GetNpcTrust(npcSlug);
                _lastMood = state.GetNpcMood(npcSlug);
            }

            UpdateTrustBar(hasNpc, state, npcSlug);
            UpdateMoodLabel(hasNpc, state, npcSlug);
            UpdateDialogueCount(hasNpc, dialogueCount);
            UpdateVisibility(hasNpc);
        }

        void UpdateTrustBar(bool hasNpc, NPCEvidenceState state, string npcSlug)
        {
            if (_trustSlider == null)
                return;

            if (hasNpc)
            {
                int trust = state.GetNpcTrust(npcSlug);
                _trustSlider.value = trust;

                var fillImage = _trustSlider.fillRect?.GetComponent<Image>();
                if (fillImage != null)
                    fillImage.color = TrustColor(trust);
            }
            else
            {
                _trustSlider.value = _trustSlider.minValue;
            }
        }

        void UpdateMoodLabel(bool hasNpc, NPCEvidenceState state, string npcSlug)
        {
            if (_moodLabel == null)
                return;

            if (hasNpc)
            {
                string mood = state.GetNpcMood(npcSlug);
                string label = state.GetTrustLabel(npcSlug);
                _moodLabel.text = $"{mood} \u00b7 {label}";
            }
            else
            {
                _moodLabel.text = "--";
            }
        }

        void UpdateDialogueCount(bool hasNpc, int dialogueCount)
        {
            if (_dialogueCountLabel == null)
                return;

            _dialogueCountLabel.text = hasNpc ? dialogueCount.ToString() : "0";
        }

        void UpdateVisibility(bool hasNpc)
        {
            if (_canvasGroup == null || !autoHideWhenNoNpc)
                return;

            _canvasGroup.alpha = hasNpc ? 1f : 0f;
            _canvasGroup.interactable = hasNpc;
            _canvasGroup.blocksRaycasts = hasNpc;
        }

        static Color TrustColor(int trust)
        {
            if (trust >= 80)
                return new Color(0.2f, 0.8f, 0.2f);
            if (trust >= 60)
                return new Color(0.6f, 0.8f, 0.2f);
            if (trust >= 40)
                return Color.yellow;
            if (trust >= 20)
                return new Color(1f, 0.6f, 0f);
            return new Color(0.9f, 0.2f, 0.2f);
        }
    }
}
