// File: Assets/_Project/Tasks/StabilizerRealignmentTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class StabilizerRealignmentTask : TaskObjectiveBase
    {
        [Header("Step IDs")]
        [SerializeField] private string drainStepId = "drain";
        [SerializeField] private string armAStepId = "arm_a";
        [SerializeField] private string armBStepId = "arm_b";
        [SerializeField] private string armCStepId = "arm_c";
        [SerializeField] private string lockStepId = "lock";

        [Header("World Impact")]
        [SerializeField] private FloodZone linkedFloodZone;

        protected override void OnServerResetTask()
        {
            SetObjective("Drain the stabilizer chamber.");
            SetStatus("Begin by draining the chamber manifold.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            string expected = GetExpectedStepId();
            if (string.Equals(stepId, expected))
            {
                return $"{GetStepLabel(stepId)} // Correct next step";
            }

            return $"Out of order // {DisplayName} may destabilize";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            string expected = GetExpectedStepId();
            if (!string.Equals(stepId, expected))
            {
                if (linkedFloodZone != null)
                {
                    linkedFloodZone.TrySetState(linkedFloodZone.CurrentState == FloodZoneState.Dry
                        ? FloodZoneState.Wet
                        : FloodZoneState.Flooding);
                }

                RegisterFailure("Stabilizer order error. Pressure spiked.");
                return false;
            }

            switch (CurrentStepIndex)
            {
                case 0:
                    AdvanceToNextStep("Align stabilizer arm A.", "Chamber drained.");
                    return true;

                case 1:
                    AdvanceToNextStep("Align stabilizer arm B.", "Arm A aligned.");
                    return true;

                case 2:
                    AdvanceToNextStep("Align stabilizer arm C.", "Arm B aligned.");
                    return true;

                case 3:
                    AdvanceToNextStep("Lock the stabilizer array at the control console.", "Arm C aligned.");
                    return true;

                case 4:
                    if (linkedFloodZone != null)
                    {
                        linkedFloodZone.TrySetState(FloodZoneState.SealedSafe);
                    }

                    CompleteTask("Stabilizers realigned and locked.");
                    return true;
            }

            RegisterFailure("Unexpected stabilizer state.");
            return false;
        }

        protected override void OnLockedOut()
        {
            if (linkedFloodZone != null)
            {
                linkedFloodZone.TrySetState(FloodZoneState.Submerged);
            }
        }

        private string GetExpectedStepId()
        {
            return CurrentStepIndex switch
            {
                0 => drainStepId,
                1 => armAStepId,
                2 => armBStepId,
                3 => armCStepId,
                _ => lockStepId
            };
        }

        private string GetStepLabel(string stepId)
        {
            if (string.Equals(stepId, drainStepId))
            {
                return "Drain Chamber";
            }

            if (string.Equals(stepId, armAStepId))
            {
                return "Align Arm A";
            }

            if (string.Equals(stepId, armBStepId))
            {
                return "Align Arm B";
            }

            if (string.Equals(stepId, armCStepId))
            {
                return "Align Arm C";
            }

            if (string.Equals(stepId, lockStepId))
            {
                return "Lock Array";
            }

            return stepId;
        }
    }
}
