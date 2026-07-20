using System.Threading;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Contract for a single scene initialization phase.
    /// Each phase is an independent, testable unit with explicit dependencies.
    /// </summary>
    public interface ISceneInitializationPhase
    {
        /// <summary>The phase identifier matching <see cref="NPCSceneInitializationPhase"/> enum.</summary>
        NPCSceneInitializationPhase PhaseId { get; }

        /// <summary>Phases that must complete successfully before this one runs.</summary>
        NPCSceneInitializationPhase[] DependsOn { get; }

        /// <summary>The <see cref="Monitoring.NPCFlowStage"/> used for telemetry correlation.</summary>
        Monitoring.NPCFlowStage TelemetryStage { get; }

        /// <summary>Check if this phase should run given the current context and config.</summary>
        bool IsEnabled(InitializationContext ctx);

        /// <summary>Execute the phase logic. Throw on unrecoverable failure.</summary>
        System.Threading.Tasks.Task ExecuteAsync(InitializationContext ctx, CancellationToken ct);
    }
}
