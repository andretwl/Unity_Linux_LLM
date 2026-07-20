using System;
using System.Collections.Generic;
using NPCSystem.Monitoring;
using NPCSystem.Monitoring.Datadog;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Standardized telemetry helper for the initialization pipeline.
    /// Emits structured events through TelemetryRouter (file, Console Pro, Datadog)
    /// and DatadogMetricsService (counters, timers for dashboards).
    ///
    /// Every phase uses this to guarantee consistent observability:
    ///   - Same category ("init") for filtering
    ///   - Same tags (correlationId, phase, duration) for correlation
    ///   - Same Datadog metrics for dashboards
    /// </summary>
    public static class InitTelemetry
    {
        const string Category = "init";
        const string Source = "InitPipeline";

        // ── Phase Events ──────────────────────────────────────────────

        /// <summary>Emitted before a phase begins execution.</summary>
        public static void PhaseStarted(
            string correlationId,
            NPCSceneInitializationPhase phase,
            int index,
            int total)
        {
            TelemetryRouter.Point(
                correlationId,
                Source,
                Category,
                "start",
                $"Phase {index + 1}/{total} {phase} started.",
                new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["phase"] = phase.ToString(),
                    ["phaseIndex"] = index,
                    ["totalPhases"] = total
                });
        }

        /// <summary>Emitted after a phase completes successfully.</summary>
        public static void PhaseCompleted(
            string correlationId,
            NPCSceneInitializationPhase phase,
            TimeSpan duration,
            Dictionary<string, object> data = null)
        {
            var tags = new Dictionary<string, object>
            {
                ["correlationId"] = correlationId,
                ["phase"] = phase.ToString()
            };

            if (data != null)
            {
                foreach (var kvp in data)
                    tags[kvp.Key] = kvp.Value;
            }

            TelemetryRouter.Timed(
                correlationId,
                Source,
                Category,
                "success",
                (long)duration.TotalMilliseconds,
                $"Phase {phase} completed in {duration.TotalMilliseconds:F0}ms.",
                tags);

            DatadogMetricsService.Timer(
                "init.phase.duration",
                duration.TotalMilliseconds,
                tags: new[] { $"phase:{phase.ToString().ToLowerInvariant()}" });
        }

        /// <summary>Emitted when a phase fails with an exception.</summary>
        public static void PhaseFailed(
            string correlationId,
            NPCSceneInitializationPhase phase,
            Exception ex,
            TimeSpan duration)
        {
            TelemetryRouter.Timed(
                correlationId,
                Source,
                Category,
                "error",
                (long)duration.TotalMilliseconds,
                $"Phase {phase} failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["phase"] = phase.ToString(),
                    ["exception"] = ex.GetType().Name,
                    ["exceptionMessage"] = ex.Message
                });

            DatadogMetricsService.Increment(
                "init.phase.failed",
                tags: new[]
                {
                    $"phase:{phase.ToString().ToLowerInvariant()}",
                    $"error:{ex.GetType().Name}"
                });
        }

        /// <summary>Emitted when a phase is skipped (disabled or dependency failed).</summary>
        public static void PhaseSkipped(
            string correlationId,
            NPCSceneInitializationPhase phase,
            string reason)
        {
            TelemetryRouter.Point(
                correlationId,
                Source,
                Category,
                "warning",
                $"Phase {phase} skipped: {reason}",
                new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["phase"] = phase.ToString(),
                    ["reason"] = reason
                });

            DatadogMetricsService.Increment(
                "init.phase.skipped",
                tags: new[] { $"phase:{phase.ToString().ToLowerInvariant()}" });
        }

        // ── Pipeline Events ───────────────────────────────────────────

        /// <summary>Emitted once before the first phase runs.</summary>
        public static void PipelineStarted(
            string correlationId,
            bool isWebGL,
            int phaseCount)
        {
            TelemetryRouter.Point(
                correlationId,
                Source,
                Category,
                "start",
                $"Initialization pipeline started ({phaseCount} phases, WebGL={isWebGL}).",
                new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["isWebGL"] = isWebGL,
                    ["phaseCount"] = phaseCount
                });

            DatadogMetricsService.Increment("init.pipeline.started");
        }

        /// <summary>Emitted after the last phase completes.</summary>
        public static void PipelineCompleted(
            string correlationId,
            TimeSpan totalDuration,
            int completed,
            int skipped)
        {
            TelemetryRouter.Timed(
                correlationId,
                Source,
                Category,
                "success",
                (long)totalDuration.TotalMilliseconds,
                $"Pipeline completed — {completed} phases OK, {skipped} skipped, {totalDuration.TotalMilliseconds:F0}ms total.",
                new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["phasesCompleted"] = completed,
                    ["phasesSkipped"] = skipped
                });

            DatadogMetricsService.Timer("init.pipeline.duration", totalDuration.TotalMilliseconds);
            DatadogMetricsService.Gauge("init.pipeline.phases_completed", completed);
            DatadogMetricsService.Gauge("init.pipeline.phases_skipped", skipped);

            if (skipped > 0)
                DatadogMetricsService.Increment("init.pipeline.partial", tags: new[] { $"skipped:{skipped}" });
        }

        /// <summary>Emitted when the pipeline aborts due to a phase failure.</summary>
        public static void PipelineFailed(
            string correlationId,
            NPCSceneInitializationPhase failedPhase,
            Exception ex,
            TimeSpan totalDuration)
        {
            TelemetryRouter.Timed(
                correlationId,
                Source,
                Category,
                "error",
                (long)totalDuration.TotalMilliseconds,
                $"Pipeline FAILED at {failedPhase}: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["failedPhase"] = failedPhase.ToString(),
                    ["exception"] = ex.GetType().Name,
                    ["exceptionMessage"] = ex.Message
                });

            DatadogMetricsService.Increment(
                "init.pipeline.failed",
                tags: new[]
                {
                    $"phase:{failedPhase.ToString().ToLowerInvariant()}",
                    $"error:{ex.GetType().Name}"
                });
        }
    }
}
