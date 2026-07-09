using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace NPCSystem.Tests
{
    public class NPCDialogueNetworkingTests
    {
        [Test]
        public void DialogueRequestSanitizeInPlaceTrimsValuesAndGeneratesRequestId()
        {
            var request = new NPCDialogueRequestMessage
            {
                requestId = "   ",
                npcSlug = "  Butler  ",
                playerMessage = "  Where were you last night?  ",
            };

            request.SanitizeInPlace();

            Assert.That(request.requestId, Is.Not.Null.And.Not.Empty);
            Assert.That(request.npcSlug, Is.EqualTo("butler"));
            Assert.That(request.playerMessage, Is.EqualTo("Where were you last night?"));
        }

        [Test]
        public void DialogueSelectionSanitizeInPlaceTrimsAndLowercasesNpcSlug()
        {
            var selection = new NPCDialogueSelectionMessage { npcSlug = "  Chef  " };

            selection.SanitizeInPlace();

            Assert.That(selection.npcSlug, Is.EqualTo("chef"));
        }

        [Test]
        public void DialogueResponseMessageSanitizeInPlaceCleansContentAndDisplayName()
        {
            var response = new NPCDialogueResponseMessage
            {
                requestId = "  abc123  ",
                npcSlug = "  Butler  ",
                displayName = "  Butler\n\t ",
                content = "Hello\r\nworld!\x00 "
            };

            response.SanitizeInPlace();

            Assert.That(response.requestId, Is.EqualTo("abc123"));
            Assert.That(response.npcSlug, Is.EqualTo("butler"));
            Assert.That(response.displayName, Is.EqualTo("Butler"));
            Assert.That(response.content, Is.EqualTo("Hello world!"));
        }

        [Test]
        public void NetworkSessionManagerStoresAndRetrievesPerClientSelection()
        {
            var gameObject = new GameObject("NPCNetworkSessionManagerTests");
            var sessionManager = gameObject.AddComponent<NPCNetworkSessionManager>();

            try
            {
                sessionManager.SetSelectedNpcSlug(11ul, "Butler");
                sessionManager.SetSelectedNpcSlug(22ul, "Chef");

                Assert.That(
                    sessionManager.TryGetSelectedNpcSlug(11ul, out string firstNpc),
                    Is.True
                );
                Assert.That(firstNpc, Is.EqualTo("butler"));
                Assert.That(
                    sessionManager.TryGetSelectedNpcSlug(22ul, out string secondNpc),
                    Is.True
                );
                Assert.That(secondNpc, Is.EqualTo("chef"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NetworkSessionManagerClearClientSessionRemovesSelection()
        {
            var gameObject = new GameObject("NPCNetworkSessionManagerTests");
            var sessionManager = gameObject.AddComponent<NPCNetworkSessionManager>();

            try
            {
                sessionManager.SetSelectedNpcSlug(11ul, "Maid");
                sessionManager.ClearClientSession(11ul);

                Assert.That(sessionManager.TryGetSelectedNpcSlug(11ul, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NetworkSessionManagerStoresIndependentHistorySnapshotsPerClient()
        {
            var gameObject = new GameObject("NPCNetworkSessionManagerTests");
            var sessionManager = gameObject.AddComponent<NPCNetworkSessionManager>();

            try
            {
                sessionManager.SetHistorySnapshot(
                    11ul,
                    "butler",
                    new System.Collections.Generic.List<DialogueEntry>
                    {
                        new DialogueEntry("user", "Where were you?"),
                        new DialogueEntry("assistant", "In the study."),
                    }
                );
                sessionManager.SetHistorySnapshot(
                    22ul,
                    "butler",
                    new System.Collections.Generic.List<DialogueEntry>
                    {
                        new DialogueEntry("user", "Did you hear a noise?"),
                        new DialogueEntry("assistant", "Only the clock."),
                    }
                );

                var firstHistory = sessionManager.GetHistorySnapshot(11ul, "butler");
                var secondHistory = sessionManager.GetHistorySnapshot(22ul, "butler");

                Assert.That(firstHistory, Has.Count.EqualTo(2));
                Assert.That(firstHistory[0].content, Is.EqualTo("Where were you?"));
                Assert.That(secondHistory, Has.Count.EqualTo(2));
                Assert.That(secondHistory[0].content, Is.EqualTo("Did you hear a noise?"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NetworkSessionManagerStoresIndependentEvidenceSnapshotsPerClient()
        {
            var gameObject = new GameObject("NPCNetworkSessionManagerTests");
            var sessionManager = gameObject.AddComponent<NPCNetworkSessionManager>();

            try
            {
                var firstSnapshot = new NPCEvidenceStateSnapshot();
                firstSnapshot.discoveredClues.Add(
                    new ClueEntry("butler", "The study window was open.", "observation", 1f)
                );
                firstSnapshot.obtainedItems.Add("rusty-key");

                var secondSnapshot = new NPCEvidenceStateSnapshot();
                secondSnapshot.visitedLocations.Add("kitchen");
                secondSnapshot.npcTrustKeys.Add("chef");
                secondSnapshot.npcTrustValues.Add(72);

                sessionManager.SetEvidenceSnapshot(11ul, firstSnapshot);
                sessionManager.SetEvidenceSnapshot(22ul, secondSnapshot);

                var loadedFirst = sessionManager.GetEvidenceSnapshot(11ul);
                var loadedSecond = sessionManager.GetEvidenceSnapshot(22ul);

                Assert.That(loadedFirst.discoveredClues, Has.Count.EqualTo(1));
                Assert.That(loadedFirst.obtainedItems, Has.Count.EqualTo(1));
                Assert.That(loadedSecond.visitedLocations, Has.Count.EqualTo(1));
                Assert.That(loadedSecond.npcTrustValues, Has.Count.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NetworkSessionManagerStoresIndependentPlayerNamesPerClient()
        {
            var gameObject = new GameObject("NPCNetworkSessionManagerTests");
            var sessionManager = gameObject.AddComponent<NPCNetworkSessionManager>();

            try
            {
                sessionManager.SetPlayerDisplayName(11ul, "Alice");
                sessionManager.SetPlayerDisplayName(22ul, "Bob");

                Assert.That(sessionManager.GetPlayerDisplayName(11ul), Is.EqualTo("Alice"));
                Assert.That(sessionManager.GetPlayerDisplayName(22ul), Is.EqualTo("Bob"));
                Assert.That(sessionManager.GetPlayerDisplayName(33ul), Is.EqualTo(string.Empty));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LooksLikeFallbackPlayerName_DetectsFallbackNames()
        {
            Assert.That(NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName(null), Is.True);
            Assert.That(NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName(""), Is.True);
            Assert.That(NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName("  "), Is.True);
            Assert.That(
                NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName("Player 123"),
                Is.True
            );
            Assert.That(NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName("Player 0"), Is.True);
            Assert.That(NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName("Alice"), Is.False);
            Assert.That(NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName("Player"), Is.False);
            Assert.That(
                NPCDialogueNetworkBridge.LooksLikeFallbackPlayerName(" Player 42 "),
                Is.True
            );
        }

        [Test]
        public void CloneHistorySnapshot_DeepCopies()
        {
            var original = new Dictionary<string, List<DialogueEntry>>
            {
                ["butler"] = new List<DialogueEntry>
                {
                    new DialogueEntry("user", "Hello"),
                    new DialogueEntry("assistant", "Hi"),
                },
                ["maid"] = new List<DialogueEntry>
                {
                    new DialogueEntry("user", "Clean the room"),
                    new DialogueEntry("assistant", "Of course"),
                },
            };

            var clone = NPCDialogueNetworkBridge.CloneHistorySnapshot(original);

            Assert.That(clone, Has.Count.EqualTo(2));
            Assert.That(clone["butler"][0].content, Is.EqualTo("Hello"));
            Assert.That(clone["maid"][1].content, Is.EqualTo("Of course"));

            clone["butler"][0].content = "Modified";
            Assert.That(
                original["butler"][0].content,
                Is.EqualTo("Hello"),
                "Clone should be a deep copy"
            );
        }

        [Test]
        public void CloneHistorySnapshot_HandlesNullInput()
        {
            var clone = NPCDialogueNetworkBridge.CloneHistorySnapshot(null);
            Assert.That(clone, Is.Empty);
        }

        [Test]
        public void CloneHistorySnapshot_SkipsNullEntries()
        {
            var original = new Dictionary<string, List<DialogueEntry>>
            {
                ["butler"] = new List<DialogueEntry>
                {
                    new DialogueEntry("user", "Hello"),
                    null,
                    new DialogueEntry("assistant", "Hi"),
                },
            };

            var clone = NPCDialogueNetworkBridge.CloneHistorySnapshot(original);
            Assert.That(clone["butler"], Has.Count.EqualTo(2));
        }

        [Test]
        public void FindProfileBySlug_WithDialogueManager_ReturnsMatchingProfile()
        {
            var bridgeObject = new GameObject(nameof(NPCDialogueNetworkingTests));
            var bridge = bridgeObject.AddComponent<NPCDialogueNetworkBridge>();
            var dialogueObject = new GameObject("DialogueManager");
            var manager = dialogueObject.AddComponent<NPCDialogueManager>();
            var profile = ScriptableObject.CreateInstance<NPCProfile>();
            profile.NpcSlug = "test-npc";
            profile.DisplayName = "Test NPC";
            profile.systemPrompt = "Test";
            profile.maxTokens = 64;
            profile.ragResults = 1;
            profile.historySaveFile = "NPCDialogue/test.json";
            manager.Profiles = new[] { profile };

            try
            {
                bridge.DialogueManager = manager;

                NPCProfile found = bridge.FindProfileBySlug("test-npc");
                Assert.That(found, Is.SameAs(profile));

                NPCProfile notFound = bridge.FindProfileBySlug("nonexistent");
                Assert.That(notFound, Is.Null);

                NPCProfile nullResult = bridge.FindProfileBySlug(null);
                Assert.That(nullResult, Is.Null);

                NPCProfile emptyResult = bridge.FindProfileBySlug("  ");
                Assert.That(emptyResult, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(bridgeObject);
                Object.DestroyImmediate(dialogueObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ShouldRelayLocally_DefaultsToTrueInEditMode()
        {
            var bridgeObject = new GameObject(nameof(NPCDialogueNetworkingTests));
            var bridge = bridgeObject.AddComponent<NPCDialogueNetworkBridge>();

            try
            {
                Assert.That(bridge.ShouldRelayLocally(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(bridgeObject);
            }
        }

        private static void SetOwnerClientId(NetworkBehaviour behaviour, ulong clientId)
        {
            var netObj = behaviour.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj == null)
            {
                netObj = behaviour.gameObject.AddComponent<Unity.Netcode.NetworkObject>();
            }

            var netObjType = typeof(Unity.Netcode.NetworkObject);
            var fields = netObjType.GetFields(
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
            );
            foreach (var f in fields)
            {
                if (
                    f.Name.Contains("OwnerClientId")
                    || f.Name.Contains("ownerClientId")
                    || f.Name.Equals("m_OwnerClientId")
                )
                {
                    try
                    {
                        f.SetValue(netObj, clientId);
                    }
                    catch { }
                }
            }

            var netBehType = typeof(Unity.Netcode.NetworkBehaviour);
            var behFields = netBehType.GetFields(
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
            );
            foreach (var f in behFields)
            {
                if (
                    f.Name.Contains("OwnerClientId")
                    || f.Name.Contains("ownerClientId")
                    || f.Name.Equals("m_OwnerClientId")
                )
                {
                    try
                    {
                        f.SetValue(behaviour, clientId);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void TryVerbalItemTransfer_WithValidGivePattern_TriggersTransfer()
        {
            var bridgeObject = new GameObject("Bridge");
            var bridge = bridgeObject.AddComponent<NPCDialogueNetworkBridge>();

            var itemObject = new GameObject("Item_Ledger");
            var item = itemObject.AddComponent<NPCTransferableItem>();
            item.ItemId = "evidence-ledger";
            item.DisplayName = "Evidence Ledger";

            var avatarObject = new GameObject("Avatar");
            var avatar = avatarObject.AddComponent<NPCPlayerNetworkAvatar>();
            SetOwnerClientId(avatar, 42ul);

            var interactor = avatarObject.AddComponent<NPCNetworkItemInteractor>();

            var sessionManagerObject = new GameObject("SessionManager");
            var sessionManager = sessionManagerObject.AddComponent<NPCNetworkSessionManager>();

            try
            {
                string response =
                    "Take this ledger! It contains everything you need to solve the mystery!";
                bridge.TryVerbalItemTransfer(42ul, response);

                Assert.That(
                    sessionManager.GetEvidenceSnapshot(42ul).obtainedItems,
                    Contains.Item("evidence-ledger")
                );
            }
            finally
            {
                Object.DestroyImmediate(bridgeObject);
                Object.DestroyImmediate(itemObject);
                Object.DestroyImmediate(avatarObject);
                Object.DestroyImmediate(sessionManagerObject);
            }
        }

        [Test]
        public void TryVerbalItemTransfer_WithoutGivePattern_DoesNotTriggerTransfer()
        {
            var bridgeObject = new GameObject("Bridge");
            var bridge = bridgeObject.AddComponent<NPCDialogueNetworkBridge>();

            var itemObject = new GameObject("Item_Ledger");
            var item = itemObject.AddComponent<NPCTransferableItem>();
            item.ItemId = "evidence-ledger";
            item.DisplayName = "Evidence Ledger";

            var avatarObject = new GameObject("Avatar");
            var avatar = avatarObject.AddComponent<NPCPlayerNetworkAvatar>();
            SetOwnerClientId(avatar, 42ul);

            var interactor = avatarObject.AddComponent<NPCNetworkItemInteractor>();

            var sessionManagerObject = new GameObject("SessionManager");
            var sessionManager = sessionManagerObject.AddComponent<NPCNetworkSessionManager>();

            try
            {
                string response =
                    "I have a ledger in my cabinet, but I am too busy cooking to show it to you.";
                bridge.TryVerbalItemTransfer(42ul, response);

                Assert.That(
                    sessionManager.GetEvidenceSnapshot(42ul).obtainedItems,
                    Is.Not.Contains("evidence-ledger")
                );
            }
            finally
            {
                Object.DestroyImmediate(bridgeObject);
                Object.DestroyImmediate(itemObject);
                Object.DestroyImmediate(avatarObject);
                Object.DestroyImmediate(sessionManagerObject);
            }
        }
    }
}
