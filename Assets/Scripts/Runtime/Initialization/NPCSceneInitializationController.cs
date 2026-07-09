using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
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

        [Header("References")]
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

        [Header("Startup")]
        [FormerlySerializedAs("InitializeOnStart")]
        [SerializeField]
        bool _initializeOnStart = true;
        [FormerlySerializedAs("ConfigureNetworkTransport")]
        [SerializeField]
        bool _configureNetworkTransport = false;

        [Tooltip(
            "If true, initializes the dialogue manager immediately during scene start. Set to false to delay initialization until after player login (recommended for WebGL memory-smart start)."
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
        Task _initializationTask;

        public bool IsInitialized =>
            _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
        public Task InitializationTask => _initializationTask;

        void Reset()
        {
            ResolveReferences();
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ResolveReferences();
            }
        }

        void Awake()
        {
            ResolveReferences();
        }

        async void Start()
        {
            if (!_initializeOnStart)
                return;
            if (ShouldDeferInitializationForWebGL())
            {
                _flowLogger = _flowLogger != null ? _flowLogger : NPCFlowLogger.FindOrCreate();
                _flowLogger.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Info,
                    "Deferred automatic scene initialization for WebGL startup to avoid browser bootstrap instability. Call InitializeSceneAsync after the page finishes loading and the player is ready.",
                    source: nameof(NPCSceneInitializationController)
                );
                return;
            }
            await InitializeSceneAsync();
        }

        [ContextMenu("Initialize Scene")]
        public async void InitializeSceneFromContextMenu()
        {
            await InitializeSceneAsync();
        }

        public Task InitializeSceneAsync()
        {
            _initializationTask ??= InitializeSceneInternalAsync();
            return _initializationTask;
        }

        bool ShouldDeferInitializationForWebGL()
        {
            return Application.platform == RuntimePlatform.WebGLPlayer;
        }

        async Task InitializeSceneInternalAsync()
        {
            if (_started)
                return;
            _started = true;

            foreach (NPCSceneInitializationPhase phase in OrderedPhases)
            {
                await RunPhaseAsync(phase);
            }
        }

        async Task RunPhaseAsync(NPCSceneInitializationPhase phase)
        {
            NPCFlowLogger logger = _flowLogger ?? NPCFlowLogger.FindOrCreate();
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
                        _flowLogger = logger;
                        break;

                    case NPCSceneInitializationPhase.SceneReferences:
                        ResolveReferences();
                        break;

                    case NPCSceneInitializationPhase.NetworkTransport:
                        if (_configureNetworkTransport && _networkBootstrap != null)
                        {
                            _networkBootstrap.ApplyTransportConfiguration();
                        }
                        break;

                    case NPCSceneInitializationPhase.DialogueServices:
                        if (_initializeDialogueManager && _dialogueManager != null)
                        {
                            await _dialogueManager.InitializeAsync();
                        }
                        break;

                    case NPCSceneInitializationPhase.BackendReadiness:
                        if (_verifyBackendsDuringInitialization && _backendReadiness != null)
                        {
                            bool probeLocalAi =
                                _initializeDialogueManager
                                && (_dialogueManager != null && _dialogueManager.InitializeOnStart);
                            await _backendReadiness.ProbeAsync(probeLocalAi);
                        }
                        break;

                    case NPCSceneInitializationPhase.NetworkBridge:
                        if (_initializeNetworkBridge && _networkBridge != null)
                        {
                            await _networkBridge.InitializeAsync();
                        }
                        break;

                    case NPCSceneInitializationPhase.Validation:
                        if (_validateAfterInitialization && _smokeValidator != null)
                        {
                            if (_dialogueManager != null && !_dialogueManager.IsInitialized)
                            {
                                logger.Log(
                                    NPCFlowStage.SceneBootstrap,
                                    NPCFlowStatus.Skipped,
                                    NPCFlowLogLevel.Info,
                                    "Skipped scene initialization smoke validation because dialogue manager is not initialized yet (deferred loading active).",
                                    source: nameof(NPCSceneInitializationController),
                                    data: new Dictionary<string, object>
                                    {
                                        ["phase"] = phase.ToString(),
                                    }
                                );
                            }
                            else
                            {
                                _smokeValidator.ValidateConfiguration();
                            }
                        }
                        break;

                    case NPCSceneInitializationPhase.Spawning:
                        if (_startNetworkingAfterInitialization && _networkBootstrap != null)
                        {
                            bool skipForBatchmodeBootstrap =
                                Application.isBatchMode
                                && _networkBootstrap.TransportConfig.autoStartMode
                                    != NPCNetworkAutoStartMode.Manual;

                            if (skipForBatchmodeBootstrap)
                            {
                                logger.Log(
                                    NPCFlowStage.SceneBootstrap,
                                    NPCFlowStatus.Skipped,
                                    NPCFlowLogLevel.Info,
                                    "Skipped scene initialization network start because batchmode bootstrap auto-start is active.",
                                    source: nameof(NPCSceneInitializationController),
                                    data: new Dictionary<string, object>
                                    {
                                        ["phase"] = phase.ToString(),
                                        ["autoStartMode"] =
                                            _networkBootstrap.TransportConfig.autoStartMode.ToString(),
                                    }
                                );
                                break;
                            }

                            bool started = _networkBootstrap.StartConfiguredMode();
                            logger.Log(
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
                        }
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
                logger.Log(
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

        public void ResolveReferences()
        {
            if (_flowLogger == null)
                _flowLogger = NPCFlowLogger.FindOrCreate();

            if (_networkBootstrap == null)
                _networkBootstrap = FindAnyObjectByType<NPCNetworkBootstrap>(
                    FindObjectsInactive.Include
                );

            if (_dialogueManager == null)
                _dialogueManager = FindAnyObjectByType<NPCDialogueManager>(
                    FindObjectsInactive.Include
                );

            if (_networkBridge == null)
                _networkBridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(
                    FindObjectsInactive.Include
                );

            if (_backendReadiness == null)
                _backendReadiness = FindAnyObjectByType<NPCBackendReadinessService>(
                    FindObjectsInactive.Include
                );

            if (_smokeValidator == null)
                _smokeValidator = FindAnyObjectByType<NPCDialogueSmokeValidator>(
                    FindObjectsInactive.Include
                );
        }
    }
}
