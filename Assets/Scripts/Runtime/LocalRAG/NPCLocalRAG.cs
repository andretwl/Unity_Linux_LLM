using System;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
{
    [Serializable]
    public class NPCLocalRAG : NPCSearchable
    {
        [Tooltip("Search method GameObject (NPCSimpleSearch).")]
        [FormerlySerializedAs("search")]
        [SerializeField]
        NPCSearchMethod _search;

        [Tooltip("Chunking method GameObject (optional).")]
        [FormerlySerializedAs("chunking")]
        [SerializeField]
        NPCChunking _chunking;

        // ─── Public accessors ───
        public NPCSearchMethod SearchMethod => _search;
        public NPCChunking Chunking => _chunking;

        public void Init()
        {
            UpdateGameObjects();
        }

        public void ReturnChunks(bool returnChunks)
        {
            if (_chunking != null)
                _chunking.ReturnChunks(returnChunks);
        }

        protected void ConstructSearch()
        {
            _search = ConstructComponent<NPCSearchMethod>(
                typeof(NPCSimpleSearch),
                (
                    previous,
                    current
                ) => { /* embedder assignment handled by UpdateGameObjects */ }
            );
        }

        protected void ConstructChunking()
        {
            // Chunking is optional and currently unused by the dialogue pipeline.
            // Kept for backward compatibility with local RAG scenes that reference it.
        }

        public override void UpdateGameObjects()
        {
            if (this == null)
                return;
            ConstructSearch();
            ConstructChunking();
        }

        protected NPCSearchable GetSearcher()
        {
            if (_chunking != null)
                return _chunking;
            if (_search != null)
                return _search;
            NPCFlowLogger.FindOrCreate().Log(
                NPCFlowStage.LocalRagReady,
                NPCFlowStatus.Error,
                NPCFlowLogLevel.Error,
                "[NPC] Local RAG search GameObject is null",
                source: nameof(NPCLocalRAG)
            );
            return null;
        }

#if UNITY_EDITOR
        private void OnValidateUpdate()
        {
            UnityEditor.EditorApplication.delayCall -= OnValidateUpdate;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    UpdateGameObjects();
            };
        }
#endif

        public override string Get(int key) => GetSearcher()?.Get(key);

        /// <summary>
        /// Add a string to the local RAG index.
        /// DEPRECATED: Use QdrantRAGService or Cognee for dynamic knowledge updates.
        /// Local RAG is intended as a read-only fallback for static NPC knowledge.
        /// </summary>
        public override async Task<int> Add(string inputString, string group = "")
        {
            NPCFlowLogger.FindOrCreate().Log(
                NPCFlowStage.LocalRagReady,
                NPCFlowStatus.Warning,
                NPCFlowLogLevel.Warning,
                $"NPCLocalRAG.Add() called — prefer Qdrant or Cognee for dynamic knowledge updates. Group: {group}",
                source: nameof(NPCLocalRAG),
                data: new System.Collections.Generic.Dictionary<string, object>
                {
                    ["group"] = group ?? "",
                    ["textLength"] = inputString?.Length ?? 0,
                }
            );

            var searcher = GetSearcher();
            if (searcher == null)
                return -1;
            return await searcher.Add(inputString, group);
        }

        /// <summary>
        /// Remove a string from the local RAG index.
        /// DEPRECATED: Use QdrantRAGService or Cognee for dynamic knowledge updates.
        /// </summary>
        public override int Remove(string inputString, string group = "")
        {
            NPCFlowLogger.FindOrCreate().Log(
                NPCFlowStage.LocalRagReady,
                NPCFlowStatus.Warning,
                NPCFlowLogLevel.Warning,
                $"NPCLocalRAG.Remove() called — prefer Qdrant or Cognee for dynamic knowledge updates.",
                source: nameof(NPCLocalRAG)
            );
            return GetSearcher()?.Remove(inputString, group) ?? 0;
        }

        public override void Remove(int key) => GetSearcher()?.Remove(key);

        public override int Count() => GetSearcher()?.Count() ?? 0;

        public override int Count(string group) => GetSearcher()?.Count(group) ?? 0;

        public override void Clear() => GetSearcher()?.Clear();

        public override async Task<int> IncrementalSearch(string queryString, string group = "")
        {
            var searcher = GetSearcher();
            if (searcher == null)
                return -1;
            return await searcher.IncrementalSearch(queryString, group);
        }

        public override (string[], float[], bool) IncrementalFetch(int fetchKey, int k) =>
            GetSearcher()?.IncrementalFetch(fetchKey, k) ?? (new string[0], new float[0], true);

        public override (int[], float[], bool) IncrementalFetchKeys(int fetchKey, int k) =>
            GetSearcher()?.IncrementalFetchKeys(fetchKey, k) ?? (new int[0], new float[0], true);

        public override void IncrementalSearchComplete(int fetchKey) =>
            GetSearcher()?.IncrementalSearchComplete(fetchKey);

        public override void Save(ZipArchive archive) => GetSearcher()?.Save(archive);

        public override void Load(ZipArchive archive) => GetSearcher()?.Load(archive);
    }
}
