// File: Assets/_Project/Tasks/HullPatchTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class HullPatchTask : TaskObjectiveBase
    {
        [Header("Patch Order")]
        [SerializeField] private string[] requiredStepOrder = { "scrape", "foam", "plate", "seal" };

        [Header("World Impact")]
        [SerializeField] private FloodZone[] stabilizedZones = new FloodZone[0];
        [SerializeField] private FloodZone[] failureZones = new FloodZone[0];
        [SerializeField] private NetworkBleachLeakHazard[] suppressedHazardsOnComplete = new NetworkBleachLeakHazard[0];
        [SerializeField, Min(0f)] private float hazardSuppressionSeconds = 28f;
        [SerializeField] private bool completeSetsSafe = true;

        protected override void OnServerResetTask()
        {
            SetObjective(BuildObjectiveText());
            SetStatus("Patch the hull in order. Incorrect patching opens flood seams.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            string expected = GetExpectedStepId();
            return string.Equals(stepId, expected)
                ? $"Patch {FormatStep(expected)} // Correct"
                : $"Wrong patch step // expected {FormatStep(expected)}";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            string expected = GetExpectedStepId();
            if (!string.Equals(stepId, expected))
            {
                EscalateFailure(false);
                RegisterFailure($"Patch sequence mismatch. Expected {FormatStep(expected)}.");
                return false;
            }

            int nextIndex = CurrentStepIndex + 1;
            if (requiredStepOrder == null || nextIndex >= requiredStepOrder.Length)
            {
                ApplyCompletionState();
                CompleteTask("Hull seam patched. Flood ingress reduced.");
                return true;
            }

            AdvanceToNextStep(BuildObjectiveText(nextIndex), FormatStep(stepId) + " applied.");
            return true;
        }

        protected override void OnLockedOut()
        {
            EscalateFailure(true);
        }

        private void ApplyCompletionState()
        {
            FloodZoneState target = completeSetsSafe ? FloodZoneState.SealedSafe : FloodZoneState.Wet;
            if (stabilizedZones != null)
            {
                for (int i = 0; i < stabilizedZones.Length; i++)
                {
                    FloodZone zone = stabilizedZones[i];
                    if (zone != null)
                    {
                        zone.TrySetState(target);
                    }
                }
            }

            if (suppressedHazardsOnComplete != null)
            {
                for (int i = 0; i < suppressedHazardsOnComplete.Length; i++)
                {
                    NetworkBleachLeakHazard hazard = suppressedHazardsOnComplete[i];
                    if (hazard != null)
                    {
                        hazard.ServerSuppressFor(hazardSuppressionSeconds, hazard.DisplayName + " starved by hull patch");
                    }
                }
            }
        }

        private void EscalateFailure(bool hard)
        {
            if (failureZones == null)
            {
                return;
            }

            FloodZoneState target = hard ? FloodZoneState.Submerged : FloodZoneState.Flooding;
            for (int i = 0; i < failureZones.Length; i++)
            {
                FloodZone zone = failureZones[i];
                if (zone != null && zone.CurrentState != FloodZoneState.SealedSafe)
                {
                    zone.TrySetState(target);
                }
            }
        }

        private string GetExpectedStepId()
        {
            if (requiredStepOrder == null || requiredStepOrder.Length == 0)
            {
                return string.Empty;
            }

            int index = Mathf.Clamp(CurrentStepIndex, 0, requiredStepOrder.Length - 1);
            return requiredStepOrder[index];
        }

        private string BuildObjectiveText(int forcedIndex = -1)
        {
            if (requiredStepOrder == null || requiredStepOrder.Length == 0)
            {
                return "Hull patch sequence missing.";
            }

            int index = forcedIndex >= 0 ? forcedIndex : Mathf.Clamp(CurrentStepIndex, 0, requiredStepOrder.Length - 1);
            return $"Apply hull patch step: {FormatStep(requiredStepOrder[index])}.";
        }

        private static string FormatStep(string stepId)
        {
            return string.IsNullOrWhiteSpace(stepId) ? "UNKNOWN" : stepId.Replace("_", " ").ToUpperInvariant();
        }
    }
}
