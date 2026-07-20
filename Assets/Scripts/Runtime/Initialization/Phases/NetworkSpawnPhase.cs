using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;
using UnityEngine;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 8: Starts network spawning (server mode).
    /// In batch mode, the bootstrap already handled auto-start in Awake()
    /// via CLI args. This phase skips to avoid double-start.
    /// </summary>
    public sealed class NetworkSpawnPhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.Spawning;
        public NPCSceneInitializationPhase[] DependsOn => new[] { NPCSceneInitializationPhase.Validation };
        public NPCFlowStage TelemetryStage => NPCFlowStage.NetworkHost;

        public bool IsEnabled(InitializationContext ctx)
        {
            return ctx.GetPhaseConfig(NPCSceneInitializationPhase.Spawning)?.Enabled ?? true;
        }

        public Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            if (ctx.NetworkBootstrap == null)
            {
                LogMissingRef(ctx, "_networkBootstrap (NPCNetworkBootstrap)");
                return Task.CompletedTask;
            }

            // In batch mode, the bootstrap already handled auto-start in Awake()
            // via CLI args (see NPCNetworkBootstrap.Awake). Skip here to avoid double-start.
            if (Application.isBatchMode)
            {
                ctx.Logger?.Log(
                    TelemetryStage,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Debug,
                    "Skipped network start because batchmode bootstrap is handling it via CLI args.",
                    source: nameof(NetworkSpawnPhase),
                    data: new Dictionary<string, object>
                    {
                        ["correlationId"] = ctx.CorrelationId
                    });
                return Task.CompletedTask;
            }

            bool started = ctx.NetworkBootstrap.StartConfiguredMode();

            ctx.Logger?.Log(
                TelemetryStage,
                started ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                started ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                started
                    ? "NetworkManager started from scene initialization controller."
                    : "NetworkManager start skipped by scene initialization controller.",
                source: nameof(NetworkSpawnPhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["started"] = started
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
                source: nameof(NetworkSpawnPhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["missingRef"] = refName
                });
        }
    }
}
