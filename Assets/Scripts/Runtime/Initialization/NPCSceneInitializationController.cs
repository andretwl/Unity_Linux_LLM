using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NPCSystem.Monitoring;
using UnityEngine;
using UnityEngine.Serialization;

using NPCSystem.Dialogue.Core;
using NPCSystem.Network.Core;
using NPCSystem.Network.Bridges;

namespace NPCSystem.Initialization
{
    public enum NPCSceneInitializationPhase
    {
        Logger,
        ReferenceValidation,
        TransportConfiguration,
        DialogueServices,
        BackendReadiness,
        NetworkBridge,
        Validation,
        Spawning
    }

    /// <summary>
    /// Single orchestrator for all scene initialization.
    /// Delegates to ISceneInitializationPhase implementations for each step.
    ///
    /// Design constraints:
    /// - Every component's Start() is empty — no self-init, no fire-and-forget.
    /// - All references are serialized in the Inspector (no FindAnyObjectByType).
    /// - On WebGL, Phases 1-2 run immediately (logger + ref validation),
    ///   Phases 3-8 run on explicit ContinueInitializationAsync() after scene load.
    /// - On all other platforms, all 8 phases run in Start().
    /// - If a required serialized reference is null, the pipeline logs an error
    ///   and skips that phase — the scene is misconfigured.
    /// </summary>
    public sealed class NPCSceneInitializationController : MonoBehaviour
    {
        // ── Phase ordering (static, shared with tests) ──
        public static readonly NPCSceneInitializationPhase[] OrderedPhases =
        {
            NPCSceneInitializationPhase.Logger,
            NPCSceneInitializationPhase.ReferenceValidation,
            NPCSceneInitializationPhase.TransportConfiguration,
            NPCSceneInitializationPhase.DialogueServices,
            NPCSceneInitializationPhase.BackendReadiness,
            NPCSceneInitializationPhase.NetworkBridge,
            NPCSceneInitializationPhase.Validation,
            NPCSceneInitializationPhase.Spawning
        };

        // ── Public accessors (used by tests) ──
        public NPCNetworkBootstrap NetworkBootstrap => _networkBootstrap;
        public NPCDialogueManager DialogueManager => _dialogueManager;
        public NPCBackendReadinessService BackendReadiness => _backendReadiness;
        public NPCDialogueNetworkBridge NetworkBridge => _networkBridge;
        public NPCDialogueSmokeValidator SmokeValidator => _smokeValidator;

        // ── Pipeline state (for external observers) ──
        public InitializationState State => _context?.State ?? InitializationState.NotStarted;
        public NPCSceneInitializationPhase? CurrentPhase => _context?.CurrentPhase;
        public string CorrelationId => _context?.CorrelationId;
        public bool IsPhaseCompleted(NPCSceneInitializationPhase phase) => _context?.IsPhaseCompleted(phase) ?? false;
        public string GetStatusSummary() => _context?.GetStatusSummary() ?? "Pipeline not initialized.";

        /// <summary>True when Phases 1-2 ran and 3-8 are pending ContinueInitializationAsync().</summary>
        public bool IsDeferred =>
#if UNITY_WEBGL && !UNITY_EDITOR
            _context?.IsDeferred ?? false;
#else
            false;
#endif

        // ── Inspector References ──
        [Header("References — all must be serialized in Inspector")]
        [SerializeField, FormerlySerializedAs("FlowLogger")]
        NPCFlowLogger _flowLogger;

        [SerializeField, FormerlySerializedAs("NetworkBootstrap")]
        NPCNetworkBootstrap _networkBootstrap;

        [SerializeField, FormerlySerializedAs("DialogueManager")]
        NPCDialogueManager _dialogueManager;

        [SerializeField, FormerlySerializedAs("BackendReadiness")]
        NPCBackendReadinessService _backendReadiness;

        [SerializeField, FormerlySerializedAs("NetworkBridge")]
        NPCDialogueNetworkBridge _networkBridge;

        [SerializeField, FormerlySerializedAs("SmokeValidator")]
        NPCDialogueSmokeValidator _smokeValidator;

        [Header("Configuration")]
        [SerializeField, FormerlySerializedAs("InitializeOnStart")]
        bool _initializeOnStart = true;

        [SerializeField, FormerlySerializedAs("ConfigureNetworkTransport")]
        bool _configureNetworkTransport = true;

        [SerializeField, FormerlySerializedAs("InitializeDialogueManager")]
        bool _initializeDialogueManager = true;

        [SerializeField, FormerlySerializedAs("VerifyBackendsDuringInitialization")]
        bool _verifyBackendsDuringInitialization = true;

        [SerializeField, FormerlySerializedAs("InitializeNetworkBridge")]
        bool _initializeNetworkBridge = true;

        [SerializeField, FormerlySerializedAs("ValidateAfterInitialization")]
        bool _validateAfterInitialization = true;

        [SerializeField, FormerlySerializedAs("StartNetworkingAfterInitialization")]
        bool _startNetworkingAfterInitialization = true;

        [SerializeField, FormerlySerializedAs("SceneInitializationConfig")]
        SceneInitializationConfig _config;

        // ── Private state ──
        InitializationContext _context;
        CancellationTokenSource _pipelineCts;
        bool _initialized;

        // Phase registry
        readonly Dictionary<NPCSceneInitializationPhase, ISceneInitializationPhase> _phaseHandlers
            = new Dictionary<NPCSceneInitializationPhase, ISceneInitializationPhase>();

        // ── Unity Lifecycle ──

        void OnValidate()
        {
            if (_initializeOnStart && _flowLogger == null)
                ValidateSerializedReferences();
        }

        void Awake()
        {
            RegisterPhaseHandlers();
        }

        async void Start()
        {
            if (!_initializeOnStart) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: run Phases 1-2 immediately (logger + ref validation),
            // defer Phases 3-8 to ContinueInitializationAsync().
            await RunPhasesAsync(0, 2);
            _context.IsDeferred = true;
            _context.SetState(InitializationState.Running);
#else
            await RunFullPipelineAsync();
#endif
        }

        // ── Public API ──

        /// <summary>
        /// Continue WebGL initialization after the scene is fully loaded.
        /// Runs Phases 3-8 (Transport, Dialogue, Backend, Bridge, Validation, Spawning).
        /// Safe to call multiple times — only the first call has effect.
        /// </summary>
        public async Task ContinueInitializationAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_context == null || !_context.IsDeferred)
                throw new InvalidOperationException(
                    "ContinueInitializationAsync() is only valid on WebGL after Start() deferred the pipeline.");

            _context.IsDeferred = false;
            await RunPhasesAsync(2, OrderedPhases.Length);
#else
            await Task.CompletedTask;
#endif
        }

        /// <summary>Run the full pipeline immediately (all 8 phases).</summary>
        [ContextMenu("Initialize Scene")]
        public async void InitializeSceneFromContextMenu()
        {
            await RunFullPipelineAsync();
        }

        // ── Internal orchestration ──

        async Task RunFullPipelineAsync()
        {
            _context = CreateContext();
            _context.SetState(InitializationState.Running);
            _pipelineCts = new CancellationTokenSource();

            InitTelemetry.PipelineStarted(
                _context.CorrelationId,
                _context.IsDeferred,
                OrderedPhases.Length);

            try
            {
                // Apply pipeline timeout
                if (_config != null && _config.PipelineTimeoutSeconds > 0)
                    _pipelineCts.CancelAfter(TimeSpan.FromSeconds(_config.PipelineTimeoutSeconds));

                for (int i = 0; i < OrderedPhases.Length; i++)
                {
                    await RunPhaseAsync(OrderedPhases[i], i);
                }

                _context.SetState(InitializationState.Completed);
                _context.LogSummary();

                InitTelemetry.PipelineCompleted(
                    _context.CorrelationId,
                    _context.Elapsed,
                    completed: _context.Results.Count,
                    skipped: 0);
            }
            catch (OperationCanceledException)
            {
                _context.SetState(InitializationState.Failed);
                _context.LogSummary();

                InitTelemetry.PipelineFailed(
                    _context.CorrelationId,
                    _context.CurrentPhase ?? NPCSceneInitializationPhase.Logger,
                    new TimeoutException("Pipeline timed out."),
                    _context.Elapsed);
            }
            catch (Exception ex)
            {
                _context.SetState(InitializationState.Failed);
                _context.LogSummary();

                InitTelemetry.PipelineFailed(
                    _context.CorrelationId,
                    _context.CurrentPhase ?? NPCSceneInitializationPhase.Logger,
                    ex,
                    _context.Elapsed);
            }
            finally
            {
                _pipelineCts?.Dispose();
                _pipelineCts = null;
            }
        }

        async Task RunPhasesAsync(int startIndex, int endIndex)
        {
            if (_context == null)
            {
                _context = CreateContext();
                _context.SetState(InitializationState.Running);
            }

            for (int i = startIndex; i < endIndex; i++)
            {
                await RunPhaseAsync(OrderedPhases[i], i);
            }
        }

        async Task RunPhaseAsync(NPCSceneInitializationPhase phase, int index)
        {
            _context.SetCurrentPhase(phase);
            _context.Logger = _flowLogger;

            var handler = GetPhaseHandler(phase);

            // Check if phase is enabled
            if (handler != null && !handler.IsEnabled(_context))
            {
                var skipResult = PhaseResult.SkippedResult(phase, "Phase disabled by config.");
                _context.RecordPhaseResult(skipResult);
                InitTelemetry.PhaseSkipped(_context.CorrelationId, phase, "Disabled by config");
                return;
            }

            // Check dependencies
            if (handler?.DependsOn != null)
            {
                foreach (var dep in handler.DependsOn)
                {
                    if (!_context.IsPhaseCompleted(dep))
                    {
                        var skipResult = PhaseResult.SkippedResult(phase, $"Dependency {dep} not completed.");
                        _context.RecordPhaseResult(skipResult);
                        InitTelemetry.PhaseSkipped(_context.CorrelationId, phase, $"Dependency {dep} failed/skipped");
                        return;
                    }
                }
            }

            InitTelemetry.PhaseStarted(_context.CorrelationId, phase, index, OrderedPhases.Length);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Apply per-phase timeout
                using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(_pipelineCts?.Token ?? CancellationToken.None);
                var config = _config?.GetConfig(phase);
                if (config != null && config.TimeoutSeconds > 0)
                    phaseCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

                await handler.ExecuteAsync(_context, phaseCts.Token);

                sw.Stop();
                var result = PhaseResult.Succeeded(phase, sw.Elapsed);
                _context.RecordPhaseResult(result);

                InitTelemetry.PhaseCompleted(_context.CorrelationId, phase, sw.Elapsed);
            }
            catch (OperationCanceledException) when (_pipelineCts?.IsCancellationRequested == true)
            {
                sw.Stop();
                var result = PhaseResult.Failed(phase, sw.Elapsed,
                    new TimeoutException($"Phase {phase} timed out."));
                _context.RecordPhaseResult(result);
                throw; // Re-throw to abort pipeline
            }
            catch (Exception ex)
            {
                sw.Stop();
                var result = PhaseResult.Failed(phase, sw.Elapsed, ex);
                _context.RecordPhaseResult(result);

                InitTelemetry.PhaseFailed(_context.CorrelationId, phase, ex, sw.Elapsed);

                throw; // Abort pipeline on phase failure
            }
        }

        // ── Phase registration ──

        void RegisterPhaseHandlers()
        {
            RegisterPhase(new LoggerPhase());
            RegisterPhase(new ReferenceValidationPhase());
            RegisterPhase(new TransportConfigPhase());
            RegisterPhase(new DialogueInitPhase());
            RegisterPhase(new BackendProbePhase());
            RegisterPhase(new NetworkBridgePhase());
            RegisterPhase(new SmokeValidationPhase());
            RegisterPhase(new NetworkSpawnPhase());
        }

        void RegisterPhase(ISceneInitializationPhase phase)
        {
            _phaseHandlers[phase.PhaseId] = phase;
        }

        ISceneInitializationPhase GetPhaseHandler(NPCSceneInitializationPhase phase)
        {
            return _phaseHandlers.TryGetValue(phase, out var handler) ? handler : null;
        }

        // ── Context creation ──

        InitializationContext CreateContext()
        {
            return new InitializationContext
            {
                Logger = _flowLogger,
                NetworkBootstrap = _networkBootstrap,
                DialogueManager = _dialogueManager,
                BackendReadiness = _backendReadiness,
                NetworkBridge = _networkBridge,
                SmokeValidator = _smokeValidator,
                Config = _config,
                IsDeferred = false
            };
        }

        // ── Reference validation ──

        void ValidateSerializedReferences()
        {
            var missing = new List<string>();

            if (_flowLogger == null) missing.Add("_flowLogger (NPCFlowLogger)");
            if (_networkBootstrap == null) missing.Add("_networkBootstrap (NPCNetworkBootstrap)");
            if (_dialogueManager == null) missing.Add("_dialogueManager (NPCDialogueManager)");
            if (_backendReadiness == null) missing.Add("_backendReadiness (NPCBackendReadinessService)");
            if (_networkBridge == null) missing.Add("_networkBridge (NPCDialogueNetworkBridge)");
            if (_smokeValidator == null) missing.Add("_smokeValidator (NPCDialogueSmokeValidator)");

            if (missing.Count == 0) return;

            string msg = "Scene initialization controller is missing serialized references: "
                + string.Join(", ", missing) + ". "
                + "Drag the required components into the Inspector slots. "
                + "FindAnyObjectByType is not used — all dependencies must be wired in the scene.";

            if (_flowLogger != null)
            {
                _flowLogger.Log(
                    NPCFlowStage.ReferenceResolution,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    msg,
                    source: nameof(NPCSceneInitializationController));
            }
            else
            {
                Debug.LogWarning($"[{nameof(NPCSceneInitializationController)}] {msg}");
            }
        }
    }
}
