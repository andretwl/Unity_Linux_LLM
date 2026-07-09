using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

namespace NPCSystem
{
    public class QdrantRAGService : MonoBehaviour
    {
        [FoldoutGroup("Qdrant Endpoint", true, nameof(_qdrantUrl), nameof(_collectionName))]
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
        string _collectionName = "npc_knowledge";

        [FoldoutGroup("Embedder", true, nameof(_embedder))]
        [SerializeField]
        private EditorAttributes.Void embedderGroup;

        [HelpBox(
            "Assign the NPCLocalAIEmbedder used to encode queries before searching Qdrant.",
            MessageMode.Log
        )]
        [SerializeField, HideProperty, FormerlySerializedAs("embedder")]
        NPCLocalAIEmbedder _embedder;

        [SerializeField, ReadOnly]
        string inspectorStatus = "Not validated yet.";

        [ShowInInspector]
        string SearchEndpointPreview => BuildSearchEndpoint();

        // ─── Public accessors ───
        public string QdrantUrl { get => _qdrantUrl; set => _qdrantUrl = value; }
        public string CollectionName { get => _collectionName; set => _collectionName = value; }
        public NPCLocalAIEmbedder Embedder { get => _embedder; set => _embedder = value; }

        [Button("Validate Qdrant Inspector Settings")]
        void ValidateInspectorSettings()
        {
            inspectorStatus =
                HasValidQdrantUrl() && HasValidCollectionName()
                    ? $"Qdrant settings look valid: {BuildSearchEndpoint()}"
                    : "Qdrant settings are invalid. Check URL and collection name.";
        }

        [Button("Log Qdrant Configuration")]
        void LogQdrantConfiguration()
        {
            NPCFlowLogger
                .FindOrCreate()
                .Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "Qdrant configuration logged.",
                    data: new Dictionary<string, object>
                    {
                        ["qdrantUrl"] = _qdrantUrl,
                        ["collectionName"] = _collectionName,
                        ["searchEndpoint"] = BuildSearchEndpoint(),
                    },
                    source: nameof(QdrantRAGService)
                );
            inspectorStatus = $"Logged Qdrant configuration: {BuildSearchEndpoint()}";
        }

        [Button("Test Qdrant Connection")]
        async void TestQdrantConnection()
        {
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();

            if (!HasValidQdrantUrl() || !HasValidCollectionName())
            {
                inspectorStatus = "Invalid Qdrant URL or collection name.";
                logger.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "Qdrant connection test skipped. Check URL and collection name.",
                    data: new Dictionary<string, object>
                    {
                        ["endpoint"] = _qdrantUrl,
                        ["collection"] = _collectionName,
                    },
                    source: nameof(QdrantRAGService)
                );
                return;
            }

            string endpoint = BuildSearchEndpoint();
            int pointCount = await CountPoints(endpoint);

            inspectorStatus =
                pointCount > 0
                    ? $"Qdrant is reachable. {pointCount} point(s) found in '{_collectionName}'."
                    : "Qdrant is reachable but empty (no points).";

            logger.Log(
                NPCFlowStage.ConfigurationValidation,
                pointCount > 0 ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                pointCount > 0 ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                inspectorStatus,
                source: nameof(QdrantRAGService)
            );
        }

        public string BuildSearchEndpoint() =>
            $"{_qdrantUrl}/collections/{_collectionName}/points/search";

        async Task<int> CountPoints(string endpoint)
        {
            string scrollEndpoint = endpoint.Replace("/points/search", "/points/scroll");

            var scrollPayload = new Dictionary<string, object>
            {
                ["limit"] = 1,
                ["with_payload"] = false,
                ["with_vector"] = false,
            };
            string scrollJson = JsonUtility.ToJson(
                new QdrantPayload { payload = scrollPayload }
            );

            using var request = new UnityWebRequest(scrollEndpoint, "POST");
            byte[] bytes = Encoding.UTF8.GetBytes(scrollJson);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[QdrantRAGService] Count failed: {request.error}\n{request.downloadHandler.text}"
                );
                return -1;
            }

            try
            {
                var scrollResult = JsonUtility.FromJson<QdrantScrollResult>(
                    $"{{\"result\":{request.downloadHandler.text}}}"
                );
                return scrollResult?.result?.points?.Length ?? 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[QdrantRAGService] Count parse error: {ex.Message}\n{request.downloadHandler.text}"
                );
                return -1;
            }
        }

        public async Task<List<string>> SearchAsync(string query, int limit = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            string endpoint = BuildSearchEndpoint();

            var searchPayload = new Dictionary<string, object>
            {
                ["limit"] = limit,
                ["with_payload"] = true,
                ["with_vector"] = false,
            };

            if (_embedder == null)
            {
                _embedder = FindAnyObjectByType<NPCLocalAIEmbedder>(FindObjectsInactive.Include);
                if (_embedder == null)
                {
                    Debug.LogError(
                        "[QdrantRAGService] No NPCLocalAIEmbedder found for query encoding."
                    );
                    return new List<string>();
                }
            }

            try
            {
                List<float> queryVector = await _embedder.Embeddings(query);
                searchPayload["vector"] = queryVector;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[QdrantRAGService] Embedding failed: {ex.Message}"
                );
                return new List<string>();
            }

            string searchJson = JsonUtility.ToJson(
                new QdrantPayload { payload = searchPayload }
            );

            using var request = new UnityWebRequest(endpoint, "POST");
            byte[] bytes = Encoding.UTF8.GetBytes(searchJson);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[QdrantRAGService] Search failed: {request.error}\n{request.downloadHandler.text}"
                );
                return new List<string>();
            }

            try
            {
                var searchResult = JsonUtility.FromJson<QdrantSearchResult>(
                    $"{{\"result\":{request.downloadHandler.text}}}"
                );
                return ExtractPayloadTexts(searchResult);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[QdrantRAGService] Search parse error: {ex.Message}\n{request.downloadHandler.text}"
                );
                return new List<string>();
            }
        }

        /// <summary>
        /// Search Qdrant and return results as a single concatenated string (legacy API).
        /// </summary>
        public async Task<string> SearchMemoryAsync(string query, int limit, string requestId, string npcSlug)
        {
            List<string> results = await SearchAsync(query, limit);
            return results.Count > 0 ? string.Join("\n", results) : string.Empty;
        }

        static List<string> ExtractPayloadTexts(QdrantSearchResult result)
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

            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (
                    request.result == UnityWebRequest.Result.ConnectionError
                    || request.result == UnityWebRequest.Result.ProtocolError
                )
                {
                    Debug.LogError($"[QdrantRAGService] Request failed: {request.error}");
                    return null;
                }

                return request.downloadHandler.text;
            }
        }

        public bool HasValidQdrantUrl()
        {
            return !string.IsNullOrWhiteSpace(_qdrantUrl)
                && (_qdrantUrl.StartsWith("http://") || _qdrantUrl.StartsWith("https://"));
        }

        public bool HasValidCollectionName()
        {
            return !string.IsNullOrWhiteSpace(_collectionName) && !_collectionName.Contains(" ");
        }

        [Serializable]
        class QdrantPayload
        {
            public Dictionary<string, object> payload;
        }

        [Serializable]
        class QdrantScrollResult
        {
            public QdrantScrollData result;
        }

        [Serializable]
        class QdrantScrollData
        {
            public QdrantScrollPoint[] points;
        }

        [Serializable]
        class QdrantScrollPoint
        {
            public string id;
        }

        [Serializable]
        class QdrantSearchResult
        {
            public QdrantSearchPoint[] result;
        }

        [Serializable]
        class QdrantSearchPoint
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
