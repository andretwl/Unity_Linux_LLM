using NUnit.Framework;
using UnityEngine;

namespace NPCSystem.Tests
{
    /// <summary>
    /// Tests for NPCDialogueActionPlanner — pure keyword-matching logic
    /// that maps player messages to dialogue action plans.
    /// No external dependencies: this is the most testable class in the codebase.
    /// </summary>
    public class NPCDialogueActionPlannerTests
    {
        static GameObject CreatePlannerObject()
        {
            var go = new GameObject("ActionPlannerTest");
            go.AddComponent<NPCDialogueActionPlanner>();
            return go;
        }

        static NPCProfile CreateProfile(
            bool canGiveHints = true,
            bool canAccuse = true,
            bool canReveal = true
        )
        {
            return new NPCProfile
            {
                CanGivePuzzleHints = canGiveHints,
                CanAccuseSuspects = canAccuse,
                CanRevealSecrets = canReveal,
            };
        }

        // ── Empty / null message ───────────────────────────────────

        [Test]
        public void Plan_EmptyMessage_ReturnsNone()
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan("", CreateProfile());
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.None));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Plan_NullProfile_DoesNotThrow()
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                Assert.DoesNotThrow(() => planner.Plan("hello world", null));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Notes ──────────────────────────────────────────────────

        [Test]
        [TestCase("show my notes")]
        [TestCase("notes")]
        [TestCase("notebook")]
        [TestCase("open journal")]
        [TestCase("check notebook please")]
        [TestCase("what are my NOTES")]
        public void Plan_NotesKeywords_ReturnsShowNotes(string message)
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan(message, CreateProfile());
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.ShowNotes));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Map ────────────────────────────────────────────────────

        [Test]
        [TestCase("where am i")]
        [TestCase("map")]
        [TestCase("show the map")]
        [TestCase("what room is this")]
        [TestCase("location")]
        public void Plan_MapKeywords_ReturnsShowMap(string message)
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan(message, CreateProfile());
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.ShowMap));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Solve / accuse ─────────────────────────────────────────

        [Test]
        [TestCase("solve this mystery")]
        [TestCase("accuse the butler")]
        [TestCase("who did it")]
        [TestCase("i want the solution")]
        [TestCase("tell me the answer")]
        public void Plan_SolveKeywords_ReturnsShowSolve(string message)
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan(message, CreateProfile());
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.ShowSolve));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Help ───────────────────────────────────────────────────

        [Test]
        [TestCase("help")]
        [TestCase("how do i play")]
        [TestCase("what can i do")]
        [TestCase("help me")]
        [TestCase("i need help")]
        public void Plan_HelpKeywords_ReturnsShowHelp(string message)
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan(message, CreateProfile());
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.ShowHelp));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Puzzle hints ───────────────────────────────────────────

        [Test]
        [TestCase("i'm stuck")]
        [TestCase("give me a hint")]
        [TestCase("what should i do next")]
        [TestCase("clue")]
        [TestCase("i don't know what to do")]
        [TestCase("hint")]
        public void Plan_HintKeywords_ReturnsPuzzleHint(string message)
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan(message, CreateProfile(canGiveHints: true));
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.PuzzleHint));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Plan_HintKeywords_HintsDisabled_ReturnsNone()
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                NPCProfile profile = CreateProfile(canGiveHints: false);
                var plan = planner.Plan("i'm stuck", profile);
                // Should NOT match PuzzleHint when profile disables hints
                Assert.That(plan.ActionType, Is.Not.EqualTo(NPCDialogueActionType.PuzzleHint));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Evidence / memory ──────────────────────────────────────

        [Test]
        [TestCase("what do you remember")]
        [TestCase("evidence")]
        [TestCase("what clues do you have")]
        [TestCase("tell me what you know")]
        [TestCase("what did you see")]
        [TestCase("recall")]
        public void Plan_EvidenceKeywords_ReturnsRecallEvidence(string message)
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan(message, CreateProfile());
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.RecallEvidence));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Suspect press ──────────────────────────────────────────

        [Test]
        [TestCase("who is the killer")]
        [TestCase("i suspect the maid")]
        [TestCase("culprit")]
        [TestCase("who could have done this")]
        public void Plan_SuspectKeywords_ReturnsPressSuspect(string message)
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan(message, CreateProfile(canAccuse: true));
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.PressSuspect));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Plan_SuspectKeywords_AccuseDisabled_ReturnsNone()
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan("who did it", CreateProfile(canAccuse: false));
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.None));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── Priority / precedence ──────────────────────────────────

        [Test]
        public void Plan_NotesTakesPriorityOverHelp()
        {
            // "help" is in HelpKeywords, "notes" is in NotesKeywords.
            // Plan processes NotesKeywords first.
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan("help me with my notes", CreateProfile());
                // NotesKeywords checked first → ShowNotes
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.ShowNotes));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Plan_NormalConversation_ReturnsNone()
        {
            var go = CreatePlannerObject();
            try
            {
                var planner = go.GetComponent<NPCDialogueActionPlanner>();
                var plan = planner.Plan("hello, how are you today?", CreateProfile());
                Assert.That(plan.ActionType, Is.EqualTo(NPCDialogueActionType.None));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── BuildPromptHint ────────────────────────────────────────

        [Test]
        public void BuildPromptHint_ActionPlan_ReturnsFormattedString()
        {
            var plan = new NPCDialogueActionPlan
            {
                ActionType = NPCDialogueActionType.ShowNotes,
                Reason = "test reason",
                ContextPrompt = "Guide player to notes.",
            };

            string hint = NPCDialogueActionPlanner.BuildPromptHint(plan);
            Assert.That(hint, Does.StartWith("Action guidance (ShowNotes):"));
            Assert.That(hint, Does.Contain("Guide player to notes."));
        }

        [Test]
        public void BuildPromptHint_NullPlan_ReturnsEmpty()
        {
            string hint = NPCDialogueActionPlanner.BuildPromptHint(null);
            Assert.That(hint, Is.EqualTo(string.Empty));
        }

        [Test]
        public void BuildPromptHint_NoneAction_ReturnsEmpty()
        {
            var plan = NPCDialogueActionPlan.None();
            string hint = NPCDialogueActionPlanner.BuildPromptHint(plan);
            Assert.That(hint, Is.EqualTo(string.Empty));
        }
    }
}
