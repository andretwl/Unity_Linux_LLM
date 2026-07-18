using System;
using System.Linq;
using EditorAttributes;
using UnityEngine.Serialization;
using UnityEngine;

namespace NPCSystem
{
    [CreateAssetMenu(fileName = "NPCProfile", menuName = "NPC Dialogue/NPC Profile")]
    public class NPCProfile : ScriptableObject
    {
        [Title("Identity")]
        [HelpBox(
            "NPC profile assets drive prompt construction, local knowledge paths, history persistence, and optional LoRA selection.",
            MessageMode.Log,
            drawAbove: true
        )]
        [OnValueChanged(nameof(RefreshInspectorPreview))]
        [Header("Identity")]
        [FormerlySerializedAs("npcSlug")]
        [SerializeField]
        string _npcSlug = "npc";
        public string NpcSlug { get => _npcSlug; set => _npcSlug = value; }

        [FormerlySerializedAs("displayName")]
        [SerializeField]
        string _displayName = "NPC";
        public string DisplayName { get => _displayName; set => _displayName = value; }

        [FormerlySerializedAs("portraitTexture")]
        [SerializeField]
        Texture2D _portraitTexture;
        public Texture2D PortraitTexture { get => _portraitTexture; set => _portraitTexture = value; }

        [Title("Personality")]
        [Header("Personality")]
        [FormerlySerializedAs("systemPrompt")]
        [SerializeField]
        [TextArea(4, 12)]
        string _systemPrompt = "You are a helpful in-game NPC.";
        public string SystemPrompt { get => _systemPrompt; set => _systemPrompt = value; }

        [Header("Behavior")]
        [FormerlySerializedAs("personalityBrief")]
        [SerializeField]
        [TextArea(2, 5)]
        string _personalityBrief = "";
        public string PersonalityBrief { get => _personalityBrief; set => _personalityBrief = value; }

        [FormerlySerializedAs("speakingStyle")]
        [SerializeField]
        [TextArea(2, 5)]
        string _speakingStyle = "";
        public string SpeakingStyle { get => _speakingStyle; set => _speakingStyle = value; }

        [FormerlySerializedAs("boundaries")]
        [SerializeField]
        [TextArea(2, 5)]
        string _boundaries = "";
        public string Boundaries { get => _boundaries; set => _boundaries = value; }

        [FormerlySerializedAs("secretKnowledge")]
        [SerializeField]
        [TextArea(2, 5)]
        string _secretKnowledge = "";
        public string SecretKnowledge { get => _secretKnowledge; set => _secretKnowledge = value; }

        [FormerlySerializedAs("suspicion")]
        [SerializeField]
        [Range(0f, 1f)]
        float _suspicion = 0.3f;
        public float Suspicion { get => _suspicion; set => _suspicion = value; }

        [FormerlySerializedAs("helpfulness")]
        [SerializeField]
        [Range(0f, 1f)]
        float _helpfulness = 0.7f;
        public float Helpfulness { get => _helpfulness; set => _helpfulness = value; }

        [FormerlySerializedAs("sarcasm")]
        [SerializeField]
        [Range(0f, 1f)]
        float _sarcasm = 0.2f;
        public float Sarcasm { get => _sarcasm; set => _sarcasm = value; }

        [Header("Gameplay Actions")]
        [FormerlySerializedAs("canGivePuzzleHints")]
        [SerializeField]
        bool _canGivePuzzleHints = true;
        public bool CanGivePuzzleHints { get => _canGivePuzzleHints; set => _canGivePuzzleHints = value; }

        [FormerlySerializedAs("canAccuseSuspects")]
        [SerializeField]
        bool _canAccuseSuspects = false;
        public bool CanAccuseSuspects { get => _canAccuseSuspects; set => _canAccuseSuspects = value; }

        [FormerlySerializedAs("canRevealSecrets")]
        [SerializeField]
        bool _canRevealSecrets = false;
        public bool CanRevealSecrets { get => _canRevealSecrets; set => _canRevealSecrets = value; }

        [FormerlySerializedAs("preferredActionFunctions")]
        [SerializeField]
        string[] _preferredActionFunctions = new string[0];
        public string[] PreferredActionFunctions { get => _preferredActionFunctions; set => _preferredActionFunctions = value; }

        [FormerlySerializedAs("forbiddenActionFunctions")]
        [SerializeField]
        string[] _forbiddenActionFunctions = new string[0];
        public string[] ForbiddenActionFunctions { get => _forbiddenActionFunctions; set => _forbiddenActionFunctions = value; }

        [Title("Sampling")]
        [Header("Sampling")]
        [FormerlySerializedAs("temperature")]
        [SerializeField]
        [Range(0f, 2f)]
        float _temperature = 0.7f;
        public float Temperature { get => _temperature; set => _temperature = value; }

        [FormerlySerializedAs("topP")]
        [SerializeField]
        [Range(0f, 1f)]
        float _topP = 0.9f;
        public float TopP { get => _topP; set => _topP = value; }

        [FormerlySerializedAs("minP")]
        [SerializeField]
        [Range(0f, 1f)]
        float _minP = 0.05f;
        public float MinP { get => _minP; set => _minP = value; }

        [FormerlySerializedAs("topK")]
        [SerializeField]
        [Range(0, 100)]
        int _topK = 40;
        public int TopK { get => _topK; set => _topK = value; }

        [FormerlySerializedAs("repeatPenalty")]
        [SerializeField]
        [Range(0f, 2f)]
        float _repeatPenalty = 1.1f;
        public float RepeatPenalty { get => _repeatPenalty; set => _repeatPenalty = value; }

        [FormerlySerializedAs("maxTokens")]
        [SerializeField]
        [Suffix("tokens")]
        int _maxTokens = 150;
        public int MaxTokens { get => _maxTokens; set => _maxTokens = value; }

        [Title("Knowledge")]
        [Header("Knowledge")]
        [FormerlySerializedAs("knowledgeSource")]
        [SerializeField]
        [Tooltip("'local' = file-based .rag index (offline fallback); 'qdrant' = Qdrant vector DB (network, preferred)")]
        KnowledgeSource _knowledgeSource = KnowledgeSource.Qdrant;
        public KnowledgeSource KnowledgeSource { get => _knowledgeSource; set => _knowledgeSource = value; }

        [FormerlySerializedAs("ragCategory")]
        [SerializeField]
        string _ragCategory = "";
        public string RagCategory { get => _ragCategory; set => _ragCategory = value; }

        [FormerlySerializedAs("ragResults")]
        [SerializeField]
        int _ragResults = 3;
        public int RagResults { get => _ragResults; set => _ragResults = value; }

        [FormerlySerializedAs("knowledgeSourcePath")]
        [SerializeField]
        [Tooltip("Path relative to StreamingAssets, e.g. NPCs/butler/knowledge.md")]
        [FilePath(true, "md")]
        [OnValueChanged(nameof(NormalizeProfilePaths))]
        string _knowledgeSourcePath = "";
        public string KnowledgeSourcePath { get => _knowledgeSourcePath; set => _knowledgeSourcePath = value; }

        [Title("LoRA")]
        [Header("LoRA")]
        [FormerlySerializedAs("loraAdapterPath")]
        [SerializeField]
        [Tooltip("Path relative to StreamingAssets, e.g. NPCs/butler/adapter.gguf")]
        [FilePath(true, "gguf")]
        [OnValueChanged(nameof(NormalizeProfilePaths))]
        string _loraAdapterPath = "";
        public string LoraAdapterPath { get => _loraAdapterPath; set => _loraAdapterPath = value; }

        [FormerlySerializedAs("loraWeight")]
        [SerializeField]
        [Range(0f, 1f)]
        float _loraWeight = 0.8f;
        public float LoraWeight { get => _loraWeight; set => _loraWeight = value; }

        [Title("History")]
        [Header("History")]
        [FormerlySerializedAs("historySaveFile")]
        [SerializeField]
        [Tooltip("Path relative to Application.persistentDataPath, e.g. NPCDialogue/butler.json")]
        [OnValueChanged(nameof(NormalizeProfilePaths))]
        string _historySaveFile = "";
        public string HistorySaveFile { get => _historySaveFile; set => _historySaveFile = value; }

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
            _knowledgeSourcePath = NormalizeRelativePath(_knowledgeSourcePath);
            _loraAdapterPath = NormalizeRelativePath(_loraAdapterPath);
            _historySaveFile = NormalizeRelativePath(_historySaveFile);
            RefreshInspectorPreview();
        }

        [Button("Validate Profile Configuration")]
        void RefreshInspectorPreview()
        {
            inspectorPreview =
                HasValidNpcSlug()
                && HasDisplayName()
                && HasSystemPrompt()
                && HasValidMaxTokens()
                && HasValidRagResults()
                    ? $"Profile '{GetDisplayName()}' resolves to slug '{GetNpcSlug()}' with knowledge '{GetKnowledgeSourcePath()}'."
                    : "Profile has invalid required values. Check slug, display name, prompt, max tokens, and RAG results.";
        }

        public string GetNpcSlug()
        {
            if (!string.IsNullOrWhiteSpace(_npcSlug))
                return _npcSlug.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(_displayName))
                return _displayName.Trim().ToLowerInvariant().Replace(" ", "-");
            return name.Trim().ToLowerInvariant().Replace(" ", "-");
        }

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(_displayName))
                return _displayName.Trim();
            return string.IsNullOrWhiteSpace(name) ? "NPC" : name.Trim();
        }

        public string GetRagCategory()
        {
            return string.IsNullOrWhiteSpace(_ragCategory) ? GetNpcSlug() : _ragCategory.Trim();
        }

        public string GetKnowledgeSourcePath()
        {
            return string.IsNullOrWhiteSpace(_knowledgeSourcePath)
                ? $"NPCs/{GetNpcSlug()}/knowledge.md"
                : _knowledgeSourcePath.Trim().Replace('\\', '/');
        }

        public string GetLoraAdapterPath()
        {
            return string.IsNullOrWhiteSpace(_loraAdapterPath)
                ? string.Empty
                : _loraAdapterPath.Trim().Replace('\\', '/');
        }

        public string GetHistorySaveFile()
        {
            return string.IsNullOrWhiteSpace(_historySaveFile)
                ? $"NPCDialogue/{GetNpcSlug()}.json"
                : _historySaveFile.Trim().Replace('\\', '/');
        }

        bool HasValidNpcSlug() => !string.IsNullOrWhiteSpace(GetNpcSlug());

        bool HasDisplayName() => !string.IsNullOrWhiteSpace(GetDisplayName());

        bool HasSystemPrompt() => !string.IsNullOrWhiteSpace(_systemPrompt);

        bool HasValidMaxTokens() => _maxTokens > 0;

        bool HasValidRagResults() => _ragResults > 0;

        /// <summary>True when this profile uses file-based local RAG (offline fallback).</summary>
        public bool UseLocalRag => _knowledgeSource == KnowledgeSource.Local;

        /// <summary>True when this profile uses Qdrant vector DB (network, preferred).</summary>
        public bool UseQdrantRag => _knowledgeSource == KnowledgeSource.Qdrant;

        static string NormalizeRelativePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        /// <summary>
        /// Find an NPCProfile in a profiles array by slug, display name, or asset name.
        /// Returns null when no match is found.
        /// </summary>
        public static NPCProfile FindProfileInArray(string npcName, NPCProfile[] profiles)
        {
            if (string.IsNullOrWhiteSpace(npcName) || profiles == null)
                return null;

            string key = npcName.Trim();

            // Try slug match first
            foreach (NPCProfile profile in profiles)
            {
                if (profile == null)
                    continue;

                if (
                    string.Equals(
                        profile.GetNpcSlug(),
                        key,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return profile;
            }

            // Fall back to display name / asset name
            return profiles.FirstOrDefault(
                profile =>
                    profile != null
                    && (
                        string.Equals(
                            profile.GetDisplayName(),
                            key,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            profile.name,
                            key,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
            );
        }
    }

    /// <summary>
    /// Selects the RAG backend used for NPC knowledge retrieval.
    /// <c>Local</c> uses the file-based .rag index (NPCLocalRAG + NPCSimpleSearch,
    /// suitable as offline fallback). <c>Qdrant</c> uses the Qdrant vector DB
    /// (network service, preferred for multi-NPC dialogue with metadata filtering).
    /// </summary>
    public enum KnowledgeSource
    {
        Local,
        Qdrant
    }
}
