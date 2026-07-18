using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    public class QdrantRAGServiceTests
    {
        [Test]
        public void DefaultValues_AreValid()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            Assert.IsNotNull(service);
            Assert.AreEqual("npc_knowledge", service.CollectionName);
            Assert.AreEqual("http://localhost:6333", service.QdrantUrl);

            Object.DestroyImmediate(serviceObject);
        }

        [Test]
        public void Initialize_SetsProperties()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            service.QdrantUrl = "http://192.168.1.100:7633";
            service.CollectionName = "test_collection";

            Assert.AreEqual("http://192.168.1.100:7633", service.QdrantUrl);
            Assert.AreEqual("test_collection", service.CollectionName);

            Object.DestroyImmediate(serviceObject);
        }

        [Test]
        public async Task SearchMemoryAsync_WithEmptyQuery_ReturnsEmpty()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            string result = await service.SearchMemoryAsync("", 3, "req-1", "butler");
            Assert.IsEmpty(result);

            Object.DestroyImmediate(serviceObject);
        }

        [Test]
        public async Task SearchMemoryAsync_WithZeroResults_ReturnsEmpty()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            string result = await service.SearchMemoryAsync("hello", 0, "req-1", "butler");
            Assert.IsEmpty(result);

            Object.DestroyImmediate(serviceObject);
        }

        [Test]
        public async Task SearchMemoryAsync_WithNullNpcSlug_StillWorks()
        {
            var serviceObject = new GameObject(nameof(QdrantRAGServiceTests));
            var service = serviceObject.AddComponent<QdrantRAGService>();

            string result = await service.SearchMemoryAsync("hello", 3, "req-1", null);
            Assert.IsEmpty(result);

            Object.DestroyImmediate(serviceObject);
        }
    }
}
