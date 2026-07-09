using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    public class QdrantRAGServiceTests
    {
        [Test]
        public void BuildSearchEndpoint_WithValidConfig_ReturnsCorrectUrl()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = "unity_linux_llm_codebase_v1";

            try
            {
                string endpoint = service.BuildSearchEndpoint();
                Assert.That(
                    endpoint,
                    Is.EqualTo(
                        "http://localhost:6333/collections/unity_linux_llm_codebase_v1/points/search"
                    )
                );
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void BuildSearchEndpoint_WithMissingUrl_UsesPlaceholder()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();
            service.QdrantUrl = null;
            service.CollectionName = "unity_linux_llm_codebase_v1";

            try
            {
                string endpoint = service.BuildSearchEndpoint();
                Assert.That(endpoint, Does.Contain("<missing-qdrant-url>"));
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void BuildSearchEndpoint_WithMissingCollection_UsesPlaceholder()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = null;

            try
            {
                string endpoint = service.BuildSearchEndpoint();
                Assert.That(endpoint, Does.Contain("<missing-collection>"));
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void BuildSearchEndpoint_TrimsTrailingSlashFromUrl()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333/";
            service.CollectionName = "unity_linux_llm_codebase_v1";

            try
            {
                string endpoint = service.BuildSearchEndpoint();
                Assert.That(
                    endpoint,
                    Is.EqualTo(
                        "http://localhost:6333/collections/unity_linux_llm_codebase_v1/points/search"
                    )
                );
                Assert.That(endpoint, Does.Not.Contain("//collections"));
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void HasValidQdrantUrl_AcceptsHttpAndHttps()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            try
            {
                service.QdrantUrl = "http://localhost:6333";
                Assert.That(service.HasValidQdrantUrl(), Is.True);

                service.QdrantUrl = "https://qdrant.example.com";
                Assert.That(service.HasValidQdrantUrl(), Is.True);

                service.QdrantUrl = "localhost:6333";
                Assert.That(service.HasValidQdrantUrl(), Is.False);

                service.QdrantUrl = "";
                Assert.That(service.HasValidQdrantUrl(), Is.False);

                service.QdrantUrl = null;
                Assert.That(service.HasValidQdrantUrl(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void HasValidCollectionName_RejectsSpaces()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            try
            {
                service.CollectionName = "unity_linux_llm_codebase_v1";
                Assert.That(service.HasValidCollectionName(), Is.True);

                service.CollectionName = "my collection";
                Assert.That(service.HasValidCollectionName(), Is.False);

                service.CollectionName = "";
                Assert.That(service.HasValidCollectionName(), Is.False);

                service.CollectionName = null;
                Assert.That(service.HasValidCollectionName(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void DefaultConfiguration_IsValid()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            try
            {
                Assert.That(service.QdrantUrl, Is.EqualTo("http://localhost:6333"));
                Assert.That(service.CollectionName, Is.EqualTo("unity_linux_llm_codebase_v1"));
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void SearchMemoryAsync_WithMockEmbedderAndResponse_ReturnsText()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var embedderObject = new GameObject("MockEmbedder");
            var embedder = embedderObject.AddComponent<TestableLocalAIEmbedder>();
            embedder.mockResponse = "{\"data\":[{\"embedding\":[0.1,0.2,0.3],\"index\":0}]}";

            var service = serviceObject.AddComponent<TestableQdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = "unity_linux_llm_codebase_v1";
            service.Embedder = embedder;
            service.mockResponse =
                "{\"result\":[{\"score\":0.95,\"payload\":{\"text\":\"Found knowledge\"}}]}";

            try
            {
                string result = service.SearchMemoryAsync("test query", 5, null, null).GetAwaiter().GetResult();
                Assert.That(result, Is.EqualTo("Found knowledge"));
                Assert.That(
                    service.lastRequestedEndpoint,
                    Does.Contain("unity_linux_llm_codebase_v1/points/search")
                );
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
                Object.DestroyImmediate(embedderObject);
            }
        }

        [Test]
        public void SearchMemoryAsync_WithNullMockResponse_ReturnsEmpty()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var embedderObject = new GameObject("MockEmbedder");
            var embedder = embedderObject.AddComponent<TestableLocalAIEmbedder>();
            embedder.mockResponse = "{\"data\":[{\"embedding\":[0.1,0.2,0.3],\"index\":0}]}";

            var service = serviceObject.AddComponent<TestableQdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = "unity_linux_llm_codebase_v1";
            service.Embedder = embedder;
            service.mockResponse = null;

            try
            {
                string result = service.SearchMemoryAsync("test query", 5, null, null).GetAwaiter().GetResult();
                Assert.That(result, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
                Object.DestroyImmediate(embedderObject);
            }
        }

        [Test]
        public void SearchMemoryAsync_WithEmptyQdrantResult_ReturnsEmpty()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var embedderObject = new GameObject("MockEmbedder");
            var embedder = embedderObject.AddComponent<TestableLocalAIEmbedder>();
            embedder.mockResponse = "{\"data\":[{\"embedding\":[0.1,0.2,0.3],\"index\":0}]}";

            var service = serviceObject.AddComponent<TestableQdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = "unity_linux_llm_codebase_v1";
            service.Embedder = embedder;
            service.mockResponse = "{\"result\":[]}";

            try
            {
                string result = service.SearchMemoryAsync("test query", 5, null, null).GetAwaiter().GetResult();
                Assert.That(result, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
                Object.DestroyImmediate(embedderObject);
            }
        }

        [Test]
        public void SearchMemoryAsync_EmbedsderReturnsEmptyVector_ReturnsEmpty()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var embedderObject = new GameObject("MockEmbedder");
            var embedder = embedderObject.AddComponent<TestableLocalAIEmbedder>();
            embedder.mockResponse = "{}";

            var service = serviceObject.AddComponent<TestableQdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = "unity_linux_llm_codebase_v1";
            service.Embedder = embedder;

            try
            {
                string result = service.SearchMemoryAsync("test query", 5, null, null).GetAwaiter().GetResult();
                Assert.That(result, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
                Object.DestroyImmediate(embedderObject);
            }
        }

        [Test]
        public void SearchMemoryAsync_NoEmbedderAssigned_FindsInScene()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var embedderObject = new GameObject("MockEmbedder");
            var embedder = embedderObject.AddComponent<TestableLocalAIEmbedder>();
            embedder.mockResponse = "{\"data\":[{\"embedding\":[0.1],\"index\":0}]}";

            var service = serviceObject.AddComponent<TestableQdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = "unity_linux_llm_codebase_v1";
            service.Embedder = null;
            service.mockResponse = "{\"result\":[]}";

            try
            {
                string result = service.SearchMemoryAsync("test query", 5, null, null).GetAwaiter().GetResult();
                Assert.That(result, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
                Object.DestroyImmediate(embedderObject);
            }
        }

        [Test]
        public void SearchMemoryAsync_ReturnsMultipleResults()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var embedderObject = new GameObject("MockEmbedder");
            var embedder = embedderObject.AddComponent<TestableLocalAIEmbedder>();
            embedder.mockResponse = "{\"data\":[{\"embedding\":[0.1,0.2,0.3],\"index\":0}]}";

            var service = serviceObject.AddComponent<TestableQdrantRAGService>();
            service.QdrantUrl = "http://localhost:6333";
            service.CollectionName = "unity_linux_llm_codebase_v1";
            service.Embedder = embedder;
            service.mockResponse =
                "{\"result\":[{\"score\":0.95,\"payload\":{\"text\":\"First result\"}},{\"score\":0.80,\"payload\":{\"text\":\"Second result\"}}]}";

            try
            {
                string result = service.SearchMemoryAsync("test query", 5, null, null).GetAwaiter().GetResult();
                Assert.That(result, Is.EqualTo("First result\nSecond result"));
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
                Object.DestroyImmediate(embedderObject);
            }
        }
    }

    public class TestableQdrantRAGService : QdrantRAGService
    {
        public string mockResponse;
        public string lastRequestedEndpoint;
        public string lastRequestedJson;

        protected override Task<string> SendSearchRequestAsync(string endpoint, string json)
        {
            lastRequestedEndpoint = endpoint;
            lastRequestedJson = json;
            return Task.FromResult(mockResponse);
        }
    }
}
