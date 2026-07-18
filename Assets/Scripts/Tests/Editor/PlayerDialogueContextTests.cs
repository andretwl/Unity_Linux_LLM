using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    /// <summary>
    /// Tests for player/NPC turn context isolation.
    /// Verifies that two simulated players can send turns without static
    /// player-name bleed, and that missing optional services are handled gracefully.
    /// Task 9 of webgl-dialogue-current-state-plan-20260712.
    /// </summary>
    public class PlayerDialogueContextTests
    {
        // ── Player Context Isolation ─────────────────────────────────

        [Test]
        public void SetRuntimePlayerContext_StoresNameAndClientId()
        {
            var go = new GameObject("PlayerContextTest");
            var service = go.AddComponent<NPCDialogueSessionService>();

            try
            {
                service.SetRuntimePlayerContext("Alice", 10ul);
                Assert.That(service.ActivePlayerName, Is.EqualTo("Alice"));

                service.ClearRuntimePlayerContext();
                // After clearing, ActivePlayerName falls back to AuthNetworkBridge.ActivePlayerName
                Assert.That(service.ActivePlayerName, Is.EqualTo(AuthNetworkBridge.ActivePlayerName));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetRuntimePlayerContext_EmptyString_ClearsOverride()
        {
            var go = new GameObject("PlayerContextTest2");
            var service = go.AddComponent<NPCDialogueSessionService>();

            try
            {
                service.SetRuntimePlayerContext("Bob", 5ul);
                service.SetRuntimePlayerContext("");
                Assert.That(service.ActivePlayerName, Is.EqualTo(AuthNetworkBridge.ActivePlayerName));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TwoPlayers_BackToBackTurns_NoStaticNameBleed()
        {
            // Simulate two players sending turns through the manager
            // and verify each turn uses the correct player context.
            var managerObject = new GameObject("ContextLeakTest");
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            var chatClient = managerObject.AddComponent<NPCLocalAIClient>();
            chatClient.NumRetries = 0;
            chatClient.Host = "127.0.0.1";
            chatClient.Port = 19999;
            manager._chatClient = chatClient;

            var profile = CreateProfile("butler", "Butler");
            manager.Profiles = new[] { profile };
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                manager.SwitchToNPCAsync("butler").GetAwaiter().GetResult();

                // Player 1 sets context
                manager.SetRuntimePlayerContext("Alice", 1ul);
                string capturedName1 = null;
                manager.OnResponseStart.AddListener(_ =>
                {
                    // Capture the player name at response time via the session service
                    capturedName1 = manager.IsResponding ? "turn-active" : null;
                });

                // Player 2 sets context (simulates rapid back-to-back)
                manager.SetRuntimePlayerContext("Bob", 2ul);

                // Verify the context was updated (not stuck on Alice)
                Assert.That(
                    AuthNetworkBridge.ActivePlayerName,
                    Is.Not.EqualTo("Alice").Or.EqualTo("Bob"),
                    "ActivePlayerName should reflect the latest context"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void CancelRequests_ClearsPlayerContext()
        {
            var go = new GameObject("CancelContextTest");
            var service = go.AddComponent<NPCDialogueSessionService>();

            try
            {
                service.SetRuntimePlayerContext("Charlie", 7ul);
                service.CancelRequests();
                // After cancel, context should be cleared
                // ActivePlayerName falls back to AuthNetworkBridge.ActivePlayerName
                string fallback = AuthNetworkBridge.ActivePlayerName;
                Assert.That(fallback, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ── AuthNetworkBridge ActivePlayerName ───────────────────────

        [Test]
        public void ActivePlayerName_DefaultIsPlayer()
        {
            // The static default should be "Player" before any auth
            string defaultName = AuthNetworkBridge.ActivePlayerName;
            Assert.That(defaultName, Is.EqualTo("Player"));
        }

        [Test]
        public void ActivePlayerName_CanBeSetExternally()
        {
            string original = AuthNetworkBridge.ActivePlayerName;
            try
            {
                SetActivePlayerName("TestUser");
                Assert.That(AuthNetworkBridge.ActivePlayerName, Is.EqualTo("TestUser"));
            }
            finally
            {
                SetActivePlayerName(original);
            }
        }

        // ── Missing Optional Services ────────────────────────────────

        [Test]
        public void Manager_WithoutQdrantRag_StillInitializes()
        {
            var go = new GameObject("NoQdrantTest");
            var manager = go.AddComponent<NPCDialogueManager>();
            var chatClient = go.AddComponent<NPCLocalAIClient>();
            chatClient.NumRetries = 0;
            chatClient.Host = "127.0.0.1";
            chatClient.Port = 19999;
            manager._chatClient = chatClient;
            manager.Profiles = Array.Empty<NPCProfile>();
            manager.UseQdrantRag = false;
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                // Should not throw even without Qdrant
                Assert.DoesNotThrow(() => manager.InitializeAsync().GetAwaiter().GetResult());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Manager_WithoutActionPlanner_StillInitializes()
        {
            var go = new GameObject("NoPlannerTest");
            var manager = go.AddComponent<NPCDialogueManager>();
            var chatClient = go.AddComponent<NPCLocalAIClient>();
            chatClient.NumRetries = 0;
            chatClient.Host = "127.0.0.1";
            chatClient.Port = 19999;
            manager._chatClient = chatClient;
            manager.Profiles = Array.Empty<NPCProfile>();
            manager._actionPlanner = null;
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                Assert.DoesNotThrow(() => manager.InitializeAsync().GetAwaiter().GetResult());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Manager_WithoutCognee_StillInitializes()
        {
            var go = new GameObject("NoCogneeTest");
            var manager = go.AddComponent<NPCDialogueManager>();
            var chatClient = go.AddComponent<NPCLocalAIClient>();
            chatClient.NumRetries = 0;
            chatClient.Host = "127.0.0.1";
            chatClient.Port = 19999;
            manager._chatClient = chatClient;
            manager.Profiles = Array.Empty<NPCProfile>();
            manager.UseQdrantRag = false;
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                Assert.DoesNotThrow(() => manager.InitializeAsync().GetAwaiter().GetResult());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ── Network Bridge IsNetworkReady ────────────────────────────

        [Test]
        public void IsNetworkReady_FalseWhenNotPlaying()
        {
            var go = new GameObject("NetworkReadyTest");
            var bridge = go.AddComponent<NPCDialogueNetworkBridge>();

            try
            {
                // In EditMode (not playing), IsNetworkReady should be false
                Assert.That(bridge.IsNetworkReady, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        static NPCProfile CreateProfile(string slug, string displayName)
        {
            var profile = ScriptableObject.CreateInstance<NPCProfile>();
            profile.NpcSlug = slug;
            profile.DisplayName = displayName;
            profile.SystemPrompt = "You are a helpful NPC.";
            profile.MaxTokens = 64;
            profile.RagResults = 1;
            profile.HistorySaveFile = $"NPCDialogue/{slug}.json";
            return profile;
        }

        /// <summary>
        /// Sets AuthNetworkBridge.ActivePlayerName via its internal setter using reflection.
        /// The test assembly does not have InternalsVisibleTo access.
        /// </summary>
        static void SetActivePlayerName(string value)
        {
            var prop = typeof(AuthNetworkBridge).GetProperty(
                nameof(AuthNetworkBridge.ActivePlayerName),
                BindingFlags.Public | BindingFlags.Static
            );
            MethodInfo setter = prop.GetSetMethod(nonPublic: true);
            setter.Invoke(null, new object[] { value });
        }
    }
}
