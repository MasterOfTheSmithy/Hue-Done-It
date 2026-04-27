// File: Assets/_Project/Tasks/TaskStepInteractable.cs
using HueDoneIt.Gameplay.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class TaskStepInteractable : NetworkInteractable
    {
        [SerializeField] private TaskObjectiveBase ownerTask;
        [SerializeField] private string stepId = "step";
        [SerializeField] private string interactPrompt = "Interact";

        public TaskObjectiveBase OwnerTask => ownerTask;
        public string StepId => stepId;

        public override bool CanInteract(in InteractionContext context)
        {
            return ownerTask != null && ownerTask.CanUseStep(stepId, context);
        }

        public override string GetPromptText(in InteractionContext context)
        {
            if (ownerTask == null)
            {
                return "Unwired Task Step";
            }

            return ownerTask.GetPromptForStep(stepId, context, interactPrompt);
        }

        public override bool TryInteract(in InteractionContext context)
        {
            return ownerTask != null && ownerTask.ServerTryInteractStep(stepId, context);
        }
    }
}
