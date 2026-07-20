using System;
using NPCSystem.Auth;
using NPCSystem.Character.NPC;
using NPCSystem.Character.Player;
using NPCSystem.Dialogue.Core;
using NPCSystem.Dialogue.Persistence;
using NPCSystem.Dialogue.RAG;
using NPCSystem.Dialogue.Session;
using NPCSystem.Dialogue.UI;
using NPCSystem.Initialization;
using NPCSystem.Items;
using NPCSystem.LocalAI;
using NPCSystem.Monitoring;
using NPCSystem.Network.Core;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Bootstraps the <see cref="TelemetryRouter"/> with registered sinks.
    ///
    /// Call once during game initialization (e.g. from NPCSceneInitializationController's Logger phase).
    /// Lives in the Monitoring assembly — does NOT reference NPCFlowLogger to avoid circular deps.
    /// </summary>
    public static class TelemetryBootstrapper
    {
        static bool _initialized;

        /// <summary>
        /// Initialize telemetry infrastructure. Safe to call multiple times —
        /// only the first call has any effect.
        /// </summary>
        /// <param name="sessionId">Optional session ID for file sink subfolder.</param>
        /// <param name="metricPrefix">Optional prefix for Datadog metric names.</param>
        /// <param name="enableFileSink">Write JSONL telemetry to disk (always safe on all platforms).</param>
        /// <param name="enableDatadogSink">Bridge telemetry events to Datadog metrics.</param>
        public static void Initialize(
            string sessionId = null,
            string metricPrefix = "npc",
            bool enableFileSink = true,
            bool enableDatadogSink = true
        )
        {
            if (_initialized)
            {
                Debug.Log("[TelemetryBootstrapper] Already initialized — skipping.");
                return;
            }
            _initialized = true;

            try
            {
                TelemetryRouter router = TelemetryRouter.Instance;

                // Install global log interceptor to catch stray Debug.Log calls
                // and route them through Console Pro API automatically.
                ConsoleProLogInterceptor.Initialize();

                // Register file sink (always — JSONL is lightweight and works everywhere)
                if (enableFileSink)
                {
                    var fileSink = new FileTelemetrySink(
                        sessionId: sessionId ?? Guid.NewGuid().ToString("N")
                    );
                    router.Register(fileSink);
                    Debug.Log(
                        $"[TelemetryBootstrapper] FileTelemetrySink registered → {Application.persistentDataPath}/Telemetry/"
                    );
                }

                // Register ConsolePro sink (Editor-only — enhanced filtering via Console Pro)
                // Uses the FLYINGWORM_CONSOLE_3 define for conditional compilation.
                // The sink degrades gracefully to prefixed Debug.Log when Console Pro is absent.
                {
                    var consoleSink = new ConsoleProTelemetrySink(filterPrefix: metricPrefix);
                    router.Register(consoleSink);
                    Debug.Log("[TelemetryBootstrapper] ConsoleProTelemetrySink registered.");
                }

                // Register Datadog sink
                if (enableDatadogSink)
                {
                    var datadogSink = new DatadogTelemetrySink(metricPrefix: metricPrefix);
                    router.Register(datadogSink);
                    Debug.Log("[TelemetryBootstrapper] DatadogTelemetrySink registered.");
                }

                // Freeze the router — no more sinks during gameplay
                router.Freeze();

                Debug.Log(
                    $"[TelemetryBootstrapper] Telemetry router initialized with {router.SinkCount} sink(s)."
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TelemetryBootstrapper] Initialization failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }
    }
}
