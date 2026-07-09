using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NPCSystem
{
    /// <content>Transport configuration and CLI argument parsing extracted from NPCNetworkBootstrap.</content>
    public partial class NPCNetworkBootstrap
    {
        /// <summary>
        /// Parse command-line args and override transport config / startup mode.
        /// Supports: -npc-server, -npc-host, -npc-client, -port N, -address ADDR
        /// </summary>
        void ApplyCommandLineOverrides()
        {
            if (!Application.isBatchMode && !Application.isEditor)
                return;

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();

                if (arg == "-npc-server")
                {
                    transportConfig.autoStartMode = NPCNetworkAutoStartMode.Server;
                    NPCFlowLogger
                        .FindOrCreate()
                        ?.Log(
                            NPCFlowStage.ConfigurationValidation,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            "CLI override applied: autoStartMode = Server.",
                            source: nameof(NPCNetworkBootstrap)
                        );
                }
                else if (arg == "-npc-websockets")
                {
                    transportConfig.useWebSockets = true;
                    NPCFlowLogger
                        .FindOrCreate()
                        ?.Log(
                            NPCFlowStage.ConfigurationValidation,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            "CLI override applied: useWebSockets = true.",
                            source: nameof(NPCNetworkBootstrap)
                        );
                }
                else if (arg == "-npc-host")
                {
                    transportConfig.autoStartMode = NPCNetworkAutoStartMode.Host;
                    NPCFlowLogger
                        .FindOrCreate()
                        ?.Log(
                            NPCFlowStage.ConfigurationValidation,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            "CLI override applied: autoStartMode = Host.",
                            source: nameof(NPCNetworkBootstrap)
                        );
                }
                else if (arg == "-npc-client")
                {
                    transportConfig.autoStartMode = NPCNetworkAutoStartMode.Client;
                    NPCFlowLogger
                        .FindOrCreate()
                        ?.Log(
                            NPCFlowStage.ConfigurationValidation,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            "CLI override applied: autoStartMode = Client.",
                            source: nameof(NPCNetworkBootstrap)
                        );
                }
                else if (arg == "-port" && i + 1 < args.Length)
                {
                    if (ushort.TryParse(args[i + 1], out ushort port))
                    {
                        transportConfig.port = port;
                        NPCFlowLogger
                            .FindOrCreate()
                            ?.Log(
                                NPCFlowStage.ConfigurationValidation,
                                NPCFlowStatus.Success,
                                NPCFlowLogLevel.Info,
                                $"CLI override applied: port = {port}.",
                                source: nameof(NPCNetworkBootstrap)
                            );
                        i++;
                    }
                }
                else if (arg == "-address" && i + 1 < args.Length)
                {
                    transportConfig.connectAddress = args[i + 1];
                    NPCFlowLogger
                        .FindOrCreate()
                        ?.Log(
                            NPCFlowStage.ConfigurationValidation,
                            NPCFlowStatus.Success,
                            NPCFlowLogLevel.Info,
                            $"CLI override applied: connectAddress = {transportConfig.connectAddress}.",
                            source: nameof(NPCNetworkBootstrap)
                        );
                    i++;
                }
            }
        }

        public void ApplyTransportConfiguration()
        {
            ResolveReferences();

            if (unityTransport == null)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.ConfigurationValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        "Could not find a UnityTransport component.",
                        source: nameof(NPCNetworkBootstrap)
                    );
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
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.ConfigurationValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        errorMessage,
                        source: nameof(NPCNetworkBootstrap)
                    );
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

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "Transport configured.",
                    source: nameof(NPCNetworkBootstrap),
                    data: new Dictionary<string, object>
                    {
                        ["connectAddress"] = connectionData.Address,
                        ["port"] = connectionData.Port,
                        ["listenAddress"] = connectionData.ServerListenAddress,
                        ["clientBindPort"] = connectionData.ClientBindPort,
                        ["autoStartMode"] = transportConfig.autoStartMode.ToString(),
                        ["player"] = NPCPlayModeInstanceResolver.TryGetPlayerName(
                            out string playerName
                        )
                            ? playerName
                            : "unknown",
                    }
                );
        }

        ushort ResolveClientBindPort()
        {
            if (
                NPCPlayModeInstanceResolver.TryGetCommandLineClientBindPort(
                    out ushort commandLineBindPort
                )
            )
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
                clientBindPortOverride
            );
        }
    }
}
