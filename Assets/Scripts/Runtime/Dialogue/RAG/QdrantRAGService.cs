using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EditorAttributes;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;


using NPCSystem.Monitoring;
using NPCSystem.Monitoring.Datadog;
using NPCSystem.Dialogue.Core;
using NPCSystem.Network.Core;
using NPCSystem.Character.Player;
using NPCSystem.Auth;
using NPCSystem.Items;
using NPCSystem.LocalAI;
using NPCSystem.Initialization;
using NPCSystem.Character.NPC;
using NPCSystem.Dialogue.Session;
using NPCSystem.Dialogue.UI;
using NPCSystem.Dialogue.RAG;
using NPCSystem.Dialogue.Persistence;
namespace NPCSystem.Dialogue.RAG
{
    public class QdrantRAGService : MonoBehaviour
    {
        [FoldoutGroup("Qdrant Endpoint", true, nameof(_qdrantUrl), nameof(_collectionName), nameof(_denseVectorName), nameof(_sparseVectorName))]
        [SerializeField]
        private EditorAttributes.Void qdrantEndpointGroup;

        [HelpBox(
            "Qdrant is the primary vector database for NPC knowledge. Uses NPCLocalAIEmbedder for query encoding.",
            MessageMode.Log,
            drawAbove: true
        )]
        [SerializeField, HideProperty, FormerlySerializedAs("qdrantUrl")]
        string _qdrantUrl = "http://localhost:6333";

        [SerializeField, HideProperty, FormerlySerializedAs("collectionName")]
        string _collectionName = "unity_linux_llm_codebase_v2";

        [SerializeField, HideProperty]
        string _denseVectorName = "dense";

        [SerializeField, HideProperty, FormerlySerializedAs("sparseVectorName")]
        string _sparseVectorName = "code_keywords";

        [FoldoutGroup("Embedder", true, nameof(_embedder), nameof(_expectedDenseDimension))]
        [SerializeField]
        private EditorAttributes.Void embedderGroup;

        [HelpBox(
            "Assign the NPCLocalAIEmbedder used to encode queries before searching Qdrant.",
            MessageMode.Log
        )]
        [SerializeField, HideProperty, FormerlySerializedAs("embedder")]
        NPCLocalAIEmbedder _embedder;

        [SerializeField, HideProperty]
        int _expectedDenseDimension = 768;

        [SerializeField, ReadOnly]
        string inspectorStatus = "Not validated yet.";

        [ShowInInspector]
        string SearchEndpointPreview => BuildQueryEndpoint();

        // ─── Public accessors ───
        public string QdrantUrl
        {
            get => _qdrantUrl;
            set => _qdrantUrl = value;
        }
        public string CollectionName
        {
            get => _collectionName;
            set => _collectionName = value;
        }
        public NPCLocalAIEmbedder Embedder
        {
            get => _embedder;
            set => _embedder = value;
        }

        void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ResolveWebGlHost();
#endif
        }

        private void ResolveWebGlHost()
        {
            try
            {
                Uri pageUri = new Uri(Application.absoluteURL);
                if (!NPCNetworkUtils.IsLocalHost(pageUri.Host))
                {
                    Uri qdrantUri = new Uri(_qdrantUrl);
                    if (NPCNetworkUtils.IsLocalHost(qdrantUri.Host))
                    {
                        var builder = new UriBuilder(qdrantUri);
                        builder.Host = pageUri.Host;
                        _qdrantUrl = builder.ToString().TrimEnd('/');
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QdrantRAGService] Failed to resolve WebGL host: {ex.Message}");
            }
        }

        [Button("Validate Qdrant Inspector Settings")]
        async Task ValidateInspectorSettings()
        {
            try
            {
                if (!HasValidQdrantUrl() || !HasValidCollectionName())
                {
                    inspectorStatus = "Qdrant settings are invalid. Check URL and collection name.";
                    return;
                }

                inspectorStatus = "Validating...";
                string collectionInfo = await GetCollectionInfo();
                if (string.IsNullOrEmpty(collectionInfo))
                {
                    inspectorStatus = "Failed to fetch collection info. Is Qdrant reachable?";
                }
                else
                {
                    inspectorStatus = $"Validated: {collectionInfo}";
                }
            }
            catch (Exception ex)
            {
                inspectorStatus = $"Validation error: {ex.Message}";
                Debug.LogError($"[QdrantRAGService] Validation failed: {ex}");
            }
        }

        async Task<string> GetCollectionInfo()
        {
            string url = $"{_qdrantUrl}/collections/{_collectionName}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
                return null;

            try
            {
                var response = JsonConvert.DeserializeObject<QdrantCollectionInfoResponse>(request.downloadHandler.text);
                if (response?.result?.config?.params_config?.vectors?.dense == null)
                    return "Invalid response structure.";

                int denseSize = response.result.config.params_config.vectors.dense.size;
                bool hasSparse = response.result.config.params_config.sparse_vectors != null &&
                                 response.result.config.params_config.sparse_vectors.ContainsKey(_sparseVectorName);

                // ── Datadog: emit live dimension so dashboards catch mismatches instantly ──
                DatadogMetricsService.Gauge(
                    "rag.dimension.check",
                    denseSize,
                    tags: new[] { $"collection:{_collectionName}" }
                );

                // Expected dimension for nomic-embed-text-v1.5
                const int expectedDim = 768;
                bool dimOk = denseSize == expectedDim;
                DatadogMetricsService.Increment(
                    dimOk ? "rag.dimension.valid" : "rag.dimension.invalid",
                    tags: new[] { $"collection:{_collectionName}", $"dim:{denseSize}" }
                );

                return $"Dense({denseSize}){(hasSparse ? $" + Sparse({_sparseVectorName})" : " [NO SPARSE]")}";
            }
            catch (Exception ex)
            {
                return $"Parse error: {ex.Message}";
            }
        }

        [Serializable]
        class QdrantCollectionInfoResponse
        {
            public QdrantCollectionInfoResult result;
        }

        [Serializable]
        class QdrantCollectionInfoResult
        {
            public QdrantCollectionConfig config;
        }

        [Serializable]
        class QdrantCollectionConfig
        {
            public QdrantCollectionParams params_config;
        }

        [Serializable]
        class QdrantCollectionParams
        {
            public QdrantVectorsConfig vectors;
            public Dictionary<string, object> sparse_vectors;
        }

        [Serializable]
        class QdrantVectorsConfig
        {
            public QdrantVectorParams dense;
        }

        [Serializable]
        class QdrantVectorParams
        {
            public int size;
        }

        [Button("Test Qdrant Connection")]
        async Task TestQdrantConnection()
        {
            try
            {
                NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
                string endpoint = BuildQueryEndpoint();

                using var request = UnityWebRequest.Get($"{_qdrantUrl}/collections");
                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    inspectorStatus = $"Connection failed: {request.error}";
                    logger.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error, inspectorStatus);
                    return;
                }

                inspectorStatus = "Qdrant is reachable.";
                logger.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info, inspectorStatus);
            }
            catch (Exception ex)
            {
                inspectorStatus = $"Connection error: {ex.Message}";
                Debug.LogError($"[QdrantRAGService] Connection test failed: {ex}");
            }
        }

        public string BuildQueryEndpoint() =>
            $"{_qdrantUrl}/collections/{_collectionName}/points/query";

        public async Task<List<string>> SearchAsync(string query, int limit = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            using var searchSpan = DatadogTracer.StartSpan(
                "qdrant.search_hybrid",
                service: "unity-dedicated-server",
                resource: $"{_collectionName}.query",
                type: "vector_db",
                tags: new[]
                {
                    $"collection:{_collectionName}",
                    $"limit:{limit}",
                }
            );

            var searchSw = System.Diagnostics.Stopwatch.StartNew();

            if (_embedder == null)
            {
                _embedder = FindAnyObjectByType<NPCLocalAIEmbedder>(FindObjectsInactive.Include);
            }

            if (_embedder == null)
            {
                Debug.LogError("[QdrantRAGService] No NPCLocalAIEmbedder found.");
                return new List<string>();
            }

            try
            {
                // 1. Dense Embedding
                List<float> queryVector = await _embedder.Embeddings(query);
                if (queryVector.Count == 0)
                    return new List<string>();

                // 2. Dense Query
                var payload = new QueryPayload
                {
                    query = queryVector,
                    limit = limit,
                    with_payload = true
                };

                string json = JsonConvert.SerializeObject(payload);
                string responseText = await SendSearchRequestAsync(BuildQueryEndpoint(), json);
                
                if (string.IsNullOrWhiteSpace(responseText))
                    return new List<string>();

                var searchResult = JsonConvert.DeserializeObject<QdrantQueryResult>(responseText);
                List<string> results = ExtractPayloadTexts(searchResult);

                searchSw.Stop();
                DatadogMetricsService.Timer("qdrant.search.duration", searchSw.ElapsedMilliseconds, 1.0, new[] { "mode:hybrid" });

                return results;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QdrantRAGService] Search failed: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<string> SearchMemoryAsync(string query, int limit, string requestId, string npcSlug)
        {
            List<string> results = await SearchAsync(query, limit);
            return results.Count > 0 ? string.Join("\n", results) : string.Empty;
        }

        static List<string> ExtractPayloadTexts(QdrantQueryResult result)
        {
            var texts = new List<string>();
            if (result?.result == null)
                return texts;

            foreach (var point in result.result)
            {
                if (point?.payload != null && !string.IsNullOrWhiteSpace(point.payload.text))
                    texts.Add(point.payload.text);
            }

            return texts;
        }

        protected virtual async Task<string> SendSearchRequestAsync(string endpoint, string json)
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            using UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[QdrantRAGService] Query failed: {request.error}\n{request.downloadHandler.text}");
                return null;
            }

            return request.downloadHandler.text;
        }

        public bool HasValidQdrantUrl() => !string.IsNullOrWhiteSpace(_qdrantUrl) && (_qdrantUrl.StartsWith("http://") || _qdrantUrl.StartsWith("https://"));
        public bool HasValidCollectionName() => !string.IsNullOrWhiteSpace(_collectionName) && !_collectionName.Contains(" ");

        [Serializable]
        class QueryPayload
        {
            public List<float> query;
            public int limit;
            public bool with_payload;
        }

        [Serializable]
        class DenseQuery
        {
            public List<float> vector;
        }

        [Serializable]
        class QdrantQueryResult
        {
            public QdrantQueryPoint[] result;
        }

        [Serializable]
        class QdrantQueryPoint
        {
            public string id;
            public float score;
            public QdrantSearchPayload payload;
        }

        [Serializable]
        class QdrantSearchPayload
        {
            public string text;
        }
    }
}

