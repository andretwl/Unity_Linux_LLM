using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;


using NPCSystem.Monitoring;
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
namespace NPCSystem.Items
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NPCPlayerInventory : NetworkBehaviour
    {
        public readonly NetworkList<FixedString64Bytes> itemIds = new NetworkList<FixedString64Bytes>();

        [SerializeField] private string lastInventoryStatus = "Empty";

        public IReadOnlyList<string> Items
        {
            get
            {
                var items = new List<string>(itemIds.Count);
                for (int i = 0; i < itemIds.Count; i++)
                {
                    items.Add(itemIds[i].ToString());
                }

                return items;
            }
        }

        string InventoryPreview => itemIds.Count == 0 ? "<empty>" : string.Join(", ", Items);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            itemIds.OnListChanged += HandleInventoryChanged;
            lastInventoryStatus = itemIds.Count == 0 ? "Empty" : $"Loaded {itemIds.Count} item(s).";
        }

        public override void OnNetworkDespawn()
        {
            itemIds.OnListChanged -= HandleInventoryChanged;
            base.OnNetworkDespawn();
        }

        public bool ServerTryAddItem(string itemId)
        {
            if (!IsServer)
            {
                return false;
            }

            string normalizedItemId = NormalizeItemId(itemId);
            if (string.IsNullOrWhiteSpace(normalizedItemId) || ContainsItem(normalizedItemId))
            {
                return false;
            }

            itemIds.Add(new FixedString64Bytes(normalizedItemId));
            return true;
        }

        public bool ServerTryRemoveItem(string itemId)
        {
            if (!IsServer)
            {
                return false;
            }

            string normalizedItemId = NormalizeItemId(itemId);
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (itemIds[i].ToString().Equals(normalizedItemId, System.StringComparison.OrdinalIgnoreCase))
                {
                    itemIds.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public bool ContainsItem(string itemId)
        {
            string normalizedItemId = NormalizeItemId(itemId);
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (itemIds[i].ToString().Equals(normalizedItemId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        void HandleInventoryChanged(NetworkListEvent<FixedString64Bytes> changeEvent)
        {
            lastInventoryStatus = itemIds.Count == 0
                ? "Empty"
                : $"{itemIds.Count} item(s): {string.Join(", ", Items)}";

            NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.OwnershipAuthority, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                $"Inventory updated for player {OwnerClientId}.",
                source: nameof(NPCPlayerInventory),
                data: new Dictionary<string, object>
                {
                    ["ownerClientId"] = OwnerClientId,
                    ["eventType"] = changeEvent.Type.ToString(),
                    ["itemCount"] = itemIds.Count
                });
        }

        static string NormalizeItemId(string itemId)
        {
            return string.IsNullOrWhiteSpace(itemId) ? string.Empty : itemId.Trim().ToLowerInvariant();
        }
    }
}
