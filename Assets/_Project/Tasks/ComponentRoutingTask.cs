// File: Assets/_Project/Tasks/ComponentRoutingTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Inventory;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class ComponentRoutingTask : TaskObjectiveBase
    {
        [Header("Step IDs")]
        [SerializeField] private string dispenserStepId = "dispenser";
        [SerializeField] private string installStepId = "install";

        [Header("Required Item")]
        [SerializeField] private InventoryItemDefinition requiredComponent;

        [Header("Optional Reward")]
        [SerializeField] private FloodZone rewardFloodZone;

        protected override void OnServerResetTask()
        {
            string itemName = requiredComponent != null ? requiredComponent.DisplayName : "Component";
            SetObjective($"Collect {itemName} from the dispenser.");
            SetStatus("This repair requires a physical component.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            if (requiredComponent == null)
            {
                return "Missing component definition";
            }

            if (string.Equals(stepId, dispenserStepId))
            {
                return CurrentStepIndex == 0
                    ? $"Collect {requiredComponent.DisplayName}"
                    : $"{requiredComponent.DisplayName} already collected";
            }

            if (string.Equals(stepId, installStepId))
            {
                return CurrentStepIndex == 0
                    ? $"Install point // bring {requiredComponent.DisplayName}"
                    : $"Install {requiredComponent.DisplayName}";
            }

            return fallbackPrompt;
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            if (requiredComponent == null || context.InteractorObject == null)
            {
                RegisterFailure("Invalid component routing setup.");
                return false;
            }

            if (!context.InteractorObject.TryGetComponent(out PlayerInventoryState inventory))
            {
                RegisterFailure("No inventory found on interacting player.");
                return false;
            }

            if (CurrentStepIndex == 0)
            {
                if (!string.Equals(stepId, dispenserStepId))
                {
                    RegisterFailure("Wrong station. Retrieve the component first.");
                    return false;
                }

                if (!inventory.ServerTryAddItem(requiredComponent, out string reason))
                {
                    SetStatus(reason);
                    return false;
                }

                SetObjective($"Install {requiredComponent.DisplayName} at the receiver.");
                SetStatus($"{requiredComponent.DisplayName} secured in inventory.");
                AdvanceToNextStep($"Install {requiredComponent.DisplayName} at the receiver.", $"{requiredComponent.DisplayName} secured in inventory.");
                return true;
            }

            if (!string.Equals(stepId, installStepId))
            {
                RegisterFailure("Wrong receiver. Take the component to the correct install point.");
                return false;
            }

            if (!inventory.HasItem(requiredComponent.ItemId))
            {
                RegisterFailure($"Install attempted without {requiredComponent.DisplayName}.");
                return false;
            }

            inventory.ServerTryConsumeItem(requiredComponent.ItemId);
            if (rewardFloodZone != null)
            {
                rewardFloodZone.TrySetState(FloodZoneState.SealedSafe);
            }

            CompleteTask($"{requiredComponent.DisplayName} installed successfully.");
            return true;
        }

        protected override void OnLockedOut()
        {
            if (rewardFloodZone != null)
            {
                rewardFloodZone.TrySetState(FloodZoneState.Flooding);
            }
        }
    }
}
