#if !UNITY_SERVER
using EditorAttributes;
using Void = EditorAttributes.Void;
using UnityEngine;

namespace NPCSystem.EditorUX
{
    /// <summary>
    /// Small project-owned surface for proving EditorAttributes patterns before applying them broadly.
    /// It performs only local, explicit diagnostics; no network calls are made automatically in edit mode.
    /// </summary>
    public sealed class NPCInspectorDiagnosticsPilot : MonoBehaviour
    {
        [Title("NPC Inspector Diagnostics Pilot")]
        [HelpBox(
            "Pilot component for validating EditorAttributes usage around LocalAI, Qdrant, and NPC dialogue scene references.",
            MessageMode.Log,
            drawAbove: true
        )]
        [FoldoutGroup("Scene References", true, nameof(dialogueManager), nameof(qdrantRag))]
        [SerializeField]
        private Void referencesGroup;

        [SerializeField, HideProperty, Required]
        NPCDialogueManager dialogueManager;

        [SerializeField, HideProperty, ShowField(nameof(useQdrantDiagnostics))]
        QdrantRAGService qdrantRag;

        [SerializeField, HideProperty]
        bool useQdrantDiagnostics = true;

        [FoldoutGroup("LocalAI Settings", true, nameof(localAiHost), nameof(localAiPort), nameof(localAiModel))]
        [SerializeField]
        private Void localAiGroup;

        [SerializeField, HideProperty]
        string localAiHost = "localhost";

        [SerializeField, HideProperty]
        int localAiPort = 8080;

        [SerializeField, HideProperty]
        string localAiModel = "default-llm";

        [FoldoutGroup("Diagnostics Status", true, nameof(lastValidationStatus))]
        [SerializeField]
        private Void diagnosticsStatusGroup;

        [SerializeField, HideProperty, ReadOnly]
        string lastValidationStatus = "Not validated yet.";

        [ShowInInspector]
        string LocalAiBaseUrl => $"http://{localAiHost}:{localAiPort}/v1";

        [ShowInInspector]
        string QdrantCollection => qdrantRag == null ? "<none>" : qdrantRag.collectionName;

        void Reset()
        {
            AutoAssignSceneReferences();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying)
                return;

            if (dialogueManager == null || qdrantRag == null)
            {
                AutoAssignSceneReferences();
            }
        }
#endif

        [Button("Auto Assign Scene References")]
        void AutoAssignSceneReferences()
        {
            if (dialogueManager == null)
            {
                dialogueManager = FindAnyObjectByType<NPCDialogueManager>(
                    FindObjectsInactive.Include
                );
            }

            if (qdrantRag == null)
            {
                qdrantRag = FindAnyObjectByType<QdrantRAGService>(FindObjectsInactive.Include);
            }

            lastValidationStatus =
                dialogueManager == null
                    ? "Dialogue manager not found."
                    : $"References assigned. LocalAI preview: {LocalAiBaseUrl}";
        }

        [Button("Validate LocalAI Settings")]
        void ValidateLocalAiSettings()
        {
            lastValidationStatus =
                HasLocalAiHost() && HasValidLocalAiPort() && HasLocalAiModel()
                    ? $"LocalAI settings look valid: {LocalAiBaseUrl} model={localAiModel}"
                    : "LocalAI settings are incomplete. Check host, port, and model.";
        }

        [Button(
            nameof(useQdrantDiagnostics),
            ConditionResult.ShowHide,
            buttonLabel: "Validate Qdrant Settings"
        )]
        void ValidateQdrantSettings()
        {
            if (!useQdrantDiagnostics)
            {
                lastValidationStatus = "Qdrant diagnostics are disabled for this pilot component.";
                return;
            }

            if (qdrantRag == null)
            {
                lastValidationStatus =
                    "Qdrant diagnostics enabled but no QdrantRAGService is assigned.";
                return;
            }

            bool hasUrl =
                !string.IsNullOrWhiteSpace(qdrantRag.qdrantUrl)
                && (
                    qdrantRag.qdrantUrl.StartsWith("http://")
                    || qdrantRag.qdrantUrl.StartsWith("https://")
                );
            bool hasCollection = !string.IsNullOrWhiteSpace(qdrantRag.collectionName);
            lastValidationStatus =
                hasUrl && hasCollection
                    ? $"Qdrant settings look valid: {qdrantRag.qdrantUrl} collection={qdrantRag.collectionName}"
                    : "Qdrant settings are incomplete. Check URL and collection name.";
        }

        bool HasLocalAiHost() => !string.IsNullOrWhiteSpace(localAiHost);

        bool HasValidLocalAiPort() => localAiPort > 0 && localAiPort <= 65535;

        bool HasLocalAiModel() => !string.IsNullOrWhiteSpace(localAiModel);
    }
}
#endif // !UNITY_SERVER
