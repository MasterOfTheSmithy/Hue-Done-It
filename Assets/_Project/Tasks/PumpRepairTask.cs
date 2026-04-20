// File: Assets/_Project/Tasks/PumpRepairTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class PumpRepairTask : NetworkRepairTask, IRepairTaskFloodSafetyProvider
    {
        [Header("Pump Presentation")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color idleColor = new(1f, 0.85f, 0.2f);
        [SerializeField] private Color activeColor = new(0.2f, 0.65f, 1f);
        [SerializeField] private Color completedColor = new(0.2f, 1f, 0.35f);
        [SerializeField] private Color cancelledColor = new(1f, 0.4f, 0.2f);

        [Header("Flood Hook")]
        [SerializeField] private FloodZone linkedFloodZone;

        private MaterialPropertyBlock _propertyBlock;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            TaskStateChanged += HandleTaskStateChanged;
            ApplyStateVisual(CurrentState);
        }

        public override void OnNetworkDespawn()
        {
            TaskStateChanged -= HandleTaskStateChanged;
            base.OnNetworkDespawn();
        }

        public bool IsTaskEnvironmentSafe(NetworkRepairTask task)
        {
            return linkedFloodZone == null || linkedFloodZone.IsSafe;
        }

        protected override string GetPromptForState(in InteractionContext context)
        {
            if (CurrentState == RepairTaskState.Completed)
            {
                return "Pump Repaired";
            }

            if (CurrentState == RepairTaskState.InProgress)
            {
                return ActiveClientId == context.InteractorClientId
                    ? "Repairing Pump..."
                    : "Pump Busy";
            }

            if (!IsTaskEnvironmentSafe(this))
            {
                return "Pump Unsafe: Zone flooded";
            }

            return "Repair Pump";
        }

        protected override void OnTaskCompleted()
        {
            base.OnTaskCompleted();
            ApplyStateVisual(RepairTaskState.Completed);
        }

        protected override void OnTaskCancelled(string reason)
        {
            base.OnTaskCancelled(reason);
            ApplyStateVisual(RepairTaskState.Cancelled);
        }

        protected override void OnTaskStarted(in InteractionContext context)
        {
            base.OnTaskStarted(context);
            ApplyStateVisual(RepairTaskState.InProgress);
        }

        private void HandleTaskStateChanged(RepairTaskState _, RepairTaskState current)
        {
            ApplyStateVisual(current);
        }

        private void ApplyStateVisual(RepairTaskState state)
        {
            if (statusRenderer == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            statusRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_BaseColor", GetColorForState(state));
            _propertyBlock.SetColor("_Color", GetColorForState(state));
            statusRenderer.SetPropertyBlock(_propertyBlock);
        }

        private Color GetColorForState(RepairTaskState state)
        {
            return state switch
            {
                RepairTaskState.InProgress => activeColor,
                RepairTaskState.Completed => completedColor,
                RepairTaskState.Cancelled => cancelledColor,
                _ => idleColor
            };
        }
    }
}
