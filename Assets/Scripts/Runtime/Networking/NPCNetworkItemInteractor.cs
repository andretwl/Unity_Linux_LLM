using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
{
    /// <summary>
    /// Handles item pickup and transfer between players/NPCs on the server side.
    ///
    /// Input sources can call the public/internal RPC-trigger methods directly,
    /// or let the component poll for input autonomously (legacy mode).
    /// New code should use direct calls via <see cref="NPCPlayerCharacterController"/>.
    /// </summary>
    [DefaultExecutionOrder(-340)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NPCPlayerInventory))]
    public sealed class NPCNetworkItemInteractor : NetworkBehaviour
    {
        [Header("References")]
        [FormerlySerializedAs("inventory")]
        [SerializeField]
        NPCPlayerInventory _inventory;

        [Header("Range")]
        [FormerlySerializedAs("PickupRange")]
        public float PickupRange = 3f;
        [FormerlySerializedAs("TransferRange")]
        public float TransferRange = 4f;

        void Awake()
        {
            _inventory = _inventory != null ? _inventory : GetComponent<NPCPlayerInventory>();
        }

        void Update()
        {
            // Input is now routed through NPCPlayerCharacterController event handlers.
            // This Update is intentionally left empty unless legacy polling is restored.
        }

        // \u2500\u2500\u2500 Public RPC-trigger methods (call from orchestrator) \u2500\u2500\u2500

        /// <summary>Pick up the nearest valid item on the server.</summary>
        [Rpc(SendTo.Server)]
        public void RequestPickupNearestItemServerRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            NPCTransferableItem item = FindNearestPickupCandidate(clientId);
            if (item == null)
            {
                LogInteractorEvent(
                    clientId,
                    NPCFlowStatus.Skipped,
                    "No pickup candidate in range."
                );
                return;
            }

            TransferItemToPlayer(item, clientId);
        }

        /// <summary>Give the held item to the nearest other player on the server.</summary>
        [Rpc(SendTo.Server)]
        public void RequestGiveHeldItemToNearestPlayerServerRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            NPCTransferableItem item = FindHeldItem(clientId);
            NPCPlayerNetworkAvatar targetPlayer = FindNearestOtherPlayer(clientId);
            if (item == null || targetPlayer == null)
            {
                LogInteractorEvent(
                    clientId,
                    NPCFlowStatus.Skipped,
                    "Give-to-player skipped because no held item or target player was found."
                );
                return;
            }

            TransferItemToPlayer(item, targetPlayer.OwnerClientId);
        }

        /// <summary>Give the held item to the nearest NPC on the server.</summary>
        [Rpc(SendTo.Server)]
        public void RequestGiveHeldItemToNearestNpcServerRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            NPCTransferableItem item = FindHeldItem(clientId);
            NPCServerCharacter npc = FindNearestNpc(clientId);
            if (item == null || npc == null)
            {
                LogInteractorEvent(
                    clientId,
                    NPCFlowStatus.Skipped,
                    "Give-to-NPC skipped because no held item or NPC was found."
                );
                return;
            }

            RemoveItemFromPlayer(item, clientId);
            item.AssignToNpc(npc.Slug);
        }

        // \u2500\u2500\u2500 Transfer Logic \u2500\u2500\u2500

        public void TransferItemToPlayer(NPCTransferableItem item, ulong targetClientId)
        {
            if (!Application.isPlaying)
            {
                AddItemToPlayer(item, targetClientId);
                return;
            }

            ulong previousOwnerClientId = item.OwnerClientId;
            bool hadPlayerOwner = item.IsHeldByPlayer;

            if (hadPlayerOwner)
            {
                RemoveItemFromPlayer(item, previousOwnerClientId);
            }

            item.AssignToPlayer(targetClientId);
            AddItemToPlayer(item, targetClientId);
        }

        void AddItemToPlayer(NPCTransferableItem item, ulong clientId)
        {
            NPCPlayerInventory targetInventory = FindInventory(clientId);
            targetInventory?.ServerTryAddItem(item.ItemId);

            NPCNetworkSessionManager sessionManager = FindAnyObjectByType<NPCNetworkSessionManager>(
                FindObjectsInactive.Include
            );
            sessionManager?.AddInventoryItem(clientId, item.ItemId);

            NPCDialogueNetworkBridge bridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(
                FindObjectsInactive.Include
            );
            bridge?.RefreshNotebookStateForClient(clientId);

            LogInteractorEvent(
                clientId,
                NPCFlowStatus.Success,
                $"Recorded item '{item.ItemId}' in player inventory."
            );
        }

        void RemoveItemFromPlayer(NPCTransferableItem item, ulong clientId)
        {
            NPCPlayerInventory sourceInventory = FindInventory(clientId);
            sourceInventory?.ServerTryRemoveItem(item.ItemId);

            NPCNetworkSessionManager sessionManager = FindAnyObjectByType<NPCNetworkSessionManager>(
                FindObjectsInactive.Include
            );
            sessionManager?.RemoveInventoryItem(clientId, item.ItemId);

            NPCDialogueNetworkBridge bridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(
                FindObjectsInactive.Include
            );
            bridge?.RefreshNotebookStateForClient(clientId);
        }

        // \u2500\u2500\u2500 Find helpers \u2500\u2500\u2500

        NPCTransferableItem FindNearestPickupCandidate(ulong clientId)
        {
            NPCPlayerNetworkAvatar avatar = FindPlayerAvatar(clientId);
            if (avatar == null)
                return null;

            NPCTransferableItem nearestItem = null;
            float nearestDistance = PickupRange;
            NPCTransferableItem[] items = FindObjectsByType<NPCTransferableItem>(
                FindObjectsInactive.Include
            );
            foreach (NPCTransferableItem item in items)
            {
                if (item == null || !item.IsSpawned || item.IsHeldByPlayer)
                    continue;

                float distance = Vector3.Distance(
                    avatar.transform.position,
                    item.transform.position
                );
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearestItem = item;
                }
            }

            return nearestItem;
        }

        NPCTransferableItem FindHeldItem(ulong clientId)
        {
            NPCTransferableItem[] items = FindObjectsByType<NPCTransferableItem>(
                FindObjectsInactive.Include
            );
            foreach (NPCTransferableItem item in items)
            {
                if (
                    item != null
                    && item.IsSpawned
                    && item.IsHeldByPlayer
                    && item.OwnerClientId == clientId
                )
                    return item;
            }

            return null;
        }

        NPCPlayerNetworkAvatar FindNearestOtherPlayer(ulong clientId)
        {
            NPCPlayerNetworkAvatar sourceAvatar = FindPlayerAvatar(clientId);
            if (sourceAvatar == null)
                return null;

            NPCPlayerNetworkAvatar nearestPlayer = null;
            float nearestDistance = TransferRange;
            NPCPlayerNetworkAvatar[] avatars = FindObjectsByType<NPCPlayerNetworkAvatar>(
                FindObjectsInactive.Include
            );
            foreach (NPCPlayerNetworkAvatar avatar in avatars)
            {
                if (avatar == null || avatar.OwnerClientId == clientId || !avatar.IsSpawned)
                    continue;

                float distance = Vector3.Distance(
                    sourceAvatar.transform.position,
                    avatar.transform.position
                );
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPlayer = avatar;
                }
            }

            return nearestPlayer;
        }

        NPCServerCharacter FindNearestNpc(ulong clientId)
        {
            NPCPlayerNetworkAvatar sourceAvatar = FindPlayerAvatar(clientId);
            if (sourceAvatar == null)
                return null;

            NPCServerCharacter nearestNpc = null;
            float nearestDistance = TransferRange;
            NPCServerCharacter[] npcs = FindObjectsByType<NPCServerCharacter>(
                FindObjectsInactive.Include
            );
            foreach (NPCServerCharacter npc in npcs)
            {
                if (npc == null || !npc.IsSpawned)
                    continue;

                float distance = Vector3.Distance(
                    sourceAvatar.transform.position,
                    npc.transform.position
                );
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearestNpc = npc;
                }
            }

            return nearestNpc;
        }

        NPCPlayerInventory FindInventory(ulong clientId)
        {
            NPCPlayerNetworkAvatar avatar = FindPlayerAvatar(clientId);
            return avatar != null ? avatar.GetComponent<NPCPlayerInventory>() : null;
        }

        NPCPlayerNetworkAvatar FindPlayerAvatar(ulong clientId)
        {
            NPCPlayerNetworkAvatar[] avatars = FindObjectsByType<NPCPlayerNetworkAvatar>(
                FindObjectsInactive.Include
            );
            foreach (NPCPlayerNetworkAvatar avatar in avatars)
            {
                if (avatar != null && avatar.IsSpawned && avatar.OwnerClientId == clientId)
                    return avatar;
            }

            return null;
        }

        void LogInteractorEvent(ulong clientId, NPCFlowStatus status, string message)
        {
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.OwnershipAuthority,
                    status,
                    NPCFlowLogLevel.Info,
                    message,
                    source: nameof(NPCNetworkItemInteractor),
                    data: new Dictionary<string, object> { ["clientId"] = clientId }
                );
        }
    }
}
