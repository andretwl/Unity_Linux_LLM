using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NPCSystem
{
    [DefaultExecutionOrder(-2500)]
    [DisallowMultipleComponent]
    public class NPCNetworkBootstrap : MonoBehaviour
    {
        [Header("References")]
        public NetworkManager networkManager;
        public UnityTransport unityTransport;
        public GameObject playerPrefab;
        public string playerPrefabResourcesPath = "Networking/NPCPlayerAvatar";
        public GameObject serverNpcPrefab;
        public string serverNpcPrefabResourcesPath = "Networking/NPCServerCharacter";
        public GameObject transferableItemPrefab;
        public string transferableItemPrefabResourcesPath = "Networking/NPCTransferableItem";

        [Header("Transport")]
        public NPCTransportConfig transportConfig = default;
        public bool configureOnAwake = true;
        public bool autoStartInPlayMode = false;
        public bool autoAssignClientBindPort = true;
        public ushort clientBindPortOverride = 0;

        [Header("Runtime")]
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

            // Override from command-line args
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
            // Defer auto-start to Start() so NetworkManager's Awake (execution order 0)
            // has run and initialized its internal state (ConnectionManager, LocalClient, etc.).
            // Calling StartConfiguredMode in Awake at -2500 causes NRE in NetworkManager.StartServer.
            if ((autoStartInPlayMode || (Application.isBatchMode && transportConfig.autoStartMode != NPCNetworkAutoStartMode.Manual)) && Application.isPlaying)
            {
                StartConfiguredMode();
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

        /// <summary>
        /// Parse command-line args and override transport config / startup mode.
        /// Supports: -npc-server, -npc-host, -npc-client, -port N, -address ADDR
        /// </summary>
        void ApplyCommandLineOverrides()
        {
            if (!Application.isBatchMode && !Application.isEditor)
                return;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();

                if (arg == "-npc-server")
                {
                    transportConfig.autoStartMode = NPCNetworkAutoStartMode.Server;
                    NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                        "CLI override applied: autoStartMode = Server.", source: nameof(NPCNetworkBootstrap));
                }
                else if (arg == "-npc-websockets")
                {
                    transportConfig.useWebSockets = true;
                    NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                        "CLI override applied: useWebSockets = true.", source: nameof(NPCNetworkBootstrap));
                }
                else if (arg == "-npc-host")
                {
                    transportConfig.autoStartMode = NPCNetworkAutoStartMode.Host;
                    NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                        "CLI override applied: autoStartMode = Host.", source: nameof(NPCNetworkBootstrap));
                }
                else if (arg == "-npc-client")
                {
                    transportConfig.autoStartMode = NPCNetworkAutoStartMode.Client;
                    NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                        "CLI override applied: autoStartMode = Client.", source: nameof(NPCNetworkBootstrap));
                }
                else if (arg == "-port" && i + 1 < args.Length)
                {
                    if (ushort.TryParse(args[i + 1], out ushort port))
                    {
                        transportConfig.port = port;
                        NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                            $"CLI override applied: port = {port}.", source: nameof(NPCNetworkBootstrap));
                        i++;
                    }
                }
                else if (arg == "-address" && i + 1 < args.Length)
                {
                    transportConfig.connectAddress = args[i + 1];
                    NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                        $"CLI override applied: connectAddress = {transportConfig.connectAddress}.", source: nameof(NPCNetworkBootstrap));
                    i++;
                }
            }
        }

        public void ResolveReferences()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (networkManager == null)
            {
                networkManager = FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            }

            if (unityTransport == null)
            {
                unityTransport = GetComponent<UnityTransport>();
            }

            if (unityTransport == null && networkManager != null)
            {
                unityTransport = networkManager.GetComponent<UnityTransport>();
            }

            if (unityTransport == null)
            {
                unityTransport = FindAnyObjectByType<UnityTransport>(FindObjectsInactive.Include);
            }

            if (playerPrefab == null && !string.IsNullOrWhiteSpace(playerPrefabResourcesPath))
            {
                playerPrefab = Resources.Load<GameObject>(playerPrefabResourcesPath.Trim());
            }

            if (serverNpcPrefab == null && !string.IsNullOrWhiteSpace(serverNpcPrefabResourcesPath))
            {
                serverNpcPrefab = Resources.Load<GameObject>(serverNpcPrefabResourcesPath.Trim());
            }

            if (transferableItemPrefab == null && !string.IsNullOrWhiteSpace(transferableItemPrefabResourcesPath))
            {
                transferableItemPrefab = Resources.Load<GameObject>(transferableItemPrefabResourcesPath.Trim());
            }
        }

        public void ApplyTransportConfiguration()
        {
            ResolveReferences();

            if (unityTransport == null)
            {
                NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "Could not find a UnityTransport component.", source: nameof(NPCNetworkBootstrap));
                return;
            }

            if (networkManager != null)
            {
                networkManager.NetworkConfig.NetworkTransport = unityTransport;
                if (playerPrefab != null)
                {
                    networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
                }

                RegisterNetworkPrefabs();
            }

            transportConfig.NormalizeInPlace();
#if UNITY_WEBGL && !UNITY_EDITOR
            transportConfig.useWebSockets = true;
#endif
            if (!transportConfig.TryValidate(out string errorMessage))
            {
                NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    errorMessage, source: nameof(NPCNetworkBootstrap));
                return;
            }

            unityTransport.UseWebSockets = transportConfig.useWebSockets;

            UnityTransport.ConnectionAddressData connectionData = unityTransport.ConnectionData;
            connectionData.Address = transportConfig.connectAddress;
            connectionData.Port = transportConfig.port;
            connectionData.ServerListenAddress = transportConfig.listenAddress;
            connectionData.WebSocketPath = transportConfig.webSocketPath;
            connectionData.ClientBindPort = ResolveClientBindPort();
            unityTransport.ConnectionData = connectionData;

            NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "Transport configured.",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object>
                {
                    ["connectAddress"] = connectionData.Address,
                    ["port"] = connectionData.Port,
                    ["listenAddress"] = connectionData.ServerListenAddress,
                    ["clientBindPort"] = connectionData.ClientBindPort,
                    ["autoStartMode"] = transportConfig.autoStartMode.ToString(),
                    ["player"] = NPCPlayModeInstanceResolver.TryGetPlayerName(out string playerName) ? playerName : "unknown"
                });
        }

        public bool StartConfiguredMode()
        {
            ResolveReferences();

            if (networkManager == null)
            {
                NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "Could not find a NetworkManager component.", source: nameof(NPCNetworkBootstrap));
                return false;
            }

            if (networkManager.IsListening)
            {
                NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Skipped, NPCFlowLogLevel.Info,
                    "StartConfiguredMode skipped because NetworkManager is already listening.",
                    source: nameof(NPCNetworkBootstrap),
                    data: new Dictionary<string, object>
                    {
                        ["autoStartMode"] = transportConfig.autoStartMode.ToString()
                    });
                return true;
            }

            ApplyTransportConfiguration();

            NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Start, NPCFlowLogLevel.Info,
                "Starting configured network mode.",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object>
                {
                    ["autoStartMode"] = transportConfig.autoStartMode.ToString(),
                    ["connectAddress"] = transportConfig.connectAddress ?? string.Empty,
                    ["port"] = transportConfig.port,
                    ["listenAddress"] = transportConfig.listenAddress ?? string.Empty
                });

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

        ushort ResolveClientBindPort()
        {
            if (NPCPlayModeInstanceResolver.TryGetCommandLineClientBindPort(out ushort commandLineBindPort))
            {
                return commandLineBindPort;
            }

            if (!autoAssignClientBindPort)
            {
                return clientBindPortOverride;
            }

            if (!NPCPlayModeInstanceResolver.TryGetPlayerIndex(out int playerIndex))
            {
                return clientBindPortOverride;
            }

            return NPCPlayModeInstanceResolver.ResolveClientBindPortForPlayerIndex(
                playerIndex,
                transportConfig.port,
                clientBindPortOverride);
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

        public void RegisterNetworkPrefabs()
        {
            if (networkManager == null)
            {
                return;
            }

            TryRegisterNetworkPrefab(playerPrefab, "player");
            TryRegisterNetworkPrefab(serverNpcPrefab, "serverNpc");
            TryRegisterNetworkPrefab(transferableItemPrefab, "transferableItem");
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

        void HandleServerStarted()
        {
            _logger = NPCFlowLogger.FindOrCreate();
            _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                $"Server started. localClientId={networkManager.LocalClientId}, " +
                $"listenPort={unityTransport.ConnectionData.Port}, " +
                $"clientBindPort={unityTransport.ConnectionData.ClientBindPort}",
                source: nameof(NPCNetworkBootstrap));
        }

        void HandleClientConnected(ulong clientId)
        {
            _logger = NPCFlowLogger.FindOrCreate();
            _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                $"Client connected. localClientId={networkManager.LocalClientId}, " +
                $"connectedClientId={clientId}, " +
                $"isServer={networkManager.IsServer}, isClient={networkManager.IsClient}",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object>
                {
                    ["clientId"] = clientId
                });
        }

        void HandleClientDisconnected(ulong clientId)
        {
            _logger = NPCFlowLogger.FindOrCreate();
            string disconnectReason = networkManager != null ? networkManager.DisconnectReason : string.Empty;
            _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                $"Client disconnected. localClientId={networkManager.LocalClientId}, " +
                $"disconnectedClientId={clientId}, " +
                $"shutdownInProgress={networkManager.ShutdownInProgress}",
                source: nameof(NPCNetworkBootstrap),
                data: new Dictionary<string, object>
                {
                    ["clientId"] = clientId,
                    ["disconnectReason"] = disconnectReason ?? string.Empty
                });
        }

        void TryRegisterNetworkPrefab(GameObject prefab, string label)
        {
            if (prefab == null || networkManager == null)
            {
                return;
            }

            if (IsPrefabAlreadyRegistered(prefab))
            {
                _logger = NPCFlowLogger.FindOrCreate();
                _logger?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Skipped, NPCFlowLogLevel.Debug,
                    $"Skipped runtime registration for '{label}' because prefab '{prefab.name}' is already registered in NetworkConfig.",
                    source: nameof(NPCNetworkBootstrap),
                    data: new Dictionary<string, object>
                    {
                        ["label"] = label,
                        ["prefab"] = prefab.name
                    });
                return;
            }

            if (!prefab.TryGetComponent<NetworkObject>(out _))
            {
                _logger = NPCFlowLogger.FindOrCreate();
                _logger?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    $"Skipped network prefab registration for '{label}' because '{prefab.name}' has no NetworkObject.",
                    source: nameof(NPCNetworkBootstrap));
                return;
            }

            try
            {
                networkManager.PrefabHandler.AddNetworkPrefab(prefab);
                _logger = NPCFlowLogger.FindOrCreate();
                _logger?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Success, NPCFlowLogLevel.Debug,
                    $"Registered network prefab '{prefab.name}' for '{label}'.",
                    source: nameof(NPCNetworkBootstrap),
                    data: new Dictionary<string, object>
                    {
                        ["label"] = label,
                        ["prefab"] = prefab.name
                    });
            }
            catch (Exception ex)
            {
                _logger = NPCFlowLogger.FindOrCreate();
                _logger?.Log(NPCFlowStage.ConfigurationValidation, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    $"Failed to register network prefab '{prefab.name}' for '{label}': {ex.Message}",
                    source: nameof(NPCNetworkBootstrap),
                    data: new Dictionary<string, object>
                    {
                        ["label"] = label,
                        ["prefab"] = prefab.name
                    });
                throw;
            }
        }

        bool IsPrefabAlreadyRegistered(GameObject prefab)
        {
            if (prefab == null || networkManager == null)
            {
                return false;
            }

            NetworkPrefabs prefabs = networkManager.NetworkConfig?.Prefabs;
            if (prefabs == null)
            {
                return false;
            }

            if (prefabs.Contains(prefab))
            {
                return true;
            }

            if (networkManager.NetworkConfig.PlayerPrefab == prefab)
            {
                return true;
            }

            for (int listIndex = 0; listIndex < prefabs.NetworkPrefabsLists.Count; listIndex++)
            {
                NetworkPrefabsList list = prefabs.NetworkPrefabsLists[listIndex];
                if (list == null)
                {
                    continue;
                }

                for (int prefabIndex = 0; prefabIndex < list.PrefabList.Count; prefabIndex++)
                {
                    NetworkPrefab networkPrefab = list.PrefabList[prefabIndex];
                    if (networkPrefab != null && (networkPrefab.Prefab == prefab || networkPrefab.SourcePrefabToOverride == prefab))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
