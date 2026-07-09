using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NPCSystem
{
    /// <content>Network prefab reference resolution and registration extracted from NPCNetworkBootstrap.</content>
    public partial class NPCNetworkBootstrap
    {
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

            if (
                transferableItemPrefab == null
                && !string.IsNullOrWhiteSpace(transferableItemPrefabResourcesPath)
            )
            {
                transferableItemPrefab = Resources.Load<GameObject>(
                    transferableItemPrefabResourcesPath.Trim()
                );
            }
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

        void TryRegisterNetworkPrefab(GameObject prefab, string label)
        {
            if (prefab == null || networkManager == null)
            {
                return;
            }

            if (IsPrefabAlreadyRegistered(prefab))
            {
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.ConfigurationValidation,
                        NPCFlowStatus.Skipped,
                        NPCFlowLogLevel.Debug,
                        $"Skipped runtime registration for '{label}' because prefab '{prefab.name}' is already registered in NetworkConfig.",
                        source: nameof(NPCNetworkBootstrap),
                        data: new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["label"] = label,
                            ["prefab"] = prefab.name,
                        }
                    );
                return;
            }

            if (!prefab.TryGetComponent<NetworkObject>(out _))
            {
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.ConfigurationValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Skipped network prefab registration for '{label}' because '{prefab.name}' has no NetworkObject.",
                        source: nameof(NPCNetworkBootstrap)
                    );
                return;
            }

            try
            {
                networkManager.PrefabHandler.AddNetworkPrefab(prefab);
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.ConfigurationValidation,
                        NPCFlowStatus.Success,
                        NPCFlowLogLevel.Debug,
                        $"Registered network prefab '{prefab.name}' for '{label}'.",
                        source: nameof(NPCNetworkBootstrap),
                        data: new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["label"] = label,
                            ["prefab"] = prefab.name,
                        }
                    );
            }
            catch (Exception ex)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.ConfigurationValidation,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Failed to register network prefab '{prefab.name}' for '{label}': {ex.Message}",
                        source: nameof(NPCNetworkBootstrap),
                        data: new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["label"] = label,
                            ["prefab"] = prefab.name,
                        }
                    );
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
                    if (
                        networkPrefab != null
                        && (
                            networkPrefab.Prefab == prefab
                            || networkPrefab.SourcePrefabToOverride == prefab
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
