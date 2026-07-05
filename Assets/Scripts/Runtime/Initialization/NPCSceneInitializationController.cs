using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

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
        Spawning
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
            NPCSceneInitializationPhase.Spawning
        };

        [Header("References")]
        public NPCFlowLogger flowLogger;
        public NPCNetworkBootstrap networkBootstrap;
        public NPCDialogueManager dialogueManager;
        public NPCBackendReadinessService backendReadiness;
        public NPCDialogueNetworkBridge networkBridge;
        public NPCDialogueSmokeValidator smokeValidator;

        [Header("Startup")]
        public bool initializeOnStart = true;
        public bool configureNetworkTransport = false;
        [Tooltip("If true, initializes the dialogue manager immediately during scene start. Set to false to delay initialization until after player login (recommended for WebGL memory-smart start).")]
        public bool initializeDialogueManager = false;
        public bool verifyBackendsDuringInitialization = true;
        public bool initializeNetworkBridge = true;
        public bool validateAfterInitialization = true;
        public bool startNetworkingAfterInitialization = false;

        bool _started;
        Task _initializationTask;

        public bool IsInitialized => _initializationTask != null && _initializationTask.IsCompletedSuccessfully;
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
            if (!initializeOnStart) return;
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

        async Task InitializeSceneInternalAsync()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            foreach (NPCSceneInitializationPhase phase in OrderedPhases)
            {
                await RunPhaseAsync(phase);
            }
        }

        async Task RunPhaseAsync(NPCSceneInitializationPhase phase)
        {
            NPCFlowLogger logger = flowLogger != null ? flowLogger : NPCFlowLogger.FindOrCreate();
            using var scope = NPCFlowScope.Start(logger, NPCFlowStage.SceneBootstrap, source: nameof(NPCSceneInitializationController), data: new Dictionary<string, object>
            {
                ["phase"] = phase.ToString()
            });

            try
            {
                switch (phase)
                {
                    case NPCSceneInitializationPhase.Logger:
                        flowLogger = logger;
                        break;
                    case NPCSceneInitializationPhase.SceneReferences:
                        ResolveReferences();
                        break;
                    case NPCSceneInitializationPhase.NetworkTransport:
                        if (configureNetworkTransport && networkBootstrap != null)
                        {
                            networkBootstrap.ApplyTransportConfiguration();
                        }
                        break;
                    case NPCSceneInitializationPhase.DialogueServices:
                        if (initializeDialogueManager && dialogueManager != null)
                        {
                            await dialogueManager.InitializeAsync();
                        }
                        break;
                    case NPCSceneInitializationPhase.BackendReadiness:
                        if (verifyBackendsDuringInitialization && backendReadiness != null)
                        {
                            bool probeLocalAi = initializeDialogueManager && (dialogueManager != null && dialogueManager.initializeOnStart);
                            await backendReadiness.ProbeAsync(probeLocalAi);
                        }
                        break;
                    case NPCSceneInitializationPhase.NetworkBridge:
                        if (initializeNetworkBridge && networkBridge != null)
                        {
                            await networkBridge.InitializeAsync();
                        }
                        break;
                    case NPCSceneInitializationPhase.Validation:
                        if (validateAfterInitialization && smokeValidator != null)
                        {
                            smokeValidator.ValidateConfiguration();
                        }
                        break;
                    case NPCSceneInitializationPhase.Spawning:
                        if (startNetworkingAfterInitialization && networkBootstrap != null)
                        {
                            bool skipForBatchmodeBootstrap = Application.isBatchMode &&
                                networkBootstrap.transportConfig.autoStartMode != NPCNetworkAutoStartMode.Manual;

                            if (skipForBatchmodeBootstrap)
                            {
                                logger.Log(NPCFlowStage.SceneBootstrap,
                                    NPCFlowStatus.Skipped,
                                    NPCFlowLogLevel.Info,
                                    "Skipped scene initialization network start because batchmode bootstrap auto-start is active.",
                                    source: nameof(NPCSceneInitializationController),
                                    data: new Dictionary<string, object>
                                    {
                                        ["phase"] = phase.ToString(),
                                        ["autoStartMode"] = networkBootstrap.transportConfig.autoStartMode.ToString()
                                    });
                                break;
                            }

                            bool started = networkBootstrap.StartConfiguredMode();
                            logger.Log(NPCFlowStage.SceneBootstrap,
                                started ? NPCFlowStatus.Success : NPCFlowStatus.Skipped,
                                started ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                                started ? "NetworkManager started from scene initialization controller." : "NetworkManager start skipped by scene initialization controller.",
                                source: nameof(NPCSceneInitializationController),
                                data: new Dictionary<string, object> { ["phase"] = phase.ToString() });
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown scene initialization phase.");
                }

                scope.Success("Scene initialization phase completed.", new Dictionary<string, object>
                {
                    ["phase"] = phase.ToString()
                });
            }
            catch (Exception ex)
            {
                scope.Error(ex, "Scene initialization phase failed.", new Dictionary<string, object>
                {
                    ["phase"] = phase.ToString()
                });
                throw;
            }
        }

        public void ResolveReferences()
        {
            if (flowLogger == null)
            {
                flowLogger = NPCFlowLogger.FindOrCreate();
            }

            if (networkBootstrap == null)
            {
                networkBootstrap = FindAnyObjectByType<NPCNetworkBootstrap>(FindObjectsInactive.Include);
            }

            if (dialogueManager == null)
            {
                dialogueManager = FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
            }

            if (networkBridge == null)
            {
                networkBridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(FindObjectsInactive.Include);
            }

            if (backendReadiness == null)
            {
                backendReadiness = FindAnyObjectByType<NPCBackendReadinessService>(FindObjectsInactive.Include);
            }

            if (smokeValidator == null)
            {
                smokeValidator = FindAnyObjectByType<NPCDialogueSmokeValidator>(FindObjectsInactive.Include);
            }
        }
    }
}
