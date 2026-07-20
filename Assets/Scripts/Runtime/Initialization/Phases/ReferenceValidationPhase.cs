using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 2: Validates that all serialized references are assigned in the Inspector.
    /// Logs warnings for null references — the pipeline will skip their phases.
    /// Does NOT search for references via FindAnyObjectByType — scene wiring is mandatory.
    /// </summary>
    public sealed class ReferenceValidationPhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.ReferenceValidation;
        public NPCSceneInitializationPhase[] DependsOn => new[] { NPCSceneInitializationPhase.Logger };
        public NPCFlowStage TelemetryStage => NPCFlowStage.ReferenceResolution;

        public bool IsEnabled(InitializationContext ctx) => true; // Always runs

        public Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            var missing = new List<string>();

            if (ctx.Logger == null)
                missing.Add("_flowLogger (NPCFlowLogger)");

            if (ctx.NetworkBootstrap == null)
                missing.Add("_networkBootstrap (NPCNetworkBootstrap)");

            if (ctx.DialogueManager == null)
                missing.Add("_dialogueManager (NPCDialogueManager)");

            if (ctx.BackendReadiness == null)
                missing.Add("_backendReadiness (NPCBackendReadinessService)");

            if (ctx.NetworkBridge == null)
                missing.Add("_networkBridge (NPCDialogueNetworkBridge)");

            if (ctx.SmokeValidator == null)
                missing.Add("_smokeValidator (NPCDialogueSmokeValidator)");

            if (missing.Count > 0)
            {
                string msg = "Scene initialization controller is missing serialized references: "
                    + string.Join(", ", missing)
                    + ". FindAnyObjectByType is not used — all dependencies must be wired in the scene.";

                ctx.Logger?.Log(
                    NPCFlowStage.ReferenceResolution,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    msg,
                    source: nameof(ReferenceValidationPhase),
                    data: new Dictionary<string, object>
                    {
                        ["correlationId"] = ctx.CorrelationId,
                        ["missingRefs"] = string.Join(";", missing),
                        ["missingCount"] = missing.Count
                    });
            }
            else
            {
                ctx.Logger?.Log(
                    NPCFlowStage.ReferenceResolution,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    "All serialized references validated successfully.",
                    source: nameof(ReferenceValidationPhase),
                    data: new Dictionary<string, object>
                    {
                        ["correlationId"] = ctx.CorrelationId
                    });
            }

            return Task.CompletedTask;
        }
    }
}
