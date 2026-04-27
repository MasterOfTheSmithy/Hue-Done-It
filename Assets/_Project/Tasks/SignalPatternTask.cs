// File: Assets/_Project/Tasks/SignalPatternTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class SignalPatternTask : TaskObjectiveBase
    {
        [Header("Pattern")]
        [SerializeField] private string[] requiredStepOrder = { "cyan", "amber", "magenta", "white" };

        [Header("World Impact")]
        [SerializeField] private FloodZone[] stabilizedZones = new FloodZone[0];
        [SerializeField] private FloodZone[] surgeZonesOnFailure = new FloodZone[0];
        [SerializeField] private bool completeSetsSafe = true;

        protected override void OnServerResetTask()
        {
            SetObjective(BuildObjectiveText());
            SetStatus("Match the antenna pattern. Wrong inputs create a flood surge.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            string expected = GetExpectedStepId();
            if (string.Equals(stepId, expected))
            {
                return $"Transmit {FormatStep(expected)} // Correct";
            }

            return $"Out of order // expected {FormatStep(expected)}";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            string expected = GetExpectedStepId();
            if (!string.Equals(stepId, expected))
            {
                EscalateSurgeZones(false);
                RegisterFailure($"Signal mismatch. Expected {FormatStep(expected)}.");
                return false;
            }

            int nextIndex = CurrentStepIndex + 1;
            if (nextIndex >= requiredStepOrder.Length)
            {
                ApplyCompletionState();
                CompleteTask("Antenna pattern transmitted. Route telemetry stabilized.");
                return true;
            }

            AdvanceToNextStep(BuildObjectiveText(nextIndex), $"{FormatStep(stepId)} accepted.");
            return true;
        }

        protected override void OnLockedOut()
        {
            EscalateSurgeZones(true);
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
                return "Signal pattern missing.";
            }

            int index = forcedIndex >= 0 ? forcedIndex : Mathf.Clamp(CurrentStepIndex, 0, requiredStepOrder.Length - 1);
            return $"Transmit {FormatStep(requiredStepOrder[index])} at the antenna node.";
        }

        private void ApplyCompletionState()
        {
            FloodZoneState target = completeSetsSafe ? FloodZoneState.SealedSafe : FloodZoneState.Wet;
            for (int i = 0; i < stabilizedZones.Length; i++)
            {
                FloodZone zone = stabilizedZones[i];
                if (zone != null)
                {
                    zone.TrySetState(target);
                }
            }
        }

        private void EscalateSurgeZones(bool hard)
        {
            FloodZoneState target = hard ? FloodZoneState.Submerged : FloodZoneState.Flooding;
            for (int i = 0; i < surgeZonesOnFailure.Length; i++)
            {
                FloodZone zone = surgeZonesOnFailure[i];
                if (zone != null && zone.CurrentState != FloodZoneState.SealedSafe)
                {
                    zone.TrySetState(target);
                }
            }
        }

        private static string FormatStep(string stepId)
        {
            return string.IsNullOrWhiteSpace(stepId)
                ? "UNKNOWN"
                : stepId.Replace("_", " ").ToUpperInvariant();
        }
    }
}
