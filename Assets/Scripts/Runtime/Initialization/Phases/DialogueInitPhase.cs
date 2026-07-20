using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;

namespace NPCSystem.Initialization
{
    /// <summary>
    /// Phase 4: Initializes the dialogue manager and its child services.
    /// Services (QdrantRAGService, SupabaseDialogueRepository, etc.) self-initialize
    /// in parallel using config from the DialogueManager.
    /// </summary>
    public sealed class DialogueInitPhase : ISceneInitializationPhase
    {
        public NPCSceneInitializationPhase PhaseId => NPCSceneInitializationPhase.DialogueServices;
        public NPCSceneInitializationPhase[] DependsOn => new[] { NPCSceneInitializationPhase.TransportConfiguration };
        public NPCFlowStage TelemetryStage => NPCFlowStage.SceneBootstrap;

        public bool IsEnabled(InitializationContext ctx)
        {
            return ctx.GetPhaseConfig(NPCSceneInitializationPhase.DialogueServices)?.Enabled ?? true;
        }

        public async Task ExecuteAsync(InitializationContext ctx, CancellationToken ct)
        {
            if (ctx.DialogueManager == null)
            {
                LogMissingRef(ctx, "_dialogueManager (NPCDialogueManager)");
                return;
            }

            await ctx.DialogueManager.InitializeAsync();

            ctx.Logger?.Log(
                TelemetryStage,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                "Dialogue manager initialized successfully.",
                source: nameof(DialogueInitPhase),
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
                source: nameof(DialogueInitPhase),
                data: new Dictionary<string, object>
                {
                    ["correlationId"] = ctx.CorrelationId,
                    ["missingRef"] = refName
                });
        }
    }
}
