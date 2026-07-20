using System;
using System.Collections.Generic;
using EditorAttributes;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Serialization;


using NPCSystem.Monitoring;
using NPCSystem.Monitoring.Datadog;
using NPCSystem.Dialogue.Core;
using NPCSystem.Network.Core;
using NPCSystem.Character.Player;
using NPCSystem.Auth;
using NPCSystem.Items;
using NPCSystem.LocalAI;
using NPCSystem.Initialization;
using NPCSystem.Character.NPC;
using NPCSystem.Dialogue.Session;
using NPCSystem.Dialogue.UI;
using NPCSystem.Dialogue.RAG;
using NPCSystem.Dialogue.Persistence;
namespace NPCSystem.Network.Core
{
    [DefaultExecutionOrder(-2500)]
    [DisallowMultipleComponent]
    public partial class NPCNetworkBootstrap : MonoBehaviour
    {
        [FoldoutGroup(
            "References",
            true,
            nameof(NetworkManager),
            nameof(UnityTransport),
            nameof(PlayerPrefab),
            nameof(PlayerPrefabResourcesPath),
            nameof(ServerNpcPrefab),
            nameof(ServerNpcPrefabResourcesPath)
        )]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [FormerlySerializedAs("networkManager")]
        [HideProperty]
        public NetworkManager NetworkManager;

        [FormerlySerializedAs("unityTransport")]
        [HideProperty]
        public UnityTransport UnityTransport;

        [FormerlySerializedAs("playerPrefab")]
        [HideProperty]
        public GameObject PlayerPrefab;

        [FormerlySerializedAs("playerPrefabResourcesPath")]
        [HideProperty]
        public string PlayerPrefabResourcesPath = "Networking/NPCPlayerAvatar";

        [FormerlySerializedAs("serverNpcPrefab")]
        [HideProperty]
        public GameObject ServerNpcPrefab;

        [FormerlySerializedAs("serverNpcPrefabResourcesPath")]
        [HideProperty]
        public string ServerNpcPrefabResourcesPath = "Networking/NPCServerCharacter";

        [FoldoutGroup(
            "Transport Settings",
            true,
            nameof(TransportConfig),
            nameof(ConfigureOnAwake),
            nameof(AutoStartInPlayMode),
            nameof(AutoAssignClientBindPort),
            nameof(ClientBindPortOverride)
        )]
        [SerializeField]
        EditorAttributes.Void transportSettingsGroup;

        [FormerlySerializedAs("transportConfig")]
        [HideProperty]
        public NPCTransportConfig TransportConfig = default;

        [FormerlySerializedAs("configureOnAwake")]
        [HideProperty]
        public bool ConfigureOnAwake = true;

        [FormerlySerializedAs("autoStartInPlayMode")]
        [HideProperty]
        public bool AutoStartInPlayMode = false;

        [FormerlySerializedAs("autoAssignClientBindPort")]
        [HideProperty]
        public bool AutoAssignClientBindPort = true;

        [FormerlySerializedAs("clientBindPortOverride")]
        [HideProperty]
        [HideField(nameof(AutoAssignClientBindPort))]
        public ushort ClientBindPortOverride = 0;

        [FoldoutGroup("Runtime Settings", true, nameof(ForceRunInBackground))]
        [SerializeField]
        EditorAttributes.Void runtimeSettingsGroup;

        [FormerlySerializedAs("forceRunInBackground")]
        [HideProperty]
        [Tooltip("Keeps network updates running when this instance is not the focused window.")]
        public bool ForceRunInBackground = true;

        bool _callbacksRegistered;
        NPCFlowLogger _logger;

        void Reset()
        {
            TransportConfig = NPCTransportConfig.CreateDefault();
            PlayerPrefabResourcesPath = "Networking/NPCPlayerAvatar";
            ServerNpcPrefabResourcesPath = "Networking/NPCServerCharacter";
            ResolveReferences();
        }

        void Awake()
        {
            if (TransportConfig.Port == 0)
            {
                TransportConfig = NPCTransportConfig.CreateDefault();
            }

            // Detect dedicated server CLI arg and configure mode (not start)
            if (Application.isBatchMode && HasCommandLineArg("-npc-server"))
            {
                TransportConfig.AutoStartMode = NPCNetworkAutoStartMode.Server;
            }

            ApplyCommandLineOverrides();
            ApplyRuntimeSettings();
            ResolveReferences();
            RegisterRuntimeCallbacks();

            if (ConfigureOnAwake)
            {
                ApplyTransportConfiguration();
            }
        }

        void Start()
        {
            // Intentionally empty — network start is sequenced by NPCSceneInitializationController
            // Phase 8 (Spawning). This ensures all services initialize before networking starts.
        }

        static bool HasCommandLineArg(string argName)
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (string.Equals(arg, argName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void OnDestroy()
        {
            if (!_callbacksRegistered || NetworkManager == null)
            {
                return;
            }

            NetworkManager.OnServerStarted -= HandleServerStarted;
            NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _callbacksRegistered = false;
        }

        void OnValidate()
        {
            if (TransportConfig.Port == 0)
            {
                TransportConfig = NPCTransportConfig.CreateDefault();
            }

            if (string.IsNullOrWhiteSpace(PlayerPrefabResourcesPath))
            {
                PlayerPrefabResourcesPath = "Networking/NPCPlayerAvatar";
            }

            if (string.IsNullOrWhiteSpace(ServerNpcPrefabResourcesPath))
            {
                ServerNpcPrefabResourcesPath = "Networking/NPCServerCharacter";
            }

            TransportConfig.NormalizeInPlace();

            if (!Application.isPlaying)
            {
                ResolveReferences();
            }
        }

        void ApplyRuntimeSettings()
        {
            if (!ForceRunInBackground)
            {
                return;
            }

            if (!Application.runInBackground)
            {
                Application.runInBackground = true;
            }
        }

        void RegisterRuntimeCallbacks()
        {
            if (_callbacksRegistered || NetworkManager == null)
            {
                return;
            }

            NetworkManager.OnServerStarted += HandleServerStarted;
            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _callbacksRegistered = true;
        }

        void HandleServerStarted()
        {
            _logger = NPCFlowLogger.FindOrCreate();
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Server started. localClientId={NetworkManager.LocalClientId}, "
                    + $"listenPort={UnityTransport.ConnectionData.Port}, "
                    + $"clientBindPort={UnityTransport.ConnectionData.ClientBindPort}",
                source: nameof(NPCNetworkBootstrap)
            );

            DatadogMetricsService.Increment(
                "network.server.started",
                tags: new[]
                {
                    $"listen_port:{UnityTransport.ConnectionData.Port}",
                    $"transport:{UnityTransport.Protocol.ToString() ?? "unknown"}",
                }
            );
        }

        void HandleClientConnected(ulong clientId)
        {
            _logger = NPCFlowLogger.FindOrCreate();
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Client connected. localClientId={NetworkManager.LocalClientId}, "
                    + $"connectedClientId={clientId}, "
                    + $"isServer={NetworkManager.IsServer}, isClient={NetworkManager.IsClient}",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object> { ["clientId"] = clientId }
            );

            DatadogMetricsService.Increment(
                "network.client.connected",
                tags: new[]
                {
                    $"is_server:{NetworkManager.IsServer}",
                    $"is_client:{NetworkManager.IsClient}",
                }
            );
        }

        void HandleClientDisconnected(ulong clientId)
        {
            _logger = NPCFlowLogger.FindOrCreate();
            string disconnectReason =
                NetworkManager != null ? NetworkManager.DisconnectReason : string.Empty;
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Client disconnected. localClientId={NetworkManager.LocalClientId}, "
                    + $"disconnectedClientId={clientId}, "
                    + $"shutdownInProgress={NetworkManager.ShutdownInProgress}",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object>
                {
                    ["clientId"] = clientId,
                    ["disconnectReason"] = disconnectReason ?? string.Empty,
                }
            );

            DatadogMetricsService.Increment(
                "network.client.disconnected",
                tags: new[]
                {
                    $"reason:{(!string.IsNullOrEmpty(disconnectReason) ? "explicit" : "unknown")}",
                }
            );
        }

        public bool StartConfiguredMode()
        {
            ResolveReferences();

            using var netSpan = DatadogTracer.StartSpan(
                "network.start",
                service: "unity-dedicated-server",
                resource: TransportConfig.AutoStartMode.ToString(),
                type: "networking",
                tags: new[]
                {
                    $"mode:{TransportConfig.AutoStartMode}",
                    $"port:{TransportConfig.Port}",
                }
            );

            if (NetworkManager == null)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.NetworkHost,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        "Could not find a NetworkManager component.",
                        source: nameof(NPCNetworkBootstrap)
                    );
                return false;
            }

            if (NetworkManager.IsListening)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.NetworkHost,
                        NPCFlowStatus.Skipped,
                        NPCFlowLogLevel.Info,
                        "StartConfiguredMode skipped because NetworkManager is already listening.",
                        source: nameof(NPCNetworkBootstrap),
                        data: new Dictionary<string, object>
                        {
                            ["autoStartMode"] = TransportConfig.AutoStartMode.ToString(),
                        }
                    );
                return true;
            }

            ApplyTransportConfiguration();

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Info,
                    "Starting configured network mode.",
                    source: nameof(NPCNetworkBootstrap),
                    data: new Dictionary<string, object>
                    {
                        ["autoStartMode"] = TransportConfig.AutoStartMode.ToString(),
                        ["connectAddress"] = TransportConfig.ConnectAddress ?? string.Empty,
                        ["port"] = TransportConfig.Port,
                        ["listenAddress"] = TransportConfig.ListenAddress ?? string.Empty,
                    }
                );

            switch (TransportConfig.AutoStartMode)
            {
                case NPCNetworkAutoStartMode.Client:
                    DatadogMetricsService.Increment(
                        "network.mode.start",
                        tags: new[] { "mode:client" }
                    );
                    bool clientStarted = NetworkManager.StartClient();
                    netSpan.SetTag("started", clientStarted.ToString());
                    netSpan.SetTag("status", clientStarted ? "success" : "failed");
                    return clientStarted;
                case NPCNetworkAutoStartMode.Host:
                    DatadogMetricsService.Increment(
                        "network.mode.start",
                        tags: new[] { "mode:host" }
                    );
                    bool hostStarted = NetworkManager.StartHost();
                    netSpan.SetTag("started", hostStarted.ToString());
                    netSpan.SetTag("status", hostStarted ? "success" : "failed");
                    return hostStarted;
                case NPCNetworkAutoStartMode.Server:
                    DatadogMetricsService.Increment(
                        "network.mode.start",
                        tags: new[] { "mode:server" }
                    );
                    bool serverStarted = NetworkManager.StartServer();
                    netSpan.SetTag("started", serverStarted.ToString());
                    netSpan.SetTag("status", serverStarted ? "success" : "failed");
                    return serverStarted;
                default:
                    netSpan.SetTag("status", "unknown_mode");
                    return false;
            }
        }

        [ContextMenu("Start Configured Mode")]
        void StartConfiguredModeFromContextMenu()
        {
            StartConfiguredMode();
        }
    }
}
