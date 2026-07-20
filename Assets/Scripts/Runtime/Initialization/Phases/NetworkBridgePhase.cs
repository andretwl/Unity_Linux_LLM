using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 6: Initializes the network bridge for RPC dialogue forwarding.
    /// Binds NetworkBehaviour events to the DialogueManager.
    /// </summary>
    public sealed class NetworkBridgePhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.NetworkBridge;
        public NPCSceneInitializationPhase[] DependsOn => new[] { NPCSceneInitializationPhase.BackendReadiness };
        public NPCFlowStage TelemetryStage => NPCFlowStage.NetworkHost;

        public bool IsEnabled(InitializationContext ctx)
        {
            return ctx.GetPhaseConfig(NPCSceneInitializationPhase.NetworkBridge)?.Enabled ?? true;
        }

        public async Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            if (ctx.NetworkBridge == null)
            {
                LogMissingRef(ctx, "_networkBridge (NPCDialogueNetworkBridge)");
                return;
            }

            await ctx.NetworkBridge.InitializeAsync();

            ctx.Logger?.Log(
                TelemetryStage,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                "Network bridge initialized successfully.",
                source: nameof(NetworkBridgePhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId
                });
        }

        static void LogMissingRef(InitializationContext ctx, string refName)
        {
            ctx.Logger?.Log(
                NPCFlowStage.ReferenceResolution,
                NPCFlowStatus.Error,
                NPCFlowLogLevel.Error,
                $"Required reference {refName} is not assigned. "
                + "Wire it in the Inspector — FindAnyObjectByType is not used.",
                source: nameof(NetworkBridgePhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["missingRef"] = refName
                });
        }
    }
}
