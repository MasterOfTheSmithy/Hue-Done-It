// File: Assets/_Project/Tasks/PowerRelayTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class PowerRelayTask : TaskObjectiveBase
    {
        [Header("Relay Order")]
        [SerializeField] private string[] requiredStepOrder = { "breaker_a", "breaker_c", "breaker_b", "main_bus" };

        [Header("World Impact")]
        [SerializeField] private FloodZone[] stabilizedZones = new FloodZone[0];
        [SerializeField] private FloodZone[] failureZones = new FloodZone[0];
        [SerializeField, Min(0f)] private float completionTimeBonusSeconds = 9f;
        [SerializeField, Min(0f)] private float failureTimePenaltySeconds = 4f;

        protected override void OnServerResetTask()
        {
            SetObjective(BuildObjectiveText());
            SetStatus("Bring relay banks online in the called order.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            string expected = GetExpectedStepId();
            return string.Equals(stepId, expected)
                ? $"Energize {FormatStep(expected)} // Correct"
                : $"Relay out of order // expected {FormatStep(expected)}";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            string expected = GetExpectedStepId();
            if (!string.Equals(stepId, expected))
            {
                EscalateFailure(false);
                FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(DisplayName, "Relay spike destabilized a flood route.", failureTimePenaltySeconds);
                RegisterFailure($"Relay mismatch. Expected {FormatStep(expected)}.");
                return false;
            }

            int nextIndex = CurrentStepIndex + 1;
            if (requiredStepOrder == null || nextIndex >= requiredStepOrder.Length)
            {
                ApplyCompletionState(context.InteractorClientId);
                CompleteTask("Relay power restored. Stabilizers have more time.");
                return true;
            }

            AdvanceToNextStep(BuildObjectiveText(nextIndex), FormatStep(stepId) + " energized.");
            return true;
        }

        protected override void OnLockedOut()
        {
            EscalateFailure(true);
        }

        private void ApplyCompletionState(ulong clientId)
        {
            if (stabilizedZones != null)
            {
                for (int i = 0; i < stabilizedZones.Length; i++)
                {
                    FloodZone zone = stabilizedZones[i];
                    if (zone != null && zone.CurrentState != FloodZoneState.SealedSafe)
                    {
                        zone.TrySetState(FloodZoneState.Wet);
                    }
                }
            }

            FindFirstObjectByType<NetworkRoundState>()?.ServerApplyCrewStabilization(clientId, DisplayName, completionTimeBonusSeconds);
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
                return "Relay sequence missing.";
            }

            int index = forcedIndex >= 0 ? forcedIndex : Mathf.Clamp(CurrentStepIndex, 0, requiredStepOrder.Length - 1);
            return $"Energize relay node: {FormatStep(requiredStepOrder[index])}.";
        }

        private static string FormatStep(string stepId)
        {
            return string.IsNullOrWhiteSpace(stepId) ? "UNKNOWN" : stepId.Replace("_", " ").ToUpperInvariant();
        }
    }
}
