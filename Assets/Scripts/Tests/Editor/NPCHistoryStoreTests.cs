using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace NPCSystem.Tests
{
    public class NPCHistoryStoreTests
    {
        [Test]
        public void Load_MissingFile_ReturnsEmptyList()
        {
            string relativePath = $"NPCDialogue/Missing_{Guid.NewGuid():N}.json";
            List<DialogueEntry> result = NPCHistoryStore.Load(relativePath);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            string slug = $"test{Guid.NewGuid():N}";
            string relativePath = $"NPCDialogue/{slug}.json";
            string fullPath = NPCHistoryStore.GetFullPath(relativePath);

            try
            {
                var original = new List<DialogueEntry>
                {
                    new DialogueEntry("user", "Hello"),
                    new DialogueEntry("assistant", "Hi there")
                };

                NPCHistoryStore.Save(relativePath, original);
                Assert.That(File.Exists(fullPath), Is.True);

                List<DialogueEntry> loaded = NPCHistoryStore.Load(relativePath);
                Assert.That(loaded, Has.Count.EqualTo(2));
                Assert.That(loaded[0].Role, Is.EqualTo("user"));
                Assert.That(loaded[0].Content, Is.EqualTo("Hello"));
                Assert.That(loaded[1].Role, Is.EqualTo("assistant"));
                Assert.That(loaded[1].Content, Is.EqualTo("Hi there"));
            }
            finally
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
                string dir = Path.GetDirectoryName(fullPath);
                if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0)
                    Directory.Delete(dir);
            }
        }

        [Test]
        public void Load_MalformedJson_ReturnsEmptyList()
        {
            string slug = $"corrupt{Guid.NewGuid():N}";
            string relativePath = $"NPCDialogue/{slug}.json";
            string fullPath = NPCHistoryStore.GetFullPath(relativePath);

            try
            {
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, "{this is not valid json!!}");

                List<DialogueEntry> result = NPCHistoryStore.Load(relativePath);
                Assert.That(result, Is.Empty);
            }
            finally
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
        }

        [Test]
        public void Normalize_AlternatesRolesCorrectly()
        {
            var entries = new List<DialogueEntry>
            {
                new DialogueEntry("user", "Where were you?"),
                new DialogueEntry("assistant", "In the study."),
                new DialogueEntry("user", "Anyone see you?"),
                new DialogueEntry("assistant", "Only the clock.")
            };

            List<DialogueEntry> result = NPCHistoryStore.NormalizeForChatTemplate(entries, out int dropped);
            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(dropped, Is.Zero);
            Assert.That(result[0].Role, Is.EqualTo("user"));
            Assert.That(result[1].Role, Is.EqualTo("assistant"));
            Assert.That(result[2].Role, Is.EqualTo("user"));
            Assert.That(result[3].Role, Is.EqualTo("assistant"));
        }

        [Test]
        public void Normalize_DropsOddTrailingEntry()
        {
            var entries = new List<DialogueEntry>
            {
                new DialogueEntry("user", "Hello"),
                new DialogueEntry("assistant", "Hi"),
                new DialogueEntry("user", "Trailing")
            };

            List<DialogueEntry> result = NPCHistoryStore.NormalizeForChatTemplate(entries, out int dropped);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(dropped, Is.EqualTo(1));
        }

        [Test]
        public void Normalize_DropsNullEntries()
        {
            var entries = new List<DialogueEntry>
            {
                new DialogueEntry("user", "Hello"),
                null,
                new DialogueEntry("assistant", "Hi")
            };

            List<DialogueEntry> result = NPCHistoryStore.NormalizeForChatTemplate(entries, out int dropped);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(dropped, Is.EqualTo(1));
        }

        [Test]
        public void Normalize_DropsEntriesWithBlankContent()
        {
            var entries = new List<DialogueEntry>
            {
                new DialogueEntry("user", "Hello"),
                new DialogueEntry("assistant", "  "),
                new DialogueEntry("user", "Still here?")
            };

            List<DialogueEntry> result = NPCHistoryStore.NormalizeForChatTemplate(entries, out int dropped);
            Assert.That(result, Has.Count.EqualTo(0));
            Assert.That(dropped, Is.EqualTo(3));
        }

        [Test]
        public void Normalize_DropsOutOfOrderRoles()
        {
            var entries = new List<DialogueEntry>
            {
                new DialogueEntry("assistant", "I answer first?"),
                new DialogueEntry("user", "That's out of order"),
            };

            List<DialogueEntry> result = NPCHistoryStore.NormalizeForChatTemplate(entries, out int dropped);
            Assert.That(result, Is.Empty);
            Assert.That(dropped, Is.EqualTo(2));
        }

        [Test]
        public void Normalize_CaseInsensitiveRoles()
        {
            var entries = new List<DialogueEntry>
            {
                new DialogueEntry("User", "Hello"),
                new DialogueEntry("ASSISTANT", "Hi there"),
            };

            List<DialogueEntry> result = NPCHistoryStore.NormalizeForChatTemplate(entries, out int dropped);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Role, Is.EqualTo("user"));
            Assert.That(result[1].Role, Is.EqualTo("assistant"));
            Assert.That(dropped, Is.Zero);
        }

        [Test]
        public void Normalize_HandlesNullInput()
        {
            List<DialogueEntry> result = NPCHistoryStore.NormalizeForChatTemplate(null, out int dropped);
            Assert.That(result, Is.Empty);
            Assert.That(dropped, Is.Zero);
        }

        [Test]
        public void GetFullPath_UsesDefaultForEmptyOrWhitespace()
        {
            string result = NPCHistoryStore.GetFullPath("");
            Assert.That(result, Does.EndWith("NPCDialogue/default.json"));

            result = NPCHistoryStore.GetFullPath("   ");
            Assert.That(result, Does.EndWith("NPCDialogue/default.json"));
        }

        [Test]
        public void GetFullPath_NormalizesBackslashes()
        {
            string result = NPCHistoryStore.GetFullPath("NPC\\Dialogue\\test.json");
            Assert.That(result, Does.Contain("NPC/Dialogue/test.json"));
            Assert.That(result, Does.Not.Contain("\\"));
        }

        [Test]
        public void Delete_ExistingFile_Succeeds()
        {
            string slug = $"todelete{Guid.NewGuid():N}";
            string relativePath = $"NPCDialogue/{slug}.json";
            string fullPath = NPCHistoryStore.GetFullPath(relativePath);

            try
            {
                var entries = new List<DialogueEntry>
                {
                    new DialogueEntry("user", "Hello"),
                    new DialogueEntry("assistant", "Goodbye")
                };
                NPCHistoryStore.Save(relativePath, entries);
                Assert.That(File.Exists(fullPath), Is.True);

                NPCHistoryStore.Delete(relativePath);
                Assert.That(File.Exists(fullPath), Is.False);
            }
            finally
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
        }

        [Test]
        public void Delete_NonExistentFile_DoesNotThrow()
        {
            string relativePath = $"NPCDialogue/nevercreated{Guid.NewGuid():N}.json";
            Assert.DoesNotThrow(() => NPCHistoryStore.Delete(relativePath));
        }

        [Test]
        public void Save_CreatesIntermediateDirectories()
        {
            string slug = $"nested{Guid.NewGuid():N}";
            string relativePath = $"NPCDialogue/SubDir/{slug}.json";
            string fullPath = NPCHistoryStore.GetFullPath(relativePath);

            try
            {
                var entries = new List<DialogueEntry>
                {
                    new DialogueEntry("user", "Hello")
                };

                NPCHistoryStore.Save(relativePath, entries);
                Assert.That(File.Exists(fullPath), Is.True);
            }
            finally
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
                string nestedDir = Path.GetDirectoryName(fullPath);
                if (Directory.Exists(nestedDir)) Directory.Delete(nestedDir);
            }
        }
    }
}
