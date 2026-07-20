using System;
using System.Collections.Generic;
using System.Text;
using NPCSystem.Monitoring;
using UnityEngine;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Pipeline execution state.
    /// </summary>
    public enum InitializationState
    {
        NotStarted,
        Running,
        Completed,
        Failed
    }

    /// <summary>
    /// Result of a single phase execution, carrying timing, status, and diagnostic data.
    /// </summary>
    public sealed class PhaseResult
    {
        public NPCSceneInitializationPhase Phase { get; }
        public bool Success { get; }
        public bool Skipped { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public TimeSpan Duration { get; }
        public Dictionary<string, object> Data { get; }

        public PhaseResult(
            NPCSceneInitializationPhase phase,
            bool success,
            bool skipped,
            string message,
            Exception exception,
            TimeSpan duration,
            Dictionary<string, object> data)
        {
            Phase = phase;
            Success = success;
            Skipped = skipped;
            Message = message;
            Exception = exception;
            Duration = duration;
            Data = data ?? new Dictionary<string, object>();
        }

        public static PhaseResult Succeeded(NPCSceneInitializationPhase phase, TimeSpan duration, string message = null, Dictionary<string, object> data = null)
            => new PhaseResult(phase, success: true, skipped: false, message, null, duration, data);

        public static PhaseResult SkippedResult(NPCSceneInitializationPhase phase, string reason)
            => new PhaseResult(phase, success: false, skipped: true, reason, null, TimeSpan.Zero, null);

        public static PhaseResult Failed(NPCSceneInitializationPhase phase, TimeSpan duration, Exception ex, string message = null)
            => new PhaseResult(phase, success: false, skipped: false, message ?? ex?.Message, ex, duration, null);
    }

    /// <summary>
    /// Shared state bus for the initialization pipeline.
    /// Carries references, timing, correlation ID, and per-phase results
    /// so every component can observe exactly where dataflow stops.
    /// </summary>
    public sealed class InitializationContext
    {
        /// <summary>Unique ID for this pipeline run — correlates logs, spans, and metrics.</summary>
        public string CorrelationId { get; } = Guid.NewGuid().ToString("N")[..12];

        // ── Component References (set by controller before pipeline starts) ──

        public NPCFlowLogger Logger { get; set; }
        public NPCSystem.Network.Core.NPCNetworkBootstrap NetworkBootstrap { get; set; }
        public NPCSystem.Dialogue.Core.NPCDialogueManager DialogueManager { get; set; }
        public NPCBackendReadinessService BackendReadiness { get; set; }
        public NPCSystem.Network.Bridges.NPCDialogueNetworkBridge NetworkBridge { get; set; }
        public NPCSystem.Dialogue.Core.NPCDialogueSmokeValidator SmokeValidator { get; set; }

        // ── Pipeline State ──

        public InitializationState State { get; private set; } = InitializationState.NotStarted;
        public NPCSceneInitializationPhase? CurrentPhase { get; private set; }

        readonly Dictionary<NPCSceneInitializationPhase, PhaseResult> _results = new Dictionary<NPCSceneInitializationPhase, PhaseResult>();
        public IReadOnlyDictionary<NPCSceneInitializationPhase, PhaseResult> Results => _results;

        /// <summary>True when Phases 1-2 ran but 3-8 are deferred (WebGL path).</summary>
        public bool IsDeferred { get; set; }

        /// <summary>Configuration asset controlling phase enablement and timeouts.</summary>
        public SceneInitializationConfig Config { get; set; }

        // ── Timing ──

        public DateTime PipelineStartTime { get; } = DateTime.UtcNow;
        public DateTime? PipelineEndTime { get; private set; }
        public TimeSpan Elapsed => (PipelineEndTime ?? DateTime.UtcNow) - PipelineStartTime;

        // ── Mutators (called by orchestrator) ──

        public void SetState(InitializationState state)
        {
            State = state;
            if (state == InitializationState.Completed || state == InitializationState.Failed)
                PipelineEndTime = DateTime.UtcNow;
        }

        public void SetCurrentPhase(NPCSceneInitializationPhase? phase)
        {
            CurrentPhase = phase;
        }

        public void RecordPhaseResult(PhaseResult result)
        {
            _results[result.Phase] = result;
        }

        // ── Diagnostics ──

        /// <summary>
        /// Human-readable status summary for UI / debugging.
        /// Example: "Phase 5/8 (Backend) — 2.3s elapsed — Phases 1-4 OK"
        /// </summary>
        public string GetStatusSummary()
        {
            var sb = new StringBuilder();
            int total = Enum.GetValues(typeof(NPCSceneInitializationPhase)).Length;
            int current = CurrentPhase.HasValue ? (int)CurrentPhase.Value : total;
            string elapsed = $"{Elapsed.TotalSeconds:F1}s";

            sb.Append(State switch
            {
                InitializationState.NotStarted => "Pipeline not started",
                InitializationState.Running => $"Phase {current + 1}/{total} ({CurrentPhase})",
                InitializationState.Completed => $"Pipeline completed ({_results.Count}/{total} phases)",
                InitializationState.Failed => $"Pipeline FAILED at {CurrentPhase}",
                _ => "Unknown"
            });

            sb.Append($" — {elapsed} elapsed");

            if (_results.Count > 0)
            {
                int ok = 0, failed = 0, skipped = 0;
                foreach (var r in _results.Values)
                {
                    if (r.Success) ok++;
                    else if (r.Skipped) skipped++;
                    else failed++;
                }

                sb.Append($" — {ok} OK");
                if (failed > 0) sb.Append($", {failed} FAILED");
                if (skipped > 0) sb.Append($", {skipped} skipped");
            }

            return sb.ToString();
        }

        /// <summary>Get the config for a phase, or null if no config asset is set.</summary>
        public SceneInitializationConfig.PhaseConfig GetPhaseConfig(NPCSceneInitializationPhase phase)
            => Config?.GetConfig(phase);

        /// <summary>Check if a specific phase completed successfully.</summary>
        public bool IsPhaseCompleted(NPCSceneInitializationPhase phase)
            => _results.TryGetValue(phase, out var r) && r.Success;

        /// <summary>Check if a phase failed.</summary>
        public bool IsPhaseFailed(NPCSceneInitializationPhase phase)
            => _results.TryGetValue(phase, out var r) && !r.Success && !r.Skipped;

        /// <summary>Get the result for a specific phase, or null if not yet run.</summary>
        public PhaseResult GetResult(NPCSceneInitializationPhase phase)
            => _results.TryGetValue(phase, out var r) ? r : null;

        /// <summary>Log the full pipeline summary to NPCFlowLogger.</summary>
        public void LogSummary()
        {
            if (Logger == null) return;

            string summary = GetStatusSummary();
            var status = State == InitializationState.Completed
                ? NPCFlowStatus.Success
                : NPCFlowStatus.Error;

            var level = State == InitializationState.Completed
                ? NPCFlowLogLevel.Info
                : NPCFlowLogLevel.Error;

            var data = new Dictionary<string, object>
            {
                ["correlationId"] = CorrelationId,
                ["state"] = State.ToString(),
                ["elapsedMs"] = Elapsed.TotalMilliseconds,
                ["phasesCompleted"] = _results.Count
            };

            // Include per-phase durations
            foreach (var kvp in _results)
                data[$"duration.{kvp.Key.ToString().ToLowerInvariant()}"] = kvp.Value.Duration.TotalMilliseconds;

            Logger.Log(NPCFlowStage.SceneBootstrap, status, level, summary,
                source: nameof(InitializationContext), data: data);
        }
    }
}
