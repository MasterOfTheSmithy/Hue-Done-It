// File: Assets/_Project/Gameplay/Inventory/NetworkInventoryPickup.cs
using HueDoneIt.Gameplay.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Inventory
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkInventoryPickup : NetworkInteractable
    {
        [SerializeField] private InventoryItemDefinition itemDefinition;
        [SerializeField] private string pickupPrompt = "Pick Up";
        [SerializeField] private Renderer visualRenderer;
        [SerializeField] private Color availableColor = new(0.22f, 0.95f, 0.65f, 1f);
        [SerializeField] private Color unavailableColor = new(1f, 0.35f, 0.25f, 1f);

        private MaterialPropertyBlock _block;

        public void ConfigureRuntime(InventoryItemDefinition definition, string prompt, Renderer renderer = null)
        {
            itemDefinition = definition;
            pickupPrompt = string.IsNullOrWhiteSpace(prompt) ? pickupPrompt : prompt;
            visualRenderer = renderer != null ? renderer : GetComponentInChildren<Renderer>();
            ApplyVisual(availableColor);
        }

        public override bool CanInteract(in InteractionContext context)
        {
            if (itemDefinition == null || context.InteractorObject == null)
            {
                return false;
            }

            return context.InteractorObject.TryGetComponent(out PlayerInventoryState _);
        }

        public override string GetPromptText(in InteractionContext context)
        {
            if (itemDefinition == null)
            {
                return "Invalid Pickup";
            }

            if (itemDefinition.Size == InventoryItemSize.Large)
            {
                return $"{itemDefinition.DisplayName}: Too large to inventory";
            }

            if (context.InteractorObject == null || !context.InteractorObject.TryGetComponent(out PlayerInventoryState inventory))
            {
                return $"{itemDefinition.DisplayName}: No inventory";
            }

            if (!inventory.HasFreeSlot)
            {
                return $"{itemDefinition.DisplayName}: Inventory full";
            }

            return $"{pickupPrompt} ({itemDefinition.DisplayName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || itemDefinition == null || context.InteractorObject == null)
            {
                return false;
            }

            if (!context.InteractorObject.TryGetComponent(out PlayerInventoryState inventory))
            {
                return false;
            }

            if (!inventory.ServerTryAddItem(itemDefinition, out string _))
            {
                ApplyVisual(unavailableColor);
                return false;
            }

            ApplyVisual(unavailableColor);

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
            else
            {
                gameObject.SetActive(false);
            }

            return true;
        }

        protected override void Awake()
        {
            base.Awake();
            ApplyVisual(availableColor);
        }

        private void ApplyVisual(Color color)
        {
            if (visualRenderer == null)
            {
                visualRenderer = GetComponentInChildren<Renderer>();
            }

            if (visualRenderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            visualRenderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            visualRenderer.SetPropertyBlock(_block);
        }
    }
}
