using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Owns all RAG and retrieval logic — Qdrant search, local .rag index
    /// load/rebuild, embedder readiness polling, and index save.
    ///
    /// Created / initialized by NPCDialogueManager (Phase 2 extraction).
    /// </summary>
    [DefaultExecutionOrder(-1300)]
    public class NPCDialogueRetrievalService : MonoBehaviour
    {
        NPCLocalRAG _localRag;
        string _ragEmbeddingPath;
        bool _enableRAG;
        bool _useQdrantRag;
        QdrantRAGService _qdrantRag;
        bool _rebuildRagFromKnowledgeIfMissing;
        string _remoteEmbeddingHost;
        int _remoteEmbeddingPort;

        bool _ragReady;
        bool _ragUnavailable;

        static NPCFlowLogger Logger => NPCFlowLogger.FindOrCreate();

        public bool IsRagAvailable => _enableRAG && _localRag != null && !_ragUnavailable;

        // ── Initialisation ───────────────────────────────────────────────

        /// <summary>
        /// Set all dependencies. Call once during NPCDialogueManager
        /// initialisation before any other method.
        /// </summary>
        public void Initialize(
            NPCLocalRAG localRag,
            string ragEmbeddingPath,
            bool enableRAG,
            bool useQdrantRag,
            QdrantRAGService qdrantRag,
            bool rebuildRagFromKnowledgeIfMissing,
            string remoteEmbeddingHost,
            int remoteEmbeddingPort
        )
        {
            _localRag = localRag;
            _ragEmbeddingPath = ragEmbeddingPath;
            _enableRAG = enableRAG;
            _useQdrantRag = useQdrantRag;
            _qdrantRag = qdrantRag;
            _rebuildRagFromKnowledgeIfMissing = rebuildRagFromKnowledgeIfMissing;
            _remoteEmbeddingHost = remoteEmbeddingHost;
            _remoteEmbeddingPort = remoteEmbeddingPort;
        }

        /// <summary>
        /// Sync the embedder host/port on the local RAG's LLM embedder.
        /// Called from the manager's AutoAssignReferencesIfNeeded and also
        /// from EnsureReadyAsync as a safety net.
        /// </summary>
        public void SyncEmbedderHost()
        {
            if (_localRag != null)
            {
                if (_localRag.SearchMethod == null)
                    _localRag.UpdateGameObjects();
                if (_localRag.SearchMethod != null && _localRag.SearchMethod.LlmEmbedder != null)
                {
                    _localRag.SearchMethod.LlmEmbedder.host = _remoteEmbeddingHost;
                    _localRag.SearchMethod.LlmEmbedder.port = _remoteEmbeddingPort;
                }
            }

            if (_useQdrantRag && _qdrantRag != null && _qdrantRag.Embedder != null)
            {
                _qdrantRag.Embedder.host = _remoteEmbeddingHost;
                _qdrantRag.Embedder.port = _remoteEmbeddingPort;
            }
        }

        // ── Search ───────────────────────────────────────────────────────

        /// <summary>
        /// Search for relevant knowledge across Qdrant (if enabled) and local
        /// RAG (if enabled). Returns the concatenated knowledge string, or
        /// <see cref="string.Empty"/> when nothing is found.
        /// </summary>
        public async Task<string> SearchAsync(
            NPCProfile profile,
            string playerMessage,
            string reqId = null
        )
        {
            string ragKnowledge = string.Empty;

            // 1. Qdrant search
            if (_useQdrantRag && _qdrantRag != null)
            {
                try
                {
                    string qdrantResult = await _qdrantRag.SearchMemoryAsync(
                        playerMessage,
                        Mathf.Max(1, profile.ragResults),
                        reqId,
                        profile.GetNpcSlug()
                    );

                    if (!string.IsNullOrWhiteSpace(qdrantResult))
                        ragKnowledge = qdrantResult;
                }
                catch (Exception e)
                {
                    Logger.Log(
                        NPCFlowStage.ContextRetrieval,
                        NPCFlowStatus.Fallback,
                        NPCFlowLogLevel.Warning,
                        $"Qdrant search failed: {e.Message}",
                        source: nameof(NPCDialogueRetrievalService),
                        requestId: reqId,
                        npcSlug: profile?.GetNpcSlug(),
                        data: new Dictionary<string, object>
                        {
                            ["exceptionType"] = e.GetType().Name,
                            ["exceptionMessage"] = e.Message,
                            ["source"] = "Qdrant",
                        }
                    );
                }
            }

            // 2. Local RAG search
            if (
                string.IsNullOrWhiteSpace(ragKnowledge)
                && _enableRAG
                && !_ragUnavailable
                && _localRag != null
                && profile != null
                && !string.IsNullOrWhiteSpace(profile.GetRagCategory())
            )
            {
                try
                {
                    await EnsureReadyAsync();

                    (string[] similarResults, _) = await _localRag.Search(
                        playerMessage,
                        Mathf.Max(1, profile.ragResults),
                        profile.GetRagCategory()
                    );

                    if (similarResults != null && similarResults.Length > 0)
                    {
                        ragKnowledge = string.Join(
                            Environment.NewLine,
                            similarResults.Select(result => $"- {result}")
                        );
                    }
                }
                catch (Exception e)
                {
                    Logger.Log(
                        NPCFlowStage.ContextRetrieval,
                        NPCFlowStatus.Fallback,
                        NPCFlowLogLevel.Warning,
                        $"RAG search failed: {e.Message}",
                        source: nameof(NPCDialogueRetrievalService),
                        requestId: reqId,
                        npcSlug: profile?.GetNpcSlug(),
                        data: new Dictionary<string, object>
                        {
                            ["exceptionType"] = e.GetType().Name,
                            ["exceptionMessage"] = e.Message,
                            ["source"] = "LocalRAG",
                        }
                    );
                    _ragUnavailable = true;
                }
            }

            return ragKnowledge ?? string.Empty;
        }

        // ── Index lifecycle ──────────────────────────────────────────────

        /// <summary>
        /// Load the local .rag index from disk, or rebuild it from NPC knowledge
        /// files when the metadata indicates staleness.
        /// </summary>
        public async Task LoadOrBuildIndexAsync(NPCProfile[] profiles)
        {
            if (_localRag == null || string.IsNullOrWhiteSpace(_ragEmbeddingPath))
            {
                Logger.Log(
                    NPCFlowStage.LocalRagReady,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Debug,
                    "RAG is disabled. Skipping RAG initialisation.",
                    source: nameof(NPCDialogueRetrievalService)
                );
                return;
            }

            if (_useQdrantRag)
            {
                Logger.Log(
                    NPCFlowStage.LocalRagReady,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Debug,
                    "Qdrant RAG is enabled. Skipping local RAG index load/rebuild.",
                    source: nameof(NPCDialogueRetrievalService)
                );
                return;
            }

            try
            {
                await EnsureReadyAsync();

                if (IsMetadataCurrent(out string metadataReason, profiles))
                {
                    bool loaded = await _localRag.LoadFile(_ragEmbeddingPath);
                    if (loaded)
                    {
                        Logger.Log(
                            NPCFlowStage.LocalRagReady,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            $"RAG embeddings loaded from {_ragEmbeddingPath}",
                            source: nameof(NPCDialogueRetrievalService),
                            data: new Dictionary<string, object>
                            {
                                ["ragEmbeddingPath"] = _ragEmbeddingPath,
                            }
                        );
                        return;
                    }

                    Logger.Log(
                        NPCFlowStage.LocalRagReady,
                        NPCFlowStatus.Warning,
                        NPCFlowLogLevel.Warning,
                        $"RAG metadata is current but index load failed. Rebuilding {_ragEmbeddingPath}.",
                        source: nameof(NPCDialogueRetrievalService),
                        data: new Dictionary<string, object>
                        {
                            ["ragEmbeddingPath"] = _ragEmbeddingPath,
                        }
                    );
                }
                else
                {
                    Logger.Log(
                        NPCFlowStage.LocalRagReady,
                        NPCFlowStatus.Start,
                        NPCFlowLogLevel.Debug,
                        $"RAG index rebuild required: {metadataReason}",
                        source: nameof(NPCDialogueRetrievalService)
                    );
                }

                if (!_rebuildRagFromKnowledgeIfMissing)
                    return;

                bool built = await NPCRAGImporter.RebuildAsync(
                    _localRag,
                    profiles,
                    _ragEmbeddingPath
                );

                if (built)
                {
                    Logger.Log(
                        NPCFlowStage.LocalRagReady,
                        NPCFlowStatus.Success,
                        NPCFlowLogLevel.Info,
                        $"RAG embeddings rebuilt and saved to {_ragEmbeddingPath}",
                        source: nameof(NPCDialogueRetrievalService),
                        data: new Dictionary<string, object>
                        {
                            ["ragEmbeddingPath"] = _ragEmbeddingPath,
                        }
                    );
                }
            }
            catch (Exception e)
            {
                Logger.Log(
                    NPCFlowStage.LocalRagReady,
                    NPCFlowStatus.Fallback,
                    NPCFlowLogLevel.Warning,
                    $"RAG initialization skipped: {e.Message}",
                    source: nameof(NPCDialogueRetrievalService),
                    data: new Dictionary<string, object>
                    {
                        ["exceptionType"] = e.GetType().Name,
                        ["exceptionMessage"] = e.Message,
                    }
                );
                _ragUnavailable = true;
            }
        }

        /// <summary>
        /// Add knowledge text to the local RAG index and save immediately.
        /// Note: this does NOT update the source metadata sidecar —
        /// prefer NPCRAGImporter.RebuildAsync for durable indexes.
        /// </summary>
        public async Task AddKnowledgeAsync(
            string npcName,
            string knowledgeText,
            NPCProfile[] profiles
        )
        {
            if (_localRag == null)
            {
                Logger.Log(
                    NPCFlowStage.LocalRagReady,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Warning,
                    "RAG not configured!",
                    source: nameof(NPCDialogueRetrievalService)
                );
                return;
            }

            NPCProfile profile = FindProfileInArray(npcName, profiles);
            if (profile == null)
                return;

            await EnsureReadyAsync();
            await _localRag.Add(knowledgeText, profile.GetRagCategory());
            _localRag.SaveFile(_ragEmbeddingPath);

            Logger.Log(
                NPCFlowStage.LocalRagReady,
                NPCFlowStatus.Warning,
                NPCFlowLogLevel.Warning,
                "AddKnowledgeAsync saved RAG without updating source metadata. Prefer rebuilding from source files for durable indexes.",
                source: nameof(NPCDialogueRetrievalService),
                npcSlug: profile.GetNpcSlug()
            );
        }

        /// <summary>
        /// Save the current local RAG index to disk.
        /// </summary>
        public void SaveIndex()
        {
            if (_localRag != null)
            {
                _localRag.SaveFile(_ragEmbeddingPath);
                Logger.Log(
                    NPCFlowStage.LocalRagReady,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    "SaveIndex saved RAG without updating source metadata. Prefer NPCRAGImporter.RebuildAsync for durable indexes.",
                    source: nameof(NPCDialogueRetrievalService)
                );
            }
        }

        // ── Internal helpers ─────────────────────────────────────────────

        async Task EnsureReadyAsync()
        {
            if (_localRag == null || _ragReady)
                return;
            if (_ragUnavailable)
                return;

            SyncEmbedderHost();

            if (_localRag.SearchMethod == null || _localRag.SearchMethod.LlmEmbedder == null)
            {
                await Task.Yield();
                return;
            }

            Exception lastException = null;

            for (int attempt = 0; attempt < 120; attempt++)
            {
                try
                {
                    List<float> embeddings = await _localRag.SearchMethod.LlmEmbedder.Embeddings("ready");

                    if (embeddings == null || embeddings.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "RAG embedder returned empty embeddings."
                        );
                    }

                    _ragReady = true;
                    return;
                }
                catch (Exception e)
                {
                    lastException = e;

                    if (!IsTransientStartupError(e))
                    {
                        _ragUnavailable = true;
                        Logger.Log(
                            NPCFlowStage.LocalRagReady,
                            NPCFlowStatus.Fallback,
                            NPCFlowLogLevel.Warning,
                            $"RAG embedder unreachable: {e.Message}. Continuing in prompt-only mode.",
                            source: nameof(NPCDialogueRetrievalService)
                        );
                        return;
                    }

                    await Task.Delay(100);
                }
            }

            _ragUnavailable = true;
            Logger.Log(
                NPCFlowStage.LocalRagReady,
                NPCFlowStatus.Fallback,
                NPCFlowLogLevel.Warning,
                $"RAG embedder was not ready after startup wait: {lastException?.Message}. Continuing in prompt-only mode.",
                source: nameof(NPCDialogueRetrievalService)
            );
        }

        bool IsMetadataCurrent(out string reason, NPCProfile[] profiles)
        {
            reason = string.Empty;

            NPCRAGMetadata expected = NPCRAGMetadataStore.CreateExpected(
                _ragEmbeddingPath,
                profiles ?? Array.Empty<NPCProfile>(),
                NPCRAGImporter.MaxChunkCharacters
            );

            if (!NPCRAGMetadataStore.TryLoad(_ragEmbeddingPath, out NPCRAGMetadata actual))
            {
                reason = "metadata sidecar is missing or unreadable";
                return false;
            }

            return NPCRAGMetadataStore.IsCurrent(actual, expected, out reason);
        }

        static bool IsTransientStartupError(Exception exception)
        {
            string message = exception.Message;
            return message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static NPCProfile FindProfileInArray(string npcName, NPCProfile[] profiles)
        {
            return NPCProfile.FindProfileInArray(npcName, profiles);
        }
    }
}
