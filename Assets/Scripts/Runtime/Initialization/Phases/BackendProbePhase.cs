using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 5: Probes auth backend and LocalAI endpoint readiness.
    /// Verifies backends are reachable before multiplayer dialogue traffic starts.
    /// </summary>
    public sealed class BackendProbePhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.BackendReadiness;
        public NPCSceneInitializationPhase[] DependsOn => new[] { NPCSceneInitializationPhase.DialogueServices };
        public NPCFlowStage TelemetryStage => NPCFlowStage.SceneBootstrap;

        public bool IsEnabled(InitializationContext ctx)
        {
            return ctx.GetPhaseConfig(NPCSceneInitializationPhase.BackendReadiness)?.Enabled ?? true;
        }

        public async Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            if (ctx.BackendReadiness == null)
            {
                LogMissingRef(ctx, "_backendReadiness (NPCBackendReadinessService)");
                return;
            }

            NPCBackendReadinessSnapshot snapshot = await ctx.BackendReadiness.ProbeAsync();

            var data = new Dictionary<string, object>
            {
                ["correlationId"] = ctx.CorrelationId,
                ["authReachable"] = snapshot.auth.reachable,
                ["localAiReachable"] = snapshot.localAi.reachable,
                ["authStatus"] = snapshot.auth.status,
                ["localAiStatus"] = snapshot.localAi.status
            };

            if (snapshot.auth.reachable && snapshot.localAi.reachable)
            {
                ctx.Logger?.Log(
                    TelemetryStage,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "Required multiplayer dialogue backends are reachable.",
                    source: nameof(BackendProbePhase),
                    data: data);
            }
            else
            {
                ctx.Logger?.Log(
                    TelemetryStage,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    "One or more required multiplayer dialogue backends are unreachable.",
                    source: nameof(BackendProbePhase),
                    data: data);
            }
        }

        static void LogMissingRef(InitializationContext ctx, string refName)
        {
            ctx.Logger?.Log(
                NPCFlowStage.ReferenceResolution,
                NPCFlowStatus.Error,
                NPCFlowLogLevel.Error,
                $"Required reference {refName} is not assigned. "
                + "Wire it in the Inspector — FindAnyObjectByType is not used.",
                source: nameof(BackendProbePhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["missingRef"] = refName
                });
        }
    }
}
