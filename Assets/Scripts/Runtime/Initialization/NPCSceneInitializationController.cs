using System;
using System.Collections.Generic;
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
        SceneReferences,
        NetworkTransport,
        DialogueServices,
        BackendReadiness,
        NetworkBridge,
        Validation,
        Spawning,
    }

    /// <summary>
    /// Single orchestrator for all scene initialization.
    ///
    /// CONTRACT:
    /// - Every component's Start() is empty — no self-init, no fire-and-forget.
    /// - All references are serialized in the Inspector (no FindAnyObjectByType).
    /// - On WebGL, Phases 1-2 run immediately (logger + ref validation),
    ///   Phases 3-8 run on explicit ContinueInitializationAsync() after scene load.
    /// - On all other platforms, all 8 phases run in Start().
    /// - If a required serialized reference is null, the pipeline logs an error
    ///   and skips that phase — the scene is misconfigured.
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    [DisallowMultipleComponent]
    public sealed class NPCSceneInitializationController : MonoBehaviour
    {
        public static readonly NPCSceneInitializationPhase[] OrderedPhases =
        {
            NPCSceneInitializationPhase.Logger,
            NPCSceneInitializationPhase.SceneReferences,
            NPCSceneInitializationPhase.NetworkTransport,
            NPCSceneInitializationPhase.DialogueServices,
            NPCSceneInitializationPhase.BackendReadiness,
            NPCSceneInitializationPhase.NetworkBridge,
            NPCSceneInitializationPhase.Validation,
            NPCSceneInitializationPhase.Spawning,
        };

        // ─── Public accessors (used by tests) ───
        public NPCNetworkBootstrap NetworkBootstrap => _networkBootstrap;
        public NPCDialogueManager DialogueManager => _dialogueManager;
        public NPCBackendReadinessService BackendReadiness => _backendReadiness;
        public NPCDialogueNetworkBridge NetworkBridge => _networkBridge;
        public NPCDialogueSmokeValidator SmokeValidator => _smokeValidator;
        public bool ConfigureNetworkTransport => _configureNetworkTransport;
        public bool StartNetworkingAfterInitialization => _startNetworkingAfterInitialization;

        [Header("References — all must be serialized in Inspector")]
        [FormerlySerializedAs("FlowLogger")]
        [SerializeField]
        NPCFlowLogger _flowLogger;
        [FormerlySerializedAs("NetworkBootstrap")]
        [SerializeField]
        NPCNetworkBootstrap _networkBootstrap;
        [FormerlySerializedAs("DialogueManager")]
        [SerializeField]
        NPCDialogueManager _dialogueManager;
        [FormerlySerializedAs("BackendReadiness")]
        [SerializeField]
        NPCBackendReadinessService _backendReadiness;
        [FormerlySerializedAs("NetworkBridge")]
        [SerializeField]
        NPCDialogueNetworkBridge _networkBridge;
        [FormerlySerializedAs("SmokeValidator")]
        [SerializeField]
        NPCDialogueSmokeValidator _smokeValidator;

        [Header("Startup Flags")]
        [FormerlySerializedAs("InitializeOnStart")]
        [SerializeField]
        bool _initializeOnStart = true;
        [FormerlySerializedAs("ConfigureNetworkTransport")]
        [SerializeField]
        bool _configureNetworkTransport = false;

        [Tooltip(
            "If true, initializes the dialogue manager during scene initialization. Set to false to delay initialization until after player login (recommended for WebGL memory-smart start)."
        )]
        [FormerlySerializedAs("InitializeDialogueManager")]
        [SerializeField]
        bool _initializeDialogueManager = false;
        [FormerlySerializedAs("VerifyBackendsDuringInitialization")]
        [SerializeField]
        bool _verifyBackendsDuringInitialization = false;
        [FormerlySerializedAs("InitializeNetworkBridge")]
        [SerializeField]
        bool _initializeNetworkBridge = true;
        [FormerlySerializedAs("ValidateAfterInitialization")]
        [SerializeField]
        bool _validateAfterInitialization = true;
        [FormerlySerializedAs("StartNetworkingAfterInitialization")]
        [SerializeField]
        bool _startNetworkingAfterInitialization = false;

        bool _started;
        bool _deferredStartRequested;
        Task _initializationTask;

        public bool IsInitialized =>
            _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
        public Task InitializationTask => _initializationTask;

        /// <summary>
        /// If true, only Phases 1-2 ran and the rest are pending ContinueInitializationAsync().
        /// </summary>
        public bool IsDeferred =>
            !_started && _deferredStartRequested && _initializationTask == null;

        void Reset()
        {
            // Reset logs a warning if references are null — sets up the developer.
            ValidateSerializedReferences();
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ValidateSerializedReferences();
            }
        }

        void Awake()
        {
            ValidateSerializedReferences();
        }

        async void Start()
        {
            if (!_initializeOnStart)
                return;

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                // WebGL: run Phases 1-2 immediately (logger + ref validation),
                // defer Phases 3-8 to ContinueInitializationAsync().
                await RunPhasesAsync(0, 2);
                _deferredStartRequested = true;
                return;
            }

            await InitializeSceneAsync();
        }

        /// <summary>
        /// Continue WebGL initialization after the scene is fully loaded.
        /// Runs Phases 3-8 (Transport, Dialogue, Backend, Bridge, Validation, Spawning).
        /// Safe to call multiple times — only the first call has effect.
        /// </summary>
        public Task ContinueInitializationAsync()
        {
            if (!_deferredStartRequested)
                throw new InvalidOperationException(
                    "ContinueInitializationAsync() is only valid on WebGL after Start() deferred the pipeline."
                );

            _initializationTask ??= RunPhasesAsync(2, OrderedPhases.Length);
            return _initializationTask;
        }

        /// <summary>
        /// Run the full pipeline immediately (all 8 phases).
        /// Used from ContextMenu or external triggers on all platforms.
        /// </summary>
        [ContextMenu("Initialize Scene")]
        public async void InitializeSceneFromContextMenu()
        {
            await InitializeSceneAsync();
        }

        public Task InitializeSceneAsync()
        {
            _initializationTask ??= RunPhasesAsync(0, OrderedPhases.Length);
            return _initializationTask;
        }

        async Task RunPhasesAsync(int startIndex, int endIndex)
        {
            if (_started && startIndex == 0)
                return;

            if (_started && startIndex > 0)
            {
                // Allow continuation from a deferred state only once
                if (!_deferredStartRequested)
                    return;
            }

            _started = startIndex == 0;

            for (int i = startIndex; i < endIndex; i++)
            {
                await RunPhaseAsync(OrderedPhases[i]);
            }
        }

        async Task RunPhaseAsync(NPCSceneInitializationPhase phase)
        {
            NPCFlowLogger logger = _flowLogger;
            using var scope = NPCFlowScope.Start(
                logger,
                NPCFlowStage.SceneBootstrap,
                source: nameof(NPCSceneInitializationController),
                data: new Dictionary<string, object> { ["phase"] = phase.ToString() }
            );

            try
            {
                switch (phase)
                {
                    case NPCSceneInitializationPhase.Logger:
                        // DatadogConsent, TelemetryBootstrapper, and Datadog init
                        // are handled by NPCFlowLogger.Awake() at order -3000.
                        // This phase exists only to capture the logger reference
                        // and log that the pipeline started.
                        logger?.Log(
                            NPCFlowStage.SceneBootstrap,
                            NPCFlowStatus.Start,
                            NPCFlowLogLevel.Info,
                            "Scene initialization pipeline started.",
                            source: nameof(NPCSceneInitializationController)
                        );
                        break;

                    case NPCSceneInitializationPhase.SceneReferences:
                        ValidateSerializedReferences();
                        break;

                    case NPCSceneInitializationPhase.NetworkTransport:
                        if (!_configureNetworkTransport)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Debug,
                                "Network transport configuration is disabled (_configureNetworkTransport = false).",
                                source: nameof(NPCSceneInitializationController)
                            );
                            break;
                        }
                        if (_networkBootstrap == null)
                        {
                            LogMissingRef(logger, "_networkBootstrap (NPCNetworkBootstrap)");
                            break;
                        }
                        _networkBootstrap.ApplyTransportConfiguration();
                        break;

                    case NPCSceneInitializationPhase.DialogueServices:
                        if (!_initializeDialogueManager)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Debug,
                                "Dialogue manager initialization is disabled (_initializeDialogueManager = false).",
                                source: nameof(NPCSceneInitializationController)
                            );
                            break;
                        }
                        if (_dialogueManager == null)
                        {
                            LogMissingRef(logger, "_dialogueManager (NPCDialogueManager)");
                            break;
                        }
                        await _dialogueManager.InitializeAsync();
                        break;

                    case NPCSceneInitializationPhase.BackendReadiness:
                        if (!_verifyBackendsDuringInitialization)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Debug,
                                "Backend readiness verification is disabled (_verifyBackendsDuringInitialization = false).",
                                source: nameof(NPCSceneInitializationController)
                            );
                            break;
                        }
                        if (_backendReadiness == null)
                        {
                            LogMissingRef(logger, "_backendReadiness (NPCBackendReadinessService)");
                            break;
                        }
                        {
                            bool probeLocalAi =
                                _initializeDialogueManager
                                && _dialogueManager != null
                                && _dialogueManager.InitializeOnStart;
                            await _backendReadiness.ProbeAsync(probeLocalAi);
                        }
                        break;

                    case NPCSceneInitializationPhase.NetworkBridge:
                        if (!_initializeNetworkBridge)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Debug,
                                "Network bridge initialization is disabled (_initializeNetworkBridge = false).",
                                source: nameof(NPCSceneInitializationController)
                            );
                            break;
                        }
                        if (_networkBridge == null)
                        {
                            LogMissingRef(logger, "_networkBridge (NPCDialogueNetworkBridge)");
                            break;
                        }
                        await _networkBridge.InitializeAsync();
                        break;

                    case NPCSceneInitializationPhase.Validation:
                        if (!_validateAfterInitialization)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Debug,
                                "Post-init validation is disabled (_validateAfterInitialization = false).",
                                source: nameof(NPCSceneInitializationController)
                            );
                            break;
                        }
                        if (_smokeValidator == null)
                        {
                            LogMissingRef(logger, "_smokeValidator (NPCDialogueSmokeValidator)");
                            break;
                        }
                        if (_dialogueManager != null && !_dialogueManager.IsInitialized)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Info,
                                "Skipped smoke validation because dialogue manager is not initialized yet (deferred loading active).",
                                source: nameof(NPCSceneInitializationController),
                                data: new Dictionary<string, object>
                                {
                                    ["phase"] = phase.ToString(),
                                }
                            );
                        }
                        else
                        {
                            await _smokeValidator.ValidateConfiguration();
                        }
                        break;

                    case NPCSceneInitializationPhase.Spawning:
                        if (!_startNetworkingAfterInitialization)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Debug,
                                "Network start after init is disabled (_startNetworkingAfterInitialization = false).",
                                source: nameof(NPCSceneInitializationController)
                            );
                            break;
                        }
                        if (_networkBootstrap == null)
                        {
                            LogMissingRef(logger, "_networkBootstrap (NPCNetworkBootstrap)");
                            break;
                        }

                        // In batch mode, the bootstrap already handled auto-start in Awake()
                        // via CLI args (see NPCNetworkBootstrap.Awake). Skip here to avoid double-start.
                        if (Application.isBatchMode
                            && _networkBootstrap.TransportConfig.AutoStartMode
                                != NPCNetworkAutoStartMode.Manual)
                        {
                            logger?.Log(
                                NPCFlowStage.SceneBootstrap,
                                NPCFlowStatus.Skipped,
                                NPCFlowLogLevel.Info,
                                "Skipped network start because batchmode bootstrap is handling it via CLI args.",
                                source: nameof(NPCSceneInitializationController),
                                data: new Dictionary<string, object>
                                {
                                    ["phase"] = phase.ToString(),
                                    ["autoStartMode"] =
                                        _networkBootstrap.TransportConfig.AutoStartMode.ToString(),
                                }
                            );
                            break;
                        }

                        bool started = _networkBootstrap.StartConfiguredMode();
                        logger?.Log(
                            NPCFlowStage.SceneBootstrap,
                            started ? NPCFlowStatus.Success : NPCFlowStatus.Skipped,
                            started ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                            started
                                ? "NetworkManager started from scene initialization controller."
                                : "NetworkManager start skipped by scene initialization controller.",
                            source: nameof(NPCSceneInitializationController),
                            data: new Dictionary<string, object>
                            {
                                ["phase"] = phase.ToString(),
                            }
                        );
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(phase),
                            phase,
                            "Unknown scene initialization phase."
                        );
                }

                scope.Success(
                    "Scene initialization phase completed.",
                    new Dictionary<string, object> { ["phase"] = phase.ToString() }
                );
            }
            catch (Exception ex)
            {
                logger?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Warning,
                    $"Scene initialization phase {phase} failed: {ex.Message}",
                    source: nameof(NPCSceneInitializationController),
                    data: new Dictionary<string, object>
                    {
                        ["phase"] = phase.ToString(),
                        ["exception"] = ex.ToString(),
                    }
                );
                scope.Warning(
                    $"Scene initialization phase {phase} failed: {ex.Message}",
                    new Dictionary<string, object> { ["phase"] = phase.ToString() }
                );
            }
        }

        /// <summary>
        /// Validates that all serialized references are assigned in the Inspector.
        /// Logs warnings for null references — the pipeline will skip their phases.
        /// Does NOT search for references via FindAnyObjectByType — scene wiring is mandatory.
        /// </summary>
        void ValidateSerializedReferences()
        {
            var missing = new List<string>();

            if (_flowLogger == null)
                missing.Add("_flowLogger (NPCFlowLogger)");
            // _networkBootstrap and others are optional depending on flags
            if (_networkBootstrap == null)
                missing.Add("_networkBootstrap (NPCNetworkBootstrap)");
            if (_dialogueManager == null)
                missing.Add("_dialogueManager (NPCDialogueManager)");
            if (_backendReadiness == null)
                missing.Add("_backendReadiness (NPCBackendReadinessService)");
            if (_networkBridge == null)
                missing.Add("_networkBridge (NPCDialogueNetworkBridge)");
            if (_smokeValidator == null)
                missing.Add("_smokeValidator (NPCDialogueSmokeValidator)");

            if (missing.Count == 0)
                return;

            string msg =
                "Scene initialization controller is missing serialized references: "
                + string.Join(", ", missing)
                + ". "
                + "Drag the required components into the Inspector slots. "
                + "FindAnyObjectByType is not used — all dependencies must be wired in the scene.";

            if (_flowLogger != null)
            {
                _flowLogger.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    msg,
                    source: nameof(NPCSceneInitializationController)
                );
            }
            else
            {
                Debug.LogWarning($"[{nameof(NPCSceneInitializationController)}] {msg}");
            }
        }

        static void LogMissingRef(NPCFlowLogger logger, string refName)
        {
            string msg =
                $"Cannot run phase because serialized reference {refName} is null. "
                + "Wire it in the Inspector — FindAnyObjectByType is not used.";

            if (logger != null)
            {
                logger.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    msg,
                    source: nameof(NPCSceneInitializationController)
                );
            }
            else
            {
                Debug.LogError($"[{nameof(NPCSceneInitializationController)}] {msg}");
            }
        }
    }
}
