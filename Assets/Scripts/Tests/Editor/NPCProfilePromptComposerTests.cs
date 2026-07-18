using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    /// <summary>
    /// Tests for NPCProfilePromptComposer — verifies PromptVariables template
    /// substitution and prompt construction from profile fields.
    /// </summary>
    public class NPCProfilePromptComposerTests
    {
        [Test]
        public void BuildSystemPrompt_ResolvesAllTemplateVariables()
        {
            var profile = ScriptableObject.CreateInstance<NPCProfile>();
            profile.NpcSlug = "butler";
            profile.DisplayName = "Jeeves";
            profile.SystemPrompt =
                "You are a butler in a grand mansion. The player is named {playerName}. "
                + "Your trust score is {trustScore} ({trustLabel}). "
                + "Your mood is {mood}. "
                + "You've had {dialogueCount} prior conversations. "
                + "The current location is {currentLocation} and the time is {timeOfDay}.";

            var vars = new PromptVariables
            {
                playerName = "Detective Holmes",
                npcSlug = "butler",
                trustScore = 85,
                trustLabel = "trusting",
                mood = "friendly",
                dialogueCount = 7,
                currentLocation = "the library",
                timeOfDay = "late evening",
            };

            string result = NPCProfilePromptComposer.BuildSystemPrompt(profile, vars);

            Assert.That(result, Does.Contain("Detective Holmes"));
            Assert.That(result, Does.Contain("85"));
            Assert.That(result, Does.Contain("trusting"));
            Assert.That(result, Does.Contain("friendly"));
            Assert.That(result, Does.Contain("7"));
            Assert.That(result, Does.Contain("the library"));
            Assert.That(result, Does.Contain("late evening"));

            // The raw template tokens should NOT appear
            Assert.That(result, Does.Not.Contain("{playerName}"));
            Assert.That(result, Does.Not.Contain("{trustScore}"));
            Assert.That(result, Does.Not.Contain("{currentLocation}"));
        }

        [Test]
        public void BuildSystemPrompt_WithNullProfile_UsesFallback()
        {
            var vars = PromptVariables.Default;
            string result = NPCProfilePromptComposer.BuildSystemPrompt(null, vars);

            Assert.That(result, Is.Not.Null.Or.Empty);
            Assert.That(result, Does.StartWith("Core role:"));
            Assert.That(result, Does.Contain("helpful in-game NPC"));
        }

        [Test]
        public void BuildSystemPrompt_WithNullVariables_UsesDefaults()
        {
            var profile = ScriptableObject.CreateInstance<NPCProfile>();
            profile.NpcSlug = "butler";
            profile.DisplayName = "Jeeves";
            profile.SystemPrompt = "Hello {playerName}, trust is {trustScore}.";

            string result = NPCProfilePromptComposer.BuildSystemPrompt(profile, PromptVariables.Default);

            Assert.That(result, Does.Contain("Player"));
            Assert.That(result, Does.Contain("50"));
            Assert.That(result, Does.Not.Contain("{playerName}"));
        }

        [Test]
        public void BuildSystemPrompt_IncludesAllProfileSections()
        {
            var profile = ScriptableObject.CreateInstance<NPCProfile>();
            profile.NpcSlug = "maid";
            profile.DisplayName = "Eliza";
            profile.SystemPrompt = "You are a helpful maid.";
            profile.PersonalityBrief = "Polite and methodical";
            profile.SpeakingStyle = "Formal, uses 'sir' and 'madam'";
            profile.Boundaries = "Never discuss other guests";
            profile.CanRevealSecrets = false;

            string result = NPCProfilePromptComposer.BuildSystemPrompt(profile, PromptVariables.Default);

            Assert.That(result, Does.Contain("Core role:"));
            Assert.That(result, Does.Contain("You are a helpful maid."));
            Assert.That(result, Does.Contain("Personality brief:"));
            Assert.That(result, Does.Contain("Polite and methodical"));
            Assert.That(result, Does.Contain("Speaking style:"));
            Assert.That(result, Does.Contain("Formal"));
            Assert.That(result, Does.Contain("Boundaries:"));
            Assert.That(result, Does.Contain("other guests"));
            Assert.That(result, Does.Contain("Private knowledge"));
            Assert.That(result, Does.Contain("should not reveal it"));
        }

        [Test]
        public void BuildSystemPrompt_BehaviorSlidersArePresent()
        {
            var profile = ScriptableObject.CreateInstance<NPCProfile>();
            profile.NpcSlug = "butler";

            string result = NPCProfilePromptComposer.BuildSystemPrompt(profile, PromptVariables.Default);

            Assert.That(result, Does.Contain("Suspicion="));
            Assert.That(result, Does.Contain("Helpfulness="));
            Assert.That(result, Does.Contain("Sarcasm="));
        }
    }
}
