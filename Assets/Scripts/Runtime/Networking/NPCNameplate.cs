using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class NPCNameplate : MonoBehaviour
    {
        [Header("References")]
        [FormerlySerializedAs("nameLabel")]
        [SerializeField]
        TextMeshProUGUI _nameLabel;

        NPCServerCharacter _npc;
        Camera _camera;

        void Start()
        {
            _camera = Camera.main;

            if (_nameLabel == null)
                _nameLabel = GetComponentInChildren<TextMeshProUGUI>();

            _npc = GetComponentInParent<NPCServerCharacter>();
            if (_npc != null)
            {
                _nameLabel.text = _npc.DisplayName;
                _npc.npcDisplayName.OnValueChanged += OnNpcNameChanged;
            }
        }

        void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                    return;
            }
            transform.forward = _camera.transform.forward;
        }

        void OnNpcNameChanged(string _, string newValue)
        {
            if (_nameLabel != null)
                _nameLabel.text = string.IsNullOrWhiteSpace(newValue) ? "NPC" : newValue;
        }

        void OnDestroy()
        {
            if (_npc != null)
                _npc.npcDisplayName.OnValueChanged -= OnNpcNameChanged;
        }
    }
}
