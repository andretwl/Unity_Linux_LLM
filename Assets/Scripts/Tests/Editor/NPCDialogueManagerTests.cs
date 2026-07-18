using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    public class NPCDialogueManagerTests
    {
        [Test]
        public void SwitchingToKnownProfileSetsCurrentProfileAndRaisesNpcChangedEvent()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            AttachMinimalChatClient(managerObject, manager);
            var profile = CreateProfile("butler", "Butler");
            manager.Profiles = new[] { profile };
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            string changedName = null;
            manager.OnNpcChanged.AddListener(name => changedName = name);

            try
            {
                manager.SwitchToNPCAsync("butler").GetAwaiter().GetResult();

                Assert.That(manager.CurrentProfile, Is.SameAs(profile));
                Assert.That(changedName, Is.EqualTo("Butler"));
            }
            finally
            {
                UnityEngine.Object.Destroy(managerObject);
                UnityEngine.Object.Destroy(profile);
            }
        }

        [Test]
        public void SendingBlankMessage_DoesNotRaiseErrorOrStartResponse()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            AttachMinimalChatClient(managerObject, manager);
            var profile = CreateProfile("maid", "Maid");
            manager.Profiles = new[] { profile };
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            string errorMessage = null;
            string responseStart = null;
            manager.OnError.AddListener(message => errorMessage = message);
            manager.OnResponseStart.AddListener(message => responseStart = message);

            try
            {
                manager.SwitchToNPCAsync("maid").GetAwaiter().GetResult();
                manager.SendDialogueMessage("   ");

                Assert.That(errorMessage, Is.Null);
                Assert.That(responseStart, Is.Null);
                Assert.That(manager.IsResponding, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SwitchToNPCAsync_UnknownSlug_RaisesError()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            AttachMinimalChatClient(managerObject, manager);
            manager.Profiles = Array.Empty<NPCProfile>();
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            string errorMessage = null;
            manager.OnError.AddListener(message => errorMessage = message);

            try
            {
                manager.SwitchToNPCAsync("nonexistent").GetAwaiter().GetResult();
                Assert.That(errorMessage, Is.EqualTo("NPC 'nonexistent' not found"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void SendMessage_NoProfileSelected_RaisesError()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            manager.Profiles = Array.Empty<NPCProfile>();
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            string errorMessage = null;
            manager.OnError.AddListener(message => errorMessage = message);

            try
            {
                manager.SendDialogueMessage("Hello");
                Assert.That(errorMessage, Is.EqualTo("No NPC selected"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void GetDefaultProfileSlug_WithNoProfiles_ReturnsEmpty()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            manager.Profiles = Array.Empty<NPCProfile>();
            manager.InitializeOnStart = false;

            try
            {
                string slug = manager.GetDefaultProfileSlug();
                Assert.That(slug, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void GetDefaultProfileSlug_WithProfiles_ReturnsFirstSlug()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            var butler = CreateProfile("butler", "Butler");
            var maid = CreateProfile("maid", "Maid");
            manager.Profiles = new[] { butler, maid };
            manager.InitializeOnStart = false;

            try
            {
                string slug = manager.GetDefaultProfileSlug();
                Assert.That(slug, Is.EqualTo("butler"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(butler);
                UnityEngine.Object.DestroyImmediate(maid);
            }
        }

        [Test]
        public void GetNPCNames_ReturnsAllSlugs()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            var butler = CreateProfile("butler", "Butler");
            var maid = CreateProfile("maid", "Maid");
            manager.Profiles = new[] { butler, maid };
            manager.InitializeOnStart = false;

            try
            {
                string[] names = manager.GetNPCNames();
                Assert.That(names, Is.EquivalentTo(new[] { "butler", "maid" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(butler);
                UnityEngine.Object.DestroyImmediate(maid);
            }
        }

        [Test]
        public void IsTechnicalCodebaseQuestion_DetectsTechnicalMarkers()
        {
            Assert.That(
                NPCDialogueSessionService.IsTechnicalCodebaseQuestion(
                    "Where is the script for PlayerController?"
                ),
                Is.True
            );
            Assert.That(
                NPCDialogueSessionService.IsTechnicalCodebaseQuestion(
                    "How does the RAG collection work?"
                ),
                Is.True
            );
            Assert.That(
                NPCDialogueSessionService.IsTechnicalCodebaseQuestion(
                    "Which file implements NPCDialogueManager?"
                ),
                Is.True
            );
            Assert.That(
                NPCDialogueSessionService.IsTechnicalCodebaseQuestion("Tell me about your day"),
                Is.False
            );
            Assert.That(NPCDialogueSessionService.IsTechnicalCodebaseQuestion(""), Is.False);
            Assert.That(NPCDialogueSessionService.IsTechnicalCodebaseQuestion("   "), Is.False);
            Assert.That(NPCDialogueSessionService.IsTechnicalCodebaseQuestion(null), Is.False);
        }

        [Test]
        public void CancelRequests_ClearsState()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            manager.InitializeOnStart = false;

            try
            {
                manager.SetRuntimePlayerContext("Alice", 99ul);
                Assert.DoesNotThrow(() => manager.CancelRequests());

                manager.SendDialogueMessage("Hello");
                Assert.That(manager.IsResponding, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void SetRuntimePlayerContext_StoresValues()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            manager.InitializeOnStart = false;

            try
            {
                manager.SetRuntimePlayerContext("Alice", 42ul);
                Assert.DoesNotThrow(() => manager.ClearRuntimePlayerContext());
                Assert.DoesNotThrow(() => manager.SetRuntimePlayerContext("", null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void ClearHistory_WithName_ClearsSingleNpc()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            AttachMinimalChatClient(managerObject, manager);
            var profile = CreateProfile("butler", "Butler");
            profile.HistorySaveFile = $"NPCDialogue/test_clear{Guid.NewGuid():N}.json";
            manager.Profiles = new[] { profile };
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                manager.SwitchToNPCAsync("butler").GetAwaiter().GetResult();
                List<DialogueEntry> history = manager.GetHistory("butler");
                Assert.That(history, Is.Empty);

                manager.ClearHistory("butler");
                Assert.DoesNotThrow(() => manager.ClearHistory(""));
                Assert.DoesNotThrow(() => manager.ClearHistory("nonexistent"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void CaptureAndApplyHistorySnapshot_RoundTrips()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            AttachMinimalChatClient(managerObject, manager);
            var profile = CreateProfile("butler", "Butler");
            manager.Profiles = new[] { profile };
            manager.PersistHistory = true;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                manager.SwitchToNPCAsync("butler").GetAwaiter().GetResult();

                var snapshot = manager.CaptureHistorySnapshot();
                Assert.That(snapshot, Contains.Key("butler"));

                var modified = new Dictionary<string, List<DialogueEntry>>
                {
                    ["butler"] = new List<DialogueEntry>
                    {
                        new DialogueEntry("user", "Hello"),
                        new DialogueEntry("assistant", "Hi"),
                    },
                };
                manager.ApplyHistorySnapshot(modified);

                List<DialogueEntry> loaded = manager.GetHistory("butler");
                Assert.That(loaded, Has.Count.EqualTo(2));
                Assert.That(loaded[0].content, Is.EqualTo("Hello"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void InitializeAsync_CanBeCalledMultipleTimes()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            AttachMinimalChatClient(managerObject, manager);
            manager.Profiles = Array.Empty<NPCProfile>();
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                var first = manager.InitializeAsync();
                var second = manager.InitializeAsync();
                Assert.That(first, Is.SameAs(second));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void SwitchToNPCAsync_FindsProfileByDisplayName()
        {
            var managerObject = new GameObject(nameof(NPCDialogueManagerTests));
            var manager = managerObject.AddComponent<NPCDialogueManager>();
            AttachMinimalChatClient(managerObject, manager);
            var profile = CreateProfile("chef", "Chef");
            manager.Profiles = new[] { profile };
            manager.PersistHistory = false;
            manager.EnableRAG = false;
            manager.InitializeOnStart = false;

            try
            {
                manager.SwitchToNPCAsync("Chef").GetAwaiter().GetResult();
                Assert.That(manager.CurrentProfile, Is.SameAs(profile));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

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

        static void AttachMinimalChatClient(GameObject managerObject, NPCDialogueManager manager)
        {
            manager._chatClient = managerObject.AddComponent<NPCLocalAIClient>();
            manager._chatClient.NumRetries = 0;
            manager._chatClient.Host = "127.0.0.1";
            manager._chatClient.Port = 19999;
        }
    }
}
