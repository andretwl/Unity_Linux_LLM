using System;
using System.Collections.Generic;
using EditorAttributes;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Void = EditorAttributes.Void;

namespace NPCSystem
{
    [DefaultExecutionOrder(-2500)]
    [DisallowMultipleComponent]
    public partial class NPCNetworkBootstrap : MonoBehaviour
    {
        [FoldoutGroup(
            "References",
            true,
            nameof(networkManager),
            nameof(unityTransport),
            nameof(playerPrefab),
            nameof(playerPrefabResourcesPath),
            nameof(serverNpcPrefab),
            nameof(serverNpcPrefabResourcesPath),
            nameof(transferableItemPrefab),
            nameof(transferableItemPrefabResourcesPath)
        )]
        [SerializeField]
        Void referencesGroup;

        [HideProperty]
        public NetworkManager networkManager;

        [HideProperty]
        public UnityTransport unityTransport;

        [HideProperty]
        public GameObject playerPrefab;

        [HideProperty]
        public string playerPrefabResourcesPath = "Networking/NPCPlayerAvatar";

        [HideProperty]
        public GameObject serverNpcPrefab;

        [HideProperty]
        public string serverNpcPrefabResourcesPath = "Networking/NPCServerCharacter";

        [HideProperty]
        public GameObject transferableItemPrefab;

        [HideProperty]
        public string transferableItemPrefabResourcesPath = "Networking/NPCTransferableItem";

        [FoldoutGroup(
            "Transport Settings",
            true,
            nameof(transportConfig),
            nameof(configureOnAwake),
            nameof(autoStartInPlayMode),
            nameof(autoAssignClientBindPort),
            nameof(clientBindPortOverride)
        )]
        [SerializeField]
        Void transportSettingsGroup;

        [HideProperty]
        public NPCTransportConfig transportConfig = default;

        [HideProperty]
        public bool configureOnAwake = true;

        [HideProperty]
        public bool autoStartInPlayMode = false;

        [HideProperty]
        public bool autoAssignClientBindPort = true;

        [HideProperty]
        [HideField(nameof(autoAssignClientBindPort))]
        public ushort clientBindPortOverride = 0;

        [FoldoutGroup("Runtime Settings", true, nameof(forceRunInBackground))]
        [SerializeField]
        Void runtimeSettingsGroup;

        [HideProperty]
        [Tooltip("Keeps network updates running when this instance is not the focused window.")]
        public bool forceRunInBackground = true;

        bool _callbacksRegistered;
        NPCFlowLogger _logger;

        void Reset()
        {
            transportConfig = NPCTransportConfig.CreateDefault();
            playerPrefabResourcesPath = "Networking/NPCPlayerAvatar";
            serverNpcPrefabResourcesPath = "Networking/NPCServerCharacter";
            transferableItemPrefabResourcesPath = "Networking/NPCTransferableItem";
            ResolveReferences();
        }

        void Awake()
        {
            if (transportConfig.port == 0)
            {
                transportConfig = NPCTransportConfig.CreateDefault();
            }

            ApplyCommandLineOverrides();
            ApplyRuntimeSettings();
            ResolveReferences();
            RegisterRuntimeCallbacks();

            if (configureOnAwake)
            {
                ApplyTransportConfiguration();
            }
        }

        void Start()
        {
            if (
                (
                    autoStartInPlayMode
                    || (
                        Application.isBatchMode
                        && transportConfig.autoStartMode != NPCNetworkAutoStartMode.Manual
                    )
                ) && Application.isPlaying
            )
            {
                StartConfiguredMode();
            }
        }

        void OnDestroy()
        {
            if (!_callbacksRegistered || networkManager == null)
            {
                return;
            }

            networkManager.OnServerStarted -= HandleServerStarted;
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _callbacksRegistered = false;
        }

        void OnValidate()
        {
            if (transportConfig.port == 0)
            {
                transportConfig = NPCTransportConfig.CreateDefault();
            }

            if (string.IsNullOrWhiteSpace(playerPrefabResourcesPath))
            {
                playerPrefabResourcesPath = "Networking/NPCPlayerAvatar";
            }

            if (string.IsNullOrWhiteSpace(serverNpcPrefabResourcesPath))
            {
                serverNpcPrefabResourcesPath = "Networking/NPCServerCharacter";
            }

            if (string.IsNullOrWhiteSpace(transferableItemPrefabResourcesPath))
            {
                transferableItemPrefabResourcesPath = "Networking/NPCTransferableItem";
            }

            transportConfig.NormalizeInPlace();

            if (!Application.isPlaying)
            {
                ResolveReferences();
            }
        }

        void ApplyRuntimeSettings()
        {
            if (!forceRunInBackground)
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
            if (_callbacksRegistered || networkManager == null)
            {
                return;
            }

            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _callbacksRegistered = true;
        }

        void HandleServerStarted()
        {
            _logger = NPCFlowLogger.FindOrCreate();
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Server started. localClientId={networkManager.LocalClientId}, "
                    + $"listenPort={unityTransport.ConnectionData.Port}, "
                    + $"clientBindPort={unityTransport.ConnectionData.ClientBindPort}",
                source: nameof(NPCNetworkBootstrap)
            );
        }

        void HandleClientConnected(ulong clientId)
        {
            _logger = NPCFlowLogger.FindOrCreate();
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Client connected. localClientId={networkManager.LocalClientId}, "
                    + $"connectedClientId={clientId}, "
                    + $"isServer={networkManager.IsServer}, isClient={networkManager.IsClient}",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object> { ["clientId"] = clientId }
            );
        }

        void HandleClientDisconnected(ulong clientId)
        {
            _logger = NPCFlowLogger.FindOrCreate();
            string disconnectReason =
                networkManager != null ? networkManager.DisconnectReason : string.Empty;
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Client disconnected. localClientId={networkManager.LocalClientId}, "
                    + $"disconnectedClientId={clientId}, "
                    + $"shutdownInProgress={networkManager.ShutdownInProgress}",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object>
                {
                    ["clientId"] = clientId,
                    ["disconnectReason"] = disconnectReason ?? string.Empty,
                }
            );
        }

        public bool StartConfiguredMode()
        {
            ResolveReferences();

            if (networkManager == null)
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

            if (networkManager.IsListening)
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
                            ["autoStartMode"] = transportConfig.autoStartMode.ToString(),
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
                        ["autoStartMode"] = transportConfig.autoStartMode.ToString(),
                        ["connectAddress"] = transportConfig.connectAddress ?? string.Empty,
                        ["port"] = transportConfig.port,
                        ["listenAddress"] = transportConfig.listenAddress ?? string.Empty,
                    }
                );

            switch (transportConfig.autoStartMode)
            {
                case NPCNetworkAutoStartMode.Client:
                    return networkManager.StartClient();
                case NPCNetworkAutoStartMode.Host:
                    return networkManager.StartHost();
                case NPCNetworkAutoStartMode.Server:
                    return networkManager.StartServer();
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
