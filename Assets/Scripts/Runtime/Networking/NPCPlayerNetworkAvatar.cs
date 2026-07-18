using Unity.Netcode;
using UnityEngine;

namespace NPCSystem
{
    [RequireComponent(typeof(NetworkObject))]
    public class NPCPlayerNetworkAvatar : NetworkBehaviour
    {
        /// <summary>
        /// Server-authoritative player display name, synced to all clients.
        /// NPCs read this to customize dialogue responses.
        /// </summary>
        public NetworkVariable<string> playerDisplayName = new NetworkVariable<string>(
            "",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public ulong PlayerId => OwnerClientId;

        /// <summary>
        /// The player's name, or a fallback "Player {OwnerClientId}" if not set.
        /// </summary>
        public string DisplayName
        {
            get
            {
                string name = playerDisplayName.Value;
                return string.IsNullOrEmpty(name) ? $"Player {OwnerClientId}" : name;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            playerDisplayName.OnValueChanged += HandleDisplayNameChanged;
            RefreshHierarchyName();
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.PlayerSpawn,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    $"Player avatar spawned for client {OwnerClientId}.",
                    source: nameof(NPCPlayerNetworkAvatar),
                    data: new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["ownerClientId"] = OwnerClientId,
                        ["isOwner"] = IsOwner,
                        ["isServer"] = IsServer,
                    }
                );

            // Server: set default fallback name if not set by the bridge
            if (IsServer && string.IsNullOrEmpty(playerDisplayName.Value))
            {
                playerDisplayName.Value = $"Player {OwnerClientId}";
            }

            // Client owner: auto-register the authenticated player name with the server
            if (IsOwner && !IsServer)
            {
                string pendingName = AuthNetworkBridge.ActivePlayerName;
                if (
                    !string.IsNullOrEmpty(pendingName)
                    && !string.Equals(
                        pendingName,
                        "Player",
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    RegisterPlayerNameServerRpc(pendingName);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            playerDisplayName.OnValueChanged -= HandleDisplayNameChanged;
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.PlayerSpawn,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Info,
                    $"Player avatar despawned for client {OwnerClientId}.",
                    source: nameof(NPCPlayerNetworkAvatar),
                    data: new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["ownerClientId"] = OwnerClientId,
                    }
                );
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Server-only: set the player's display name on the synced NetworkVariable.
        /// Called either by AuthNetworkBridge (for the host) or via RPC (for clients).
        /// </summary>
        public void SetDisplayName(string name)
        {
            if (!IsServer)
                return;
            playerDisplayName.Value = name?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Client sends their authenticated player name to the server after connecting.
        /// </summary>
        [Rpc(SendTo.Server)]
        void RegisterPlayerNameServerRpc(string name, RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.RpcTraffic,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Info,
                    "Received player-name registration RPC.",
                    source: nameof(NPCPlayerNetworkAvatar),
                    data: new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["senderClientId"] = rpcParams.Receive.SenderClientId,
                        ["ownerClientId"] = OwnerClientId,
                        ["requestedName"] = name ?? string.Empty,
                    }
                );

            // Only the owner of this avatar can set their own name
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                NPCFlowLogger
                    .FindOrCreate()
                    ?.Log(
                        NPCFlowStage.OwnershipAuthority,
                        NPCFlowStatus.Warning,
                        NPCFlowLogLevel.Warning,
                        "Rejected player-name registration RPC because sender is not the avatar owner.",
                        source: nameof(NPCPlayerNetworkAvatar),
                        data: new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["senderClientId"] = rpcParams.Receive.SenderClientId,
                            ["ownerClientId"] = OwnerClientId,
                        }
                    );
                return;
            }

            string sanitized = name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = $"Player {OwnerClientId}";
            }

            playerDisplayName.Value = sanitized;
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.PlayerNameRegistration,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    $"Registered player name '{sanitized}' for client {OwnerClientId}.",
                    source: nameof(NPCPlayerNetworkAvatar),
                    data: new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["ownerClientId"] = OwnerClientId,
                        ["registeredName"] = sanitized,
                    }
                );
        }

        void HandleDisplayNameChanged(string _, string __)
        {
            RefreshHierarchyName();
        }

        void RefreshHierarchyName()
        {
            gameObject.name = $"NPCPlayerAvatar_Client{OwnerClientId}_{DisplayName}";
        }
    }
}
