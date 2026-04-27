// File: Assets/_Project/Gameplay/Inventory/InventoryItemDefinition.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Inventory
{
    public enum InventoryItemSize : byte
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    [CreateAssetMenu(menuName = "HueDoneIt/Inventory/Item Definition", fileName = "InventoryItemDefinition")]
    public sealed class InventoryItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string itemId = "item-id";
        [SerializeField] private string displayName = "Inventory Item";
        [SerializeField] private string shortDescription = "Used by maintenance tasks.";

        [Header("Rules")]
        [SerializeField] private InventoryItemSize size = InventoryItemSize.Small;

        [Header("UI")]
        [SerializeField] private Sprite icon;
        [SerializeField] private Color uiTint = Color.white;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string ShortDescription => shortDescription;
        public InventoryItemSize Size => size;
        public Sprite Icon => icon;
        public Color UiTint => uiTint;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                itemId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }
}
