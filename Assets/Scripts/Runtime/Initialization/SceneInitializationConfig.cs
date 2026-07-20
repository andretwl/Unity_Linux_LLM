using System;
using System.Collections.Generic;
using EditorAttributes;
using UnityEngine;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Configuration asset for the scene initialization pipeline.
    /// Replaces 6 individual boolean flags with a single, testable, versionable asset.
    ///
    /// Create via: Assets > Create > NPC > Scene Initialization Config
    /// </summary>
    [CreateAssetMenu(menuName = "NPC/Scene Initialization Config", fileName = "SceneInitializationConfig")]
    public sealed class SceneInitializationConfig : ScriptableObject
    {
        /// <summary>Per-phase configuration entry.</summary>
        [Serializable]
        public sealed class PhaseConfig
        {
            [Tooltip("Which phase this config applies to.")]
            public NPCSceneInitializationPhase Phase;

            [Tooltip("Whether this phase should run during initialization.")]
            public bool Enabled = true;

            [Tooltip("Maximum seconds before this phase is considered timed out.")]
            [Clamp(1f, 300f)]
            public float TimeoutSeconds = 30f;
        }

        // ── Inspector Fields ──

        [Header("Phase Configuration")]
        [HelpBox("Enable/disable individual phases and set per-phase timeouts. Phases not listed here default to enabled with 30s timeout.", MessageMode.Log)]
        [SerializeField]
        [Tooltip("Per-phase configuration. Phases not listed here default to enabled with 30s timeout.")]
        List<PhaseConfig> _phases = new List<PhaseConfig>();

        [Header("Pipeline Settings")]
        [SerializeField]
        [Tooltip("When true, Phases 1-2 run immediately and 3-8 defer until ContinueInitializationAsync() (WebGL memory-smart start).")]
        bool _deferredOnWebGL = true;

        [SerializeField]
        [Tooltip("Maximum total seconds for the entire pipeline before abort.")]
        [Clamp(10f, 600f)]
        float _pipelineTimeoutSeconds = 120f;

        // ── Public API ──

        /// <summary>Whether to defer Phases 3-8 on WebGL platforms.</summary>
        public bool DeferredOnWebGL => _deferredOnWebGL;

        /// <summary>Maximum total pipeline duration in seconds.</summary>
        public float PipelineTimeoutSeconds => _pipelineTimeoutSeconds;

        /// <summary>Read-only list of phase configurations.</summary>
        public IReadOnlyList<PhaseConfig> Phases => _phases;

        /// <summary>
        /// Get the config for a specific phase. Returns a default config
        /// (enabled, 30s timeout) if the phase is not explicitly configured.
        /// </summary>
        public PhaseConfig GetConfig(NPCSceneInitializationPhase phase)
        {
            for (int i = 0; i < _phases.Count; i++)
            {
                if (_phases[i].Phase == phase)
                    return _phases[i];
            }

            // Return default config
            return new PhaseConfig
            {
                Phase = phase,
                Enabled = true,
                TimeoutSeconds = 30f
            };
        }

        /// <summary>Convenience: check if a phase is enabled.</summary>
        public bool IsPhaseEnabled(NPCSceneInitializationPhase phase)
            => GetConfig(phase).Enabled;

        /// <summary>Convenience: get timeout for a phase.</summary>
        public float GetPhaseTimeout(NPCSceneInitializationPhase phase)
            => GetConfig(phase).TimeoutSeconds;

        // ── Inspector Helpers ──

#if UNITY_EDITOR
        /// <summary>
        /// Populate with all phases at default settings.
        /// Called from Inspector button or context menu.
        /// </summary>
        [Button("Reset to Defaults")]
        void ResetToDefaults()
        {
            _phases.Clear();
            foreach (NPCSceneInitializationPhase phase in Enum.GetValues(typeof(NPCSceneInitializationPhase)))
            {
                _phases.Add(new PhaseConfig
                {
                    Phase = phase,
                    Enabled = true,
                    TimeoutSeconds = 30f
                });
            }

            _deferredOnWebGL = true;
            _pipelineTimeoutSeconds = 120f;

            Debug.Log($"[SceneInitializationConfig] Reset to defaults — {_phases.Count} phases configured.");
        }
#endif
    }
}
