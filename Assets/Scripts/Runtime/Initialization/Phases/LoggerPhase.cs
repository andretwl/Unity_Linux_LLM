using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 1: Logger capture and pipeline start event.
    /// DatadogConsent, TelemetryBootstrapper, and Datadog init are handled by
    /// NPCFlowLogger.Awake() at execution order -3000. This phase exists only
    /// to confirm the logger reference and emit the pipeline-started event.
    /// </summary>
    public sealed class LoggerPhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.Logger;
        public NPCSceneInitializationPhase[] DependsOn => System.Array.Empty<NPCSceneInitializationPhase>();
        public NPCFlowStage TelemetryStage => NPCFlowStage.SceneBootstrap;

        public bool IsEnabled(InitializationContext ctx) => true; // Always runs

        public Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            // Capture logger — the controller already validated it exists
            // in the reference validation phase (Phase 2), but on the fast
            // path (non-WebGL) this runs first, so we capture it here.
            ctx.Logger ??= NPCFlowLogger.FindOrCreate();

            // Emit pipeline-started telemetry
            InitTelemetry.PipelineStarted(
                ctx.CorrelationId,
                ctx.IsDeferred,
                phaseCount: System.Enum.GetValues(typeof(NPCSceneInitializationPhase)).Length);

            // Log to structured JSONL
            ctx.Logger?.Log(
                NPCFlowStage.SceneBootstrap,
                NPCFlowStatus.Start,
                NPCFlowLogLevel.Info,
                "Scene initialization pipeline started.",
                source: nameof(LoggerPhase),
                data: new System.Collections.Generic.Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["isDeferred"] = ctx.IsDeferred
                });

            return Task.CompletedTask;
        }
    }
}
