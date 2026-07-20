using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 3: Configures network transport settings (port, address, WebSockets).
    /// Reads CLI args and applies them to the Unity Transport component.
    /// </summary>
    public sealed class TransportConfigPhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.TransportConfiguration;
        public NPCSceneInitializationPhase[] DependsOn => new[] { NPCSceneInitializationPhase.ReferenceValidation };
        public NPCFlowStage TelemetryStage => NPCFlowStage.ConfigurationValidation;

        public bool IsEnabled(InitializationContext ctx)
        {
            // Check config asset if available, otherwise default to enabled
            // The controller sets this via the config asset before running phases
            return ctx.GetPhaseConfig(NPCSceneInitializationPhase.TransportConfiguration)?.Enabled ?? true;
        }

        public Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            if (ctx.NetworkBootstrap == null)
            {
                LogMissingRef(ctx, "_networkBootstrap (NPCNetworkBootstrap)");
                return Task.CompletedTask;
            }

            ctx.NetworkBootstrap.ApplyTransportConfiguration();

            ctx.Logger?.Log(
                TelemetryStage,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                "Network transport configuration applied.",
                source: nameof(TransportConfigPhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId
                });

            return Task.CompletedTask;
        }

        static void LogMissingRef(InitializationContext ctx, string refName)
        {
            ctx.Logger?.Log(
                NPCFlowStage.ReferenceResolution,
                NPCFlowStatus.Error,
                NPCFlowLogLevel.Error,
                $"Required reference {refName} is not assigned. "
                + "Wire it in the Inspector — FindAnyObjectByType is not used.",
                source: nameof(TransportConfigPhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["missingRef"] = refName
                });
        }
    }
}
