using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    public class NPCBackendReadinessTests
    {
        [Test]
        public void BackendSnapshotRequiresOnlyConfiguredBackends()
        {
            var snapshot = new NPCBackendReadinessSnapshot
            {
                auth = new NPCBackendProbeResult { reachable = true },
                localAi = new NPCBackendProbeResult { reachable = false },
            };

            Assert.That(
                snapshot.AllRequiredBackendsReachable(requireAuth: true, requireLocalAi: false),
                Is.True
            );
            Assert.That(
                snapshot.AllRequiredBackendsReachable(requireAuth: true, requireLocalAi: true),
                Is.False
            );
        }

        [Test]
        public void BackendReadinessServiceBuildsProbeUrlsFromSceneServices()
        {
            var gameObject = new GameObject("NPCBackendReadinessTests");
            var authObject = new GameObject("AuthService");
            var dialogueObject = new GameObject("DialogueManager");
            var service = gameObject.AddComponent<NPCBackendReadinessService>();
            var authService = authObject.AddComponent<PlayerAuthService>();
            var dialogueManager = dialogueObject.AddComponent<NPCDialogueManager>();

            try
            {
                typeof(PlayerAuthService)
                    .GetField("serviceBaseUrl", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(authService, "http://localhost:5100/");
                dialogueManager.RemoteHost = "127.0.0.1";
                dialogueManager.RemotePort = NPCLocalAIConfig.LocalAIDirectPort;
                service.AuthService = authService;
                service.DialogueManager = dialogueManager;

                MethodInfo authMethod = typeof(NPCBackendReadinessService).GetMethod(
                    "BuildAuthProbeUrl",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                MethodInfo localAiMethod = typeof(NPCBackendReadinessService).GetMethod(
                    "BuildLocalAiProbeUrl",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                Assert.That(
                    authMethod?.Invoke(service, null) as string,
                    Is.EqualTo("http://localhost:5100/api/auth/session")
                );
                Assert.That(
                    localAiMethod?.Invoke(service, null) as string,
                    Is.EqualTo($"http://127.0.0.1:{NPCLocalAIConfig.LocalAIDirectPort}/v1/models")
                );
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(authObject);
                Object.DestroyImmediate(dialogueObject);
            }
        }
    }
}
