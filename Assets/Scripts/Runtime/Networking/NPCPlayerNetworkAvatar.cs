using Unity.Netcode;
using UnityEngine;

namespace NPCSystem
{
    [RequireComponent(typeof(NetworkObject))]
    public class NPCPlayerNetworkAvatar : NetworkBehaviour
    {
        /// <summary>
        /// Register string serialization with NGO so NetworkVariable<string> works
        /// without requiring source-generator codegen.
        /// </summary>
        static NPCPlayerNetworkAvatar()
        {
            UserNetworkVariableSerialization<string>.WriteValue = (FastBufferWriter writer, in string value) =>
            {
                writer.WriteValueSafe(value);
            };
            UserNetworkVariableSerialization<string>.ReadValue = (FastBufferReader reader, out string value) =>
            {
                reader.ReadValueSafe(out value);
            };
            UserNetworkVariableSerialization<string>.DuplicateValue = (in string value, ref string duplicatedValue) =>
            {
                duplicatedValue = value;
            };
        }

        /// <summary>
        /// Server-authoritative player display name, synced to all clients.
        /// NPCs read this to customize dialogue responses.
        /// </summary>
        public NetworkVariable<string> playerDisplayName = new NetworkVariable<string>(
            "",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

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

            // Server: set default fallback name if not set by the bridge
            if (IsServer && string.IsNullOrEmpty(playerDisplayName.Value))
            {
                playerDisplayName.Value = $"Player {OwnerClientId}";
            }

            // Client owner: auto-register the authenticated player name with the server
            if (IsOwner && !IsServer)
            {
                string pendingName = AuthNetworkBridge.ActivePlayerName;
                if (!string.IsNullOrEmpty(pendingName) &&
                    !string.Equals(pendingName, "Player", System.StringComparison.OrdinalIgnoreCase))
                {
                    RegisterPlayerNameServerRpc(pendingName);
                }
            }
        }

        /// <summary>
        /// Server-only: set the player's display name on the synced NetworkVariable.
        /// Called either by AuthNetworkBridge (for the host) or via RPC (for clients).
        /// </summary>
        public void SetDisplayName(string name)
        {
            if (!IsServer) return;
            playerDisplayName.Value = name?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Client sends their authenticated player name to the server after connecting.
        /// </summary>
        [Rpc(SendTo.Server)]
        void RegisterPlayerNameServerRpc(string name, RpcParams rpcParams = default)
        {
            // Only the owner of this avatar can set their own name
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning($"[NPCPlayerNetworkAvatar] Rejected name registration from client {rpcParams.Receive.SenderClientId} (avatar owner is {OwnerClientId}).");
                return;
            }

            string sanitized = name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = $"Player {OwnerClientId}";
            }

            playerDisplayName.Value = sanitized;
            Debug.Log($"[NPCPlayerNetworkAvatar] Registered player name '{sanitized}' for client {OwnerClientId}.");
        }
    }
}
