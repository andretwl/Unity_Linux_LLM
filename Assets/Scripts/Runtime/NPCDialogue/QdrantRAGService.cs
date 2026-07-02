using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Networking;
using LLMUnity;

namespace NPCSystem
{
    public class QdrantRAGService : MonoBehaviour
    {
        [Title("Qdrant Endpoint")]
        [HelpBox("Qdrant is optional in the shipped scene. Use these fields when NPCDialogueManager.useQdrantRag is enabled or when running codebase-retrieval samples.", MessageMode.Log, drawAbove: true)]
        public string qdrantUrl = "http://localhost:6333";

        public string collectionName = "npc_knowledge";

        [SerializeField, ReadOnly]
        string inspectorStatus = "Not validated yet.";

        [ShowInInspector]
        string SearchEndpointPreview => BuildSearchEndpoint();

        [Button("Validate Qdrant Inspector Settings")]
        void ValidateInspectorSettings()
        {
            inspectorStatus = HasValidQdrantUrl() && HasValidCollectionName()
                ? $"Qdrant settings look valid: {BuildSearchEndpoint()}"
                : "Qdrant settings are invalid. Check URL and collection name.";
        }

        [Button("Log Qdrant Configuration")]
        void LogQdrantConfiguration()
        {
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "Qdrant inspector configuration logged.",
                source: nameof(QdrantRAGService),
                data: new Dictionary<string, object>
                {
                    ["qdrantUrl"] = qdrantUrl,
                    ["collectionName"] = collectionName,
                    ["searchEndpoint"] = BuildSearchEndpoint()
                });
            inspectorStatus = $"Logged Qdrant configuration: {BuildSearchEndpoint()}";
        }
        
        void Awake()
        {
            NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Success, NPCFlowLogLevel.Debug,
                "QdrantRAGService initialized.",
                source: nameof(QdrantRAGService),
                data: new Dictionary<string, object>
                {
                    ["qdrantUrl"] = qdrantUrl,
                    ["collectionName"] = collectionName
                });
        }

        [System.Serializable]
        private class QdrantSearchRequest
        {
            public List<float> vector;
            public int limit = 3;
            public bool with_payload = true;
        }

        [System.Serializable]
        private class QdrantSearchResponse
        {
            public List<QdrantPoint> result;
        }

        [System.Serializable]
        private class QdrantPoint
        {
            public float score;
            public QdrantPayload payload;
        }

        [System.Serializable]
        private class QdrantPayload
        {
            public string text;
        }

        public async Task<string> SearchMemoryAsync(RAG rag, string query, int limit = 3, string requestId = null, string npcSlug = null)
        {
            using var scope = NPCFlowScope.Start(NPCFlowLogger.Instance, NPCFlowStage.QdrantSearch, source: nameof(QdrantRAGService), requestId: requestId, npcSlug: npcSlug, data: new Dictionary<string, object>
            {
                ["endpoint"] = qdrantUrl,
                ["collection"] = collectionName,
                ["limit"] = limit
            });
            if (rag == null || rag.search == null || rag.search.llmEmbedder == null)
            {
                NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.QdrantSearch, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "RAG or LLMEmbedder is null; Qdrant search skipped.",
                    source: nameof(QdrantRAGService), requestId: requestId, npcSlug: npcSlug);
                scope.Skipped("RAG embedder missing; Qdrant skipped.");
                return string.Empty;
            }

            try
            {
                // Embed the query using LLMUnity
                List<float> queryVector = await rag.search.llmEmbedder.Embeddings(query);
                if (queryVector == null || queryVector.Count == 0)
                {
                    NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.QdrantEmbedding, NPCFlowStatus.Fallback, NPCFlowLogLevel.Warning,
                        "Failed to generate embeddings for query.",
                        source: nameof(QdrantRAGService), requestId: requestId, npcSlug: npcSlug,
                        data: new Dictionary<string, object> { ["queryLength"] = query?.Length ?? 0 });
                    scope.Fallback("Embeddings generation failed.");
                    return string.Empty;
                }

                QdrantSearchRequest requestBody = new QdrantSearchRequest
                {
                    vector = queryVector,
                    limit = limit,
                    with_payload = true
                };

                string jsonRequest = JsonUtility.ToJson(requestBody);
                string searchEndpoint = $"{qdrantUrl}/collections/{collectionName}/points/search";

                using (UnityWebRequest request = new UnityWebRequest(searchEndpoint, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequest);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");

                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.QdrantSearch, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                            $"Qdrant request failed: {request.error}",
                            source: nameof(QdrantRAGService), requestId: requestId, npcSlug: npcSlug,
                            data: new Dictionary<string, object>
                            {
                                ["endpoint"] = searchEndpoint,
                                ["vectorLength"] = queryVector.Count
                            });
                        scope.Error(null, $"Qdrant request failed: {request.error}", new Dictionary<string, object>
                        {
                            ["endpoint"] = searchEndpoint,
                            ["vectorLength"] = queryVector.Count
                        });
                        return string.Empty;
                    }

                    string responseText = request.downloadHandler.text;
                    QdrantSearchResponse response = JsonUtility.FromJson<QdrantSearchResponse>(responseText);

                    if (response != null && response.result != null && response.result.Count > 0)
                    {
                        List<string> results = new List<string>();
                        foreach (var point in response.result)
                        {
                            if (point.payload != null && !string.IsNullOrEmpty(point.payload.text))
                            {
                                results.Add(point.payload.text);
                            }
                        }
                        scope.Success("Qdrant results retrieved.", new Dictionary<string, object>
                        {
                            ["resultCount"] = results.Count,
                            ["vectorLength"] = queryVector.Count,
                            ["endpoint"] = searchEndpoint
                        });
                        return string.Join("\n", results);
                    }
                    scope.Skipped("Qdrant returned empty.", new Dictionary<string, object>
                    {
                        ["vectorLength"] = queryVector.Count,
                        ["endpoint"] = searchEndpoint
                    });
                }
            }
            catch (Exception e)
            {
                NPCFlowLogger.FindOrCreate().Log(NPCFlowStage.QdrantSearch, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    $"Exception during Qdrant search: {e.Message}",
                    source: nameof(QdrantRAGService), requestId: requestId, npcSlug: npcSlug,
                    data: new Dictionary<string, object>
                    {
                        ["exceptionType"] = e.GetType().Name,
                        ["exceptionMessage"] = e.Message
                    });
                scope.Error(e, "Exception during Qdrant search.");
            }

            return string.Empty;
        }

        bool HasValidQdrantUrl()
        {
            return !string.IsNullOrWhiteSpace(qdrantUrl)
                && (qdrantUrl.StartsWith("http://") || qdrantUrl.StartsWith("https://"));
        }

        bool HasValidCollectionName()
        {
            return !string.IsNullOrWhiteSpace(collectionName) && !collectionName.Contains(" ");
        }

        string BuildSearchEndpoint()
        {
            string baseUrl = string.IsNullOrWhiteSpace(qdrantUrl) ? "<missing-qdrant-url>" : qdrantUrl.TrimEnd('/');
            string collection = string.IsNullOrWhiteSpace(collectionName) ? "<missing-collection>" : collectionName;
            return $"{baseUrl}/collections/{collection}/points/search";
        }
    }
}
