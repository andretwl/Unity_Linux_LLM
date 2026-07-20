using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;
using NPCSystem.Monitoring.Datadog;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 7: Runs smoke validation to verify dialogue pipeline integrity.
    /// Skipped if dialogue manager is not initialized yet (deferred loading active).
    /// </summary>
    public sealed class SmokeValidationPhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.Validation;
        public NPCSceneInitializationPhase[] DependsOn => new[] { NPCSceneInitializationPhase.NetworkBridge };
        public NPCFlowStage TelemetryStage => NPCFlowStage.SmokeValidation;

        public bool IsEnabled(InitializationContext ctx)
        {
            return ctx.GetPhaseConfig(NPCSceneInitializationPhase.Validation)?.Enabled ?? true;
        }

        public async Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            // Skip if dialogue manager is not initialized yet (deferred loading active)
            if (ctx.DialogueManager == null || !ctx.DialogueManager.IsInitialized)
            {
                ctx.Logger?.Log(
                    TelemetryStage,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Debug,
                    "Skipped smoke validation because dialogue manager is not initialized yet (deferred loading active).",
                    source: nameof(SmokeValidationPhase),
                    data: new Dictionary<string, object>
                    {
                        ["correlationId"] = ctx.CorrelationId
                    });
                return;
            }

            if (ctx.SmokeValidator == null)
            {
                LogMissingRef(ctx, "_smokeValidator (NPCDialogueSmokeValidator)");
                return;
            }

            // ValidateConfiguration() is async; if it completes without exception, it passed.
            // On failure it calls Application.Quit(1), but we catch anyway for robustness.
            bool passed = true;
            try
            {
                await ctx.SmokeValidator.ValidateConfiguration();
            }
            catch (Exception ex)
            {
                ctx.Logger?.Log(
                    TelemetryStage,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"Smoke validation threw: {ex.Message}",
                    source: nameof(SmokeValidationPhase),
                    data: new Dictionary<string, object>
                    {
                        ["correlationId"] = ctx.CorrelationId,
                        ["exception"] = ex.GetType().Name
                    });
                passed = false;
            }

            ctx.Logger?.Log(
                TelemetryStage,
                passed ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                passed ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                passed ? "Smoke validation passed." : "Smoke validation failed \u2014 see previous logs for details.",
                source: nameof(SmokeValidationPhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["passed"] = passed
                });

            if (!passed)
            {
                DatadogMetricsService.Increment(
                    "init.smoke.failed",
                    tags: new[] { $"correlationId:{ctx.CorrelationId}" }
                );
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
                source: nameof(SmokeValidationPhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["missingRef"] = refName
                });
        }
    }
}
