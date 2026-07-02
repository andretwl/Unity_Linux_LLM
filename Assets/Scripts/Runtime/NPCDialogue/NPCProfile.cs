using EditorAttributes;
using UnityEngine;

namespace NPCSystem
{
    [CreateAssetMenu(fileName = "NPCProfile", menuName = "NPC Dialogue/NPC Profile")]
    public class NPCProfile : ScriptableObject
    {
        [Title("Identity")]
        [HelpBox("NPC profile assets drive prompt construction, local knowledge paths, history persistence, and optional LoRA selection.", MessageMode.Log, drawAbove: true)]
        [OnValueChanged(nameof(RefreshInspectorPreview))]
        [Header("Identity")]
        public string npcSlug = "npc";

        public string displayName = "NPC";
        public Texture2D portraitTexture;

        [Title("Personality")]
        [Header("Personality")]
        [TextArea(4, 12)]
        public string systemPrompt = "You are a helpful in-game NPC.";

        [Header("Behavior")]
        [TextArea(2, 5)] public string personalityBrief = "";
        [TextArea(2, 5)] public string speakingStyle = "";
        [TextArea(2, 5)] public string boundaries = "";
        [TextArea(2, 5)] public string secretKnowledge = "";
        [Range(0f, 1f)] public float suspicion = 0.3f;
        [Range(0f, 1f)] public float helpfulness = 0.7f;
        [Range(0f, 1f)] public float sarcasm = 0.2f;

        [Header("Gameplay Actions")]
        public bool canGivePuzzleHints = true;
        public bool canAccuseSuspects = false;
        public bool canRevealSecrets = false;
        public string[] preferredActionFunctions = new string[0];
        public string[] forbiddenActionFunctions = new string[0];

        [Title("Sampling")]
        [Header("Sampling")]
        [Range(0f, 2f)] public float temperature = 0.7f;
        [Range(0f, 1f)] public float topP = 0.9f;
        [Range(0f, 1f)] public float minP = 0.05f;
        [Range(0, 100)] public int topK = 40;
        [Range(0f, 2f)] public float repeatPenalty = 1.1f;
        public int maxTokens = 150;

        [Title("Knowledge")]
        [Header("Knowledge")]
        public string ragCategory = "";
        public int ragResults = 3;
        [Tooltip("Path relative to StreamingAssets, e.g. NPCs/butler/knowledge.md")]
        [FilePath(true, "md")]
        [OnValueChanged(nameof(NormalizeProfilePaths))]
        public string knowledgeSourcePath = "";

        [Title("LoRA")]
        [Header("LoRA")]
        [Tooltip("Path relative to StreamingAssets, e.g. NPCs/butler/adapter.gguf")]
        [FilePath(true, "gguf")]
        [OnValueChanged(nameof(NormalizeProfilePaths))]
        public string loraAdapterPath = "";
        [Range(0f, 1f)] public float loraWeight = 0.8f;

        [Title("History")]
        [Header("History")]
        [Tooltip("Path relative to Application.persistentDataPath, e.g. NPCDialogue/butler.json")]
        [OnValueChanged(nameof(NormalizeProfilePaths))]
        public string historySaveFile = "";

        [SerializeField, ReadOnly]
        string inspectorPreview = "Not validated yet.";

        [ShowInInspector]
        string ResolvedSlugPreview => GetNpcSlug();

        [ShowInInspector]
        string ResolvedKnowledgePathPreview => GetKnowledgeSourcePath();

        [ShowInInspector]
        string ResolvedHistoryFilePreview => GetHistorySaveFile();

        [Button("Normalize Profile Paths")]
        void NormalizeProfilePaths()
        {
            knowledgeSourcePath = NormalizeRelativePath(knowledgeSourcePath);
            loraAdapterPath = NormalizeRelativePath(loraAdapterPath);
            historySaveFile = NormalizeRelativePath(historySaveFile);
            RefreshInspectorPreview();
        }

        [Button("Validate Profile Configuration")]
        void RefreshInspectorPreview()
        {
            inspectorPreview = HasValidNpcSlug() && HasDisplayName() && HasSystemPrompt() && HasValidMaxTokens() && HasValidRagResults()
                ? $"Profile '{GetDisplayName()}' resolves to slug '{GetNpcSlug()}' with knowledge '{GetKnowledgeSourcePath()}'."
                : "Profile has invalid required values. Check slug, display name, prompt, max tokens, and RAG results.";
        }

        public string GetNpcSlug()
        {
            if (!string.IsNullOrWhiteSpace(npcSlug)) return npcSlug.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim().ToLowerInvariant().Replace(" ", "-");
            return name.Trim().ToLowerInvariant().Replace(" ", "-");
        }

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
            return string.IsNullOrWhiteSpace(name) ? "NPC" : name.Trim();
        }

        public string GetRagCategory()
        {
            return string.IsNullOrWhiteSpace(ragCategory) ? GetNpcSlug() : ragCategory.Trim();
        }

        public string GetKnowledgeSourcePath()
        {
            return string.IsNullOrWhiteSpace(knowledgeSourcePath)
                ? $"NPCs/{GetNpcSlug()}/knowledge.md"
                : knowledgeSourcePath.Trim().Replace('\\', '/');
        }

        public string GetLoraAdapterPath()
        {
            return string.IsNullOrWhiteSpace(loraAdapterPath)
                ? string.Empty
                : loraAdapterPath.Trim().Replace('\\', '/');
        }

        public string GetHistorySaveFile()
        {
            return string.IsNullOrWhiteSpace(historySaveFile)
                ? $"NPCDialogue/{GetNpcSlug()}.json"
                : historySaveFile.Trim().Replace('\\', '/');
        }

        bool HasValidNpcSlug() => !string.IsNullOrWhiteSpace(GetNpcSlug());

        bool HasDisplayName() => !string.IsNullOrWhiteSpace(GetDisplayName());

        bool HasSystemPrompt() => !string.IsNullOrWhiteSpace(systemPrompt);

        bool HasValidMaxTokens() => maxTokens > 0;

        bool HasValidRagResults() => ragResults > 0;

        static string NormalizeRelativePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }
    }
}
