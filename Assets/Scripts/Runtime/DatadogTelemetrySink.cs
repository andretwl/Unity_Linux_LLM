using System;
using System.Collections.Generic;
using NPCSystem.Monitoring;
using NPCSystem.Monitoring.Datadog;


using NPCSystem.Dialogue.Core;
using NPCSystem.Network.Core;
using NPCSystem.Character.Player;
using NPCSystem.Auth;
using NPCSystem.Items;
using NPCSystem.LocalAI;
using NPCSystem.Initialization;
using NPCSystem.Character.NPC;
using NPCSystem.Dialogue.Session;
using NPCSystem.Dialogue.UI;
using NPCSystem.Dialogue.RAG;
using NPCSystem.Dialogue.Persistence;
namespace NPCSystem
{
    /// <summary>
    /// Bridges <see cref="TelemetryRouter"/> events into the existing
    /// <see cref="DatadogMetricsService"/> and <see cref="DatadogTracer"/>.
    ///
    /// Timed events → DatadogMetricsService.Timer() with metric name derived from category+source.
    /// Point events with "start" status → ignore (DatadogTracer handles scopes via NPCFlowScope callback).
    ///
    /// Register during game initialization:
    /// <code>
    /// TelemetryRouter.Instance.Register(new DatadogTelemetrySink());
    /// </code>
    /// </summary>
    public sealed class DatadogTelemetrySink : ITelemetrySink
    {
        public string DisplayName => "Datadog";

        /// <summary>
        /// Optional prefix for all Datadog metric names (default "npc").
        /// </summary>
        readonly string _metricPrefix;

        public DatadogTelemetrySink(string metricPrefix = "npc")
        {
            _metricPrefix = string.IsNullOrWhiteSpace(metricPrefix) ? "npc" : metricPrefix.Trim().TrimEnd('.');
        }

        public void Emit(in TelemetryEvent evt)
        {
            // Skip "start" events — they are metadata for PerformanceTracker scopes,
            // not meaningful Datadog metrics on their own.
            if (string.Equals(evt.Status, "start", StringComparison.OrdinalIgnoreCase))
                return;

            string metricName = $"{_metricPrefix}.{evt.Category}.{evt.Source}";

            // Build tags from TelemetryEvent fields
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(evt.Status))
                tags.Add($"status:{evt.Status}");
            if (!string.IsNullOrEmpty(evt.RequestId))
                tags.Add($"request:{evt.RequestId}");

            // Forward additional tags from the event's Tags dictionary
            if (evt.Tags != null)
            {
                foreach (KeyValuePair<string, object> kvp in evt.Tags)
                {
                    if (kvp.Value != null)
                        tags.Add($"{kvp.Key}:{kvp.Value}");
                }
            }

            string[] tagArray = tags.Count > 0 ? tags.ToArray() : null;

            // Timed events → Datadog timer metric
            if (evt.DurationMs > 0)
            {
                DatadogMetricsService.Timer(metricName, evt.DurationMs, tags: tagArray);
            }

            // Error/fallback events → increment error counter
            if (string.Equals(evt.Status, "error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(evt.Status, "fallback", StringComparison.OrdinalIgnoreCase))
            {
                DatadogMetricsService.Increment($"{metricName}.count", tags: tagArray);
            }
        }
    }
}
