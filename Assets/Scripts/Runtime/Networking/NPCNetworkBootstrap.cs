using System.Collections.Generic;
using EditorAttributes;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
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
            nameof(ServerNpcPrefabResourcesPath),
            nameof(TransferableItemPrefab),
            nameof(TransferableItemPrefabResourcesPath)
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

        [FormerlySerializedAs("transferableItemPrefab")]
        [HideProperty]
        public GameObject TransferableItemPrefab;

        [FormerlySerializedAs("transferableItemPrefabResourcesPath")]
        [HideProperty]
        public string TransferableItemPrefabResourcesPath = "Networking/NPCTransferableItem";

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
            TransferableItemPrefabResourcesPath = "Networking/NPCTransferableItem";
            ResolveReferences();
        }

        void Awake()
        {
            if (TransportConfig.port == 0)
            {
                TransportConfig = NPCTransportConfig.CreateDefault();
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
            if (
                (
                    AutoStartInPlayMode
                    || (
                        Application.isBatchMode
                        && TransportConfig.autoStartMode != NPCNetworkAutoStartMode.Manual
                    )
                ) && Application.isPlaying
            )
            {
                StartConfiguredMode();
            }
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
            if (TransportConfig.port == 0)
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

            if (string.IsNullOrWhiteSpace(TransferableItemPrefabResourcesPath))
            {
                TransferableItemPrefabResourcesPath = "Networking/NPCTransferableItem";
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
                            ["autoStartMode"] = TransportConfig.autoStartMode.ToString(),
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
                        ["autoStartMode"] = TransportConfig.autoStartMode.ToString(),
                        ["connectAddress"] = TransportConfig.connectAddress ?? string.Empty,
                        ["port"] = TransportConfig.port,
                        ["listenAddress"] = TransportConfig.listenAddress ?? string.Empty,
                    }
                );

            switch (TransportConfig.autoStartMode)
            {
                case NPCNetworkAutoStartMode.Client:
                    DatadogMetricsService.Increment(
                        "network.mode.start",
                        tags: new[] { "mode:client" }
                    );
                    return NetworkManager.StartClient();
                case NPCNetworkAutoStartMode.Host:
                    DatadogMetricsService.Increment(
                        "network.mode.start",
                        tags: new[] { "mode:host" }
                    );
                    return NetworkManager.StartHost();
                case NPCNetworkAutoStartMode.Server:
                    DatadogMetricsService.Increment(
                        "network.mode.start",
                        tags: new[] { "mode:server" }
                    );
                    return NetworkManager.StartServer();
                default:
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
