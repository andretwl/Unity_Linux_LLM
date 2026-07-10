using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Lightweight snapshot of player-specific context used to enrich
    /// NPC dialogue prompts.  This is the runtime data model loaded
    /// from Supabase (or NPCEvidenceState when offline) before each
    /// dialogue session.
    /// </summary>
    [Serializable]
    public struct PlayerDialogueContext
    {
        [SerializeField]
        string _playerName;

        [SerializeField]
        string _playerId;

        // ── Per-NPC relationship (current NPC) ─────────────────

        [SerializeField]
        int _trustScore;

        [SerializeField]
        string _currentMood;

        [SerializeField]
        int _dialogueCount;

        // ── Investigation state ─────────────────────────────────

        [SerializeField]
        string[] _knownClues;

        [SerializeField]
        string[] _inventory;

        [SerializeField]
        string[] _visitedLocations;

        // ── Metadata ────────────────────────────────────────────

        [SerializeField]
        bool _loadedFromServer;

        // ── Constructor ─────────────────────────────────────────

        public PlayerDialogueContext(
            string playerName,
            string playerId,
            int trustScore = 50,
            string currentMood = "neutral",
            int dialogueCount = 0,
            IEnumerable<string> knownClues = null,
            IEnumerable<string> inventory = null,
            IEnumerable<string> visitedLocations = null,
            bool loadedFromServer = false
        )
        {
            _playerName = playerName ?? string.Empty;
            _playerId = playerId ?? string.Empty;
            _trustScore = Mathf.Clamp(trustScore, 0, 100);
            _currentMood = currentMood ?? "neutral";
            _dialogueCount = Mathf.Max(0, dialogueCount);
            _knownClues = knownClues?.ToArray() ?? Array.Empty<string>();
            _inventory = inventory?.ToArray() ?? Array.Empty<string>();
            _visitedLocations = visitedLocations?.ToArray() ?? Array.Empty<string>();
            _loadedFromServer = loadedFromServer;
        }

        // ── Properties ──────────────────────────────────────────

        public readonly string PlayerName => _playerName;
        public readonly string PlayerId => _playerId;
        public readonly int TrustScore => _trustScore;
        public readonly string CurrentMood => _currentMood;
        public readonly int DialogueCount => _dialogueCount;
        public readonly IReadOnlyList<string> KnownClues => Array.AsReadOnly(_knownClues ?? Array.Empty<string>());
        public readonly IReadOnlyList<string> Inventory => Array.AsReadOnly(_inventory ?? Array.Empty<string>());
        public readonly IReadOnlyList<string> VisitedLocations => Array.AsReadOnly(_visitedLocations ?? Array.Empty<string>());
        public readonly bool LoadedFromServer => _loadedFromServer;

        // ── Query helpers ───────────────────────────────────────

        public readonly string TrustLabel
        {
            get
            {
                if (_trustScore >= 80) return "trusting";
                if (_trustScore >= 60) return "cooperative";
                if (_trustScore >= 40) return "cautious";
                if (_trustScore >= 20) return "guarded";
                return "hostile";
            }
        }

        public readonly bool HasContext =>
            !string.IsNullOrWhiteSpace(_playerName)
            || _knownClues.Length > 0
            || _inventory.Length > 0
            || _visitedLocations.Length > 0;

        // ── Prompt builder ──────────────────────────────────────

        /// <summary>
        /// Builds a plain-text context block that can be appended to
        /// an NPC's system prompt.
        /// </summary>
        public readonly string BuildPromptBlock(string npcSlug)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(_playerName))
                lines.Add($"The player you are speaking with is named '{_playerName}'.");

            // NPC-specific relationship
            if (!string.IsNullOrWhiteSpace(npcSlug))
            {
                lines.Add(
                    $"Your current relationship with {_playerName}: trust level is '{TrustLabel}' ({_trustScore}/100), "
                    + $"current mood toward the player is '{_currentMood}', "
                    + $"and you have had {_dialogueCount} previous exchanges."
                );

                if (_trustScore < 40)
                    lines.Add(
                        "This player has not yet earned your full trust. "
                        + "Be somewhat guarded in what you reveal."
                    );
                else if (_trustScore >= 80)
                    lines.Add(
                        "You trust this player deeply and are inclined to share "
                        + "secrets or offer help freely."
                    );
            }

            // Investigation state
            if (_knownClues.Length > 0)
            {
                lines.Add(
                    $"Clues this player has already discovered: {string.Join("; ", _knownClues)}."
                );
            }

            if (_inventory.Length > 0)
            {
                lines.Add(
                    $"Items this player is carrying: {string.Join("; ", _inventory)}."
                );
            }

            if (_visitedLocations.Length > 0)
            {
                lines.Add(
                    $"Locations this player has visited: {string.Join("; ", _visitedLocations)}."
                );
            }

            if (_loadedFromServer)
                lines.Add(
                    "(Player context loaded from server — facts are persistent across sessions.)"
                );

            lines.Add(
                "Use the above context naturally in conversation. Do not list it explicitly; "
                + "weave it into your responses as the character would."
            );

            return string.Join("\n", lines);
        }

        // ── Empty / Default ──────────────────────────────────────

        public static readonly PlayerDialogueContext Empty = new PlayerDialogueContext(null, null);

        public static PlayerDialogueContext FromLocalState(
            string playerName,
            string playerId,
            string npcSlug,
            NPCEvidenceState evidence
        )
        {
            if (evidence == null)
                return new PlayerDialogueContext(playerName, playerId);

            return new PlayerDialogueContext(
                playerName: playerName,
                playerId: playerId,
                trustScore: string.IsNullOrWhiteSpace(npcSlug)
                    ? 50
                    : evidence.GetNpcTrust(npcSlug),
                currentMood: string.IsNullOrWhiteSpace(npcSlug)
                    ? "neutral"
                    : evidence.GetNpcMood(npcSlug),
                dialogueCount: 0, // counted server-side if needed
                knownClues: evidence.discoveredClues?.Select(c => c.clueText),
                inventory: evidence.obtainedItems,
                visitedLocations: evidence.visitedLocations,
                loadedFromServer: false
            );
        }
    }
}
