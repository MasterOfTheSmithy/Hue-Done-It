// File: Assets/_Project/Gameplay/Inventory/PlayerInventoryState.cs
using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Inventory
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerInventoryState : NetworkBehaviour
    {
        public const int MaxSlots = 3;

        [SerializeField] private InventoryItemDefinition[] itemCatalog = Array.Empty<InventoryItemDefinition>();

        private static InventoryItemDefinition[] _runtimeCatalog = Array.Empty<InventoryItemDefinition>();

        private readonly NetworkVariable<FixedString64Bytes> _slot0 =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _slot1 =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _slot2 =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action InventoryChanged;

        public bool HasFreeSlot =>
            string.IsNullOrEmpty(_slot0.Value.ToString()) ||
            string.IsNullOrEmpty(_slot1.Value.ToString()) ||
            string.IsNullOrEmpty(_slot2.Value.ToString());

        public int FilledSlotCount
        {
            get
            {
                int count = 0;
                if (!string.IsNullOrEmpty(_slot0.Value.ToString()))
                {
                    count++;
                }

                if (!string.IsNullOrEmpty(_slot1.Value.ToString()))
                {
                    count++;
                }

                if (!string.IsNullOrEmpty(_slot2.Value.ToString()))
                {
                    count++;
                }

                return count;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _slot0.OnValueChanged += HandleSlotChanged;
            _slot1.OnValueChanged += HandleSlotChanged;
            _slot2.OnValueChanged += HandleSlotChanged;

            if ((itemCatalog == null || itemCatalog.Length == 0) && _runtimeCatalog.Length > 0)
            {
                itemCatalog = _runtimeCatalog;
            }
        }

        public override void OnNetworkDespawn()
        {
            _slot0.OnValueChanged -= HandleSlotChanged;
            _slot1.OnValueChanged -= HandleSlotChanged;
            _slot2.OnValueChanged -= HandleSlotChanged;
            base.OnNetworkDespawn();
        }

        public void ConfigureRuntimeCatalog(InventoryItemDefinition[] catalog)
        {
            InventoryItemDefinition[] resolvedCatalog = catalog ?? Array.Empty<InventoryItemDefinition>();
            itemCatalog = resolvedCatalog;
            _runtimeCatalog = resolvedCatalog;
            InventoryChanged?.Invoke();
        }

        public void ServerClearAll()
        {
            if (!IsServer)
            {
                return;
            }

            _slot0.Value = default;
            _slot1.Value = default;
            _slot2.Value = default;
            InventoryChanged?.Invoke();
        }

        public InventoryItemDefinition GetItemInSlot(int index)
        {
            return index switch
            {
                0 => ResolveDefinition(_slot0.Value.ToString()),
                1 => ResolveDefinition(_slot1.Value.ToString()),
                2 => ResolveDefinition(_slot2.Value.ToString()),
                _ => null
            };
        }

        public bool HasItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            return string.Equals(_slot0.Value.ToString(), itemId, StringComparison.Ordinal) ||
                   string.Equals(_slot1.Value.ToString(), itemId, StringComparison.Ordinal) ||
                   string.Equals(_slot2.Value.ToString(), itemId, StringComparison.Ordinal);
        }

        public bool ServerTryAddItem(InventoryItemDefinition itemDefinition, out string reason)
        {
            reason = string.Empty;

            if (!IsServer)
            {
                reason = "Inventory add must happen on server.";
                return false;
            }

            if (itemDefinition == null)
            {
                reason = "Invalid item.";
                return false;
            }

            if (itemDefinition.Size == InventoryItemSize.Large)
            {
                reason = $"{itemDefinition.DisplayName} is too large to store.";
                return false;
            }

            if (HasItem(itemDefinition.ItemId))
            {
                reason = $"{itemDefinition.DisplayName} already held.";
                return false;
            }

            if (TryWriteSlot0(ref reason, itemDefinition.ItemId))
            {
                return true;
            }

            if (TryWriteSlot1(ref reason, itemDefinition.ItemId))
            {
                return true;
            }

            if (TryWriteSlot2(ref reason, itemDefinition.ItemId))
            {
                return true;
            }

            reason = "Inventory full.";
            return false;
        }

        public bool ServerTryConsumeItem(string itemId)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            if (TryClearSlot0(itemId))
            {
                return true;
            }

            if (TryClearSlot1(itemId))
            {
                return true;
            }

            if (TryClearSlot2(itemId))
            {
                return true;
            }

            return false;
        }

        public bool ServerTryDropSlot(int slotIndex, Vector3 dropPosition, out string reason)
        {
            reason = string.Empty;
            if (!IsServer)
            {
                reason = "Inventory drop must happen on server.";
                return false;
            }

            if (slotIndex < 0 || slotIndex >= MaxSlots)
            {
                reason = "Invalid inventory slot.";
                return false;
            }

            string itemId = GetRawItemIdInSlot(slotIndex);
            if (string.IsNullOrWhiteSpace(itemId))
            {
                reason = "Inventory slot is empty.";
                return false;
            }

            InventoryItemDefinition definition = ResolveDefinition(itemId);
            if (definition == null)
            {
                reason = "Dropped item definition is missing.";
                return false;
            }

            if (!TryClearSlot(slotIndex, itemId))
            {
                reason = "Inventory slot changed before drop.";
                return false;
            }

            SpawnDroppedPickup(definition, dropPosition);
            InventoryChanged?.Invoke();
            return true;
        }

        public string BuildInventorySummary()
        {
            string slot0 = BuildSlotLine(0);
            string slot1 = BuildSlotLine(1);
            string slot2 = BuildSlotLine(2);
            return $"{slot0}\n{slot1}\n{slot2}";
        }

        private string BuildSlotLine(int index)
        {
            string rawItemId = GetRawItemIdInSlot(index);
            if (string.IsNullOrWhiteSpace(rawItemId))
            {
                return $"{index + 1}. [Empty]";
            }

            InventoryItemDefinition definition = ResolveDefinition(rawItemId);
            return definition == null
                ? $"{index + 1}. {FormatFallbackItemName(rawItemId)}"
                : $"{index + 1}. {definition.DisplayName}";
        }

        private string GetRawItemIdInSlot(int index)
        {
            return index switch
            {
                0 => _slot0.Value.ToString(),
                1 => _slot1.Value.ToString(),
                2 => _slot2.Value.ToString(),
                _ => string.Empty
            };
        }

        private static string FormatFallbackItemName(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return "[Unknown Item]";
            }

            string cleaned = itemId.Replace('-', ' ').Replace('_', ' ').Trim();
            if (cleaned.Length == 0)
            {
                return itemId;
            }

            char[] chars = cleaned.ToCharArray();
            bool capitalizeNext = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsWhiteSpace(chars[i]))
                {
                    capitalizeNext = true;
                    continue;
                }

                chars[i] = capitalizeNext ? char.ToUpperInvariant(chars[i]) : chars[i];
                capitalizeNext = false;
            }

            return new string(chars);
        }

        private bool TryWriteSlot0(ref string reason, string itemId)
        {
            if (!string.IsNullOrEmpty(_slot0.Value.ToString()))
            {
                return false;
            }

            _slot0.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString64(itemId);
            reason = string.Empty;
            return true;
        }

        private bool TryWriteSlot1(ref string reason, string itemId)
        {
            if (!string.IsNullOrEmpty(_slot1.Value.ToString()))
            {
                return false;
            }

            _slot1.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString64(itemId);
            reason = string.Empty;
            return true;
        }

        private bool TryWriteSlot2(ref string reason, string itemId)
        {
            if (!string.IsNullOrEmpty(_slot2.Value.ToString()))
            {
                return false;
            }

            _slot2.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString64(itemId);
            reason = string.Empty;
            return true;
        }

        private bool TryClearSlot0(string itemId)
        {
            if (!string.Equals(_slot0.Value.ToString(), itemId, StringComparison.Ordinal))
            {
                return false;
            }

            _slot0.Value = default;
            return true;
        }

        private bool TryClearSlot1(string itemId)
        {
            if (!string.Equals(_slot1.Value.ToString(), itemId, StringComparison.Ordinal))
            {
                return false;
            }

            _slot1.Value = default;
            return true;
        }

        private bool TryClearSlot2(string itemId)
        {
            if (!string.Equals(_slot2.Value.ToString(), itemId, StringComparison.Ordinal))
            {
                return false;
            }

            _slot2.Value = default;
            return true;
        }

        private bool TryClearSlot(int slotIndex, string itemId)
        {
            return slotIndex switch
            {
                0 => TryClearSlot0(itemId),
                1 => TryClearSlot1(itemId),
                2 => TryClearSlot2(itemId),
                _ => false
            };
        }

        private void SpawnDroppedPickup(InventoryItemDefinition definition, Vector3 dropPosition)
        {
            GameObject pickupObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pickupObject.name = "Dropped_" + definition.ItemId;
            pickupObject.transform.position = dropPosition + Vector3.up * 0.35f;
            pickupObject.transform.localScale = definition.Size == InventoryItemSize.Medium
                ? new Vector3(0.7f, 0.55f, 0.7f)
                : new Vector3(0.52f, 0.42f, 0.52f);

            NetworkObject networkObject = pickupObject.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = pickupObject.AddComponent<NetworkObject>();
            }

            NetworkInventoryPickup pickup = pickupObject.AddComponent<NetworkInventoryPickup>();
            pickup.ConfigureRuntime(definition, "Pick Up", pickupObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = pickupObject.GetComponent<BoxCollider>() ?? pickupObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !networkObject.IsSpawned)
            {
                networkObject.Spawn(destroyWithScene: true);
            }
        }

        private InventoryItemDefinition ResolveDefinition(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            InventoryItemDefinition definition = ResolveDefinitionFromCatalog(itemId, itemCatalog);
            if (definition != null)
            {
                return definition;
            }

            return ResolveDefinitionFromCatalog(itemId, _runtimeCatalog);
        }

        private static InventoryItemDefinition ResolveDefinitionFromCatalog(string itemId, InventoryItemDefinition[] catalog)
        {
            if (catalog == null)
            {
                return null;
            }

            for (int i = 0; i < catalog.Length; i++)
            {
                InventoryItemDefinition candidate = catalog[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.ItemId, itemId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void HandleSlotChanged(FixedString64Bytes _, FixedString64Bytes __)
        {
            InventoryChanged?.Invoke();
        }
    }
}
