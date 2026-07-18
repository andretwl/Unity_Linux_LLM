using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NPCSystem
{
    public enum NPCDialogueActionType
    {
        None,
        PuzzleHint,
        ShowNotes,
        ShowMap,
        ShowSolve,
        ShowHelp,
        PressSuspect,
        RecallEvidence,
    }

    [Serializable]
    public sealed class NPCDialogueActionPlan
    {
        public NPCDialogueActionType ActionType = NPCDialogueActionType.None;
        public string Reason = string.Empty;
        public string ContextPrompt = string.Empty;

        public static NPCDialogueActionPlan None(string reason = "No action planning rule matched.")
        {
            return new NPCDialogueActionPlan
            {
                ActionType = NPCDialogueActionType.None,
                Reason = reason,
                ContextPrompt = string.Empty,
            };
        }
    }

    public class NPCDialogueActionPlanner : MonoBehaviour
    {
        public enum TrustThreshold
        {
            Hostile = 0,      // 0-19
            Guarded = 20,     // 20-39
            Cautious = 40,    // 40-59
            Cooperative = 60, // 60-79
            Trusting = 80,    // 80-100
        }

        static readonly string[] HintKeywords =
        {
            "hint",
            "stuck",
            "investigate",
            "clue",
            "what should i do",
            "next",
        };
        static readonly string[] NotesKeywords = { "notes", "notebook", "journal" };
        static readonly string[] MapKeywords = { "map", "where", "room", "location" };
        static readonly string[] SolveKeywords = { "solve", "accuse", "who did it", "solution" };
        static readonly string[] HelpKeywords = { "help", "how do i", "what can i do" };
        static readonly string[] EvidenceKeywords =
        {
            "remember",
            "evidence",
            "suspect",
            "proof",
            "alibi",
        };

        public NPCDialogueActionPlan Plan(string playerMessage, NPCProfile profile, int trustScore = 50)
        {
            if (string.IsNullOrWhiteSpace(playerMessage))
                return NPCDialogueActionPlan.None("Player message was empty.");

            string lower = playerMessage.Trim().ToLowerInvariant();
            TrustThreshold trust = ClassifyTrust(trustScore);

            // ── Actions restricted by trust level ────────────
            // Hostile NPCs don't offer anything useful
            if (trust == TrustThreshold.Hostile)
            {
                if (Matches(lower, HelpKeywords) || Matches(lower, HintKeywords))
                    return NPCDialogueActionPlan.None(
                        "NPC is hostile — will not help the player."
                    );
            }

            // Guarded NPCs don't share hints or evidence
            if (trust <= TrustThreshold.Guarded)
            {
                if (Matches(lower, HintKeywords) || Matches(lower, EvidenceKeywords))
                    return NPCDialogueActionPlan.None(
                        "NPC is guarded — will not share hints or evidence freely."
                    );
            }
            if (Matches(lower, NotesKeywords))
            {
                return BuildPlan(
                    NPCDialogueActionType.ShowNotes,
                    "Player referenced notes/notebook.",
                    "If useful, guide the player toward their notes and summarize which written clues matter most right now."
                );
            }

            if (Matches(lower, MapKeywords))
            {
                return BuildPlan(
                    NPCDialogueActionType.ShowMap,
                    "Player referenced map/location.",
                    "If useful, anchor the reply in mansion geography, rooms, or where the player should look next."
                );
            }

            if (Matches(lower, SolveKeywords))
            {
                return BuildPlan(
                    NPCDialogueActionType.ShowSolve,
                    "Player asked about solving/accusing.",
                    "If useful, frame the response around what evidence is still missing before a final accusation."
                );
            }

            if (Matches(lower, HelpKeywords))
            {
                return BuildPlan(
                    NPCDialogueActionType.ShowHelp,
                    "Player asked for help.",
                    "If useful, explain available investigation options in-character without breaking the mystery tone."
                );
            }

            if (Matches(lower, HintKeywords) && (profile == null || profile.CanGivePuzzleHints))
            {
                return BuildPlan(
                    NPCDialogueActionType.PuzzleHint,
                    "Player seems stuck and this NPC may give hints.",
                    "Offer one subtle investigation hint. Prefer nudging toward evidence, suspect behavior, or room-specific clues instead of revealing the answer outright."
                );
            }

            if (Matches(lower, EvidenceKeywords))
            {
                return BuildPlan(
                    NPCDialogueActionType.RecallEvidence,
                    "Player asked about memory/evidence/suspects.",
                    "Summarize the strongest known clues, uncertainties, and contradictions from this NPC's perspective."
                );
            }

            if (
                profile != null
                && profile.CanAccuseSuspects
                && (lower.Contains("who") || lower.Contains("suspect") || lower.Contains("culprit"))
            )
            {
                return BuildPlan(
                    NPCDialogueActionType.PressSuspect,
                    "Player asked for suspect judgment and NPC may accuse suspects.",
                    "If you name a suspect, explain the suspicion as a hypothesis grounded in clues rather than certainty."
                );
            }

            return NPCDialogueActionPlan.None();
        }

        public static string BuildPromptHint(NPCDialogueActionPlan plan)
        {
            if (
                plan == null
                || plan.ActionType == NPCDialogueActionType.None
                || string.IsNullOrWhiteSpace(plan.ContextPrompt)
            )
                return string.Empty;
            return $"Action guidance ({plan.ActionType}): {plan.ContextPrompt}";
        }

        static NPCDialogueActionPlan BuildPlan(
            NPCDialogueActionType actionType,
            string reason,
            string contextPrompt
        )
        {
            return new NPCDialogueActionPlan
            {
                ActionType = actionType,
                Reason = reason,
                ContextPrompt = contextPrompt,
            };
        }

        static bool Matches(string text, IEnumerable<string> keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword));
        }

        /// <summary>
        /// Classify a numeric trust score (0-100) into a threshold level.
        /// </summary>
        public static TrustThreshold ClassifyTrust(int trustScore)
        {
            if (trustScore >= 80)
                return TrustThreshold.Trusting;
            if (trustScore >= 60)
                return TrustThreshold.Cooperative;
            if (trustScore >= 40)
                return TrustThreshold.Cautious;
            if (trustScore >= 20)
                return TrustThreshold.Guarded;
            return TrustThreshold.Hostile;
        }
    }
}
