// File: Assets/_Project/Tasks/OxygenPurgeTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class OxygenPurgeTask : TaskObjectiveBase
    {
        [Header("Purge Order")]
        [SerializeField] private string[] requiredStepOrder = { "intake", "filter", "bleed", "reseal" };

        [Header("World Impact")]
        [SerializeField] private FloodZone[] ventedZones = new FloodZone[0];
        [SerializeField] private FloodZone[] failureZones = new FloodZone[0];
        [SerializeField] private NetworkBleachLeakHazard[] suppressedHazardsOnComplete = new NetworkBleachLeakHazard[0];
        [SerializeField, Min(0f)] private float hazardSuppressionSeconds = 24f;
        [SerializeField, Min(0f)] private float completionTimeBonusSeconds = 6f;
        [SerializeField, Min(0f)] private float failureTimePenaltySeconds = 4f;

        protected override void OnServerResetTask()
        {
            SetObjective(BuildObjectiveText());
            SetStatus("Purge oxygen in order. Wrong venting feeds the flood surge.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            string expected = GetExpectedStepId();
            return string.Equals(stepId, expected)
                ? $"Purge {FormatStep(expected)} // Correct"
                : $"Wrong purge step // expected {FormatStep(expected)}";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            string expected = GetExpectedStepId();
            if (!string.Equals(stepId, expected))
            {
                EscalateFailure(false);
                FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(DisplayName, "Wrong oxygen purge step destabilized flooded compartments.", failureTimePenaltySeconds);
                RegisterFailure($"Oxygen purge mismatch. Expected {FormatStep(expected)}.");
                return false;
            }

            int nextIndex = CurrentStepIndex + 1;
            if (requiredStepOrder == null || nextIndex >= requiredStepOrder.Length)
            {
                ApplyCompletionState(context.InteractorClientId);
                CompleteTask("Oxygen purge complete. Flood pressure reduced and bleach vapor thinned.");
                return true;
            }

            AdvanceToNextStep(BuildObjectiveText(nextIndex), FormatStep(stepId) + " purged.");
            return true;
        }

        protected override void OnLockedOut()
        {
            EscalateFailure(true);
        }

        private void ApplyCompletionState(ulong clientId)
        {
            if (ventedZones != null)
            {
                for (int i = 0; i < ventedZones.Length; i++)
                {
                    ReduceZoneOneStep(ventedZones[i]);
                }
            }

            if (suppressedHazardsOnComplete != null)
            {
                for (int i = 0; i < suppressedHazardsOnComplete.Length; i++)
                {
                    NetworkBleachLeakHazard hazard = suppressedHazardsOnComplete[i];
                    if (hazard != null)
                    {
                        hazard.ServerSuppressFor(hazardSuppressionSeconds, hazard.DisplayName + " thinned by oxygen purge");
                    }
                }
            }

            FindFirstObjectByType<NetworkRoundState>()?.ServerApplyCrewStabilization(clientId, DisplayName, completionTimeBonusSeconds);
        }

        private static void ReduceZoneOneStep(FloodZone zone)
        {
            if (zone == null)
            {
                return;
            }

            switch (zone.CurrentState)
            {
                case FloodZoneState.Submerged:
                    zone.TrySetState(FloodZoneState.Flooding);
                    break;
                case FloodZoneState.Flooding:
                    zone.TrySetState(FloodZoneState.Wet);
                    break;
                case FloodZoneState.Wet:
                    zone.TrySetState(FloodZoneState.SealedSafe);
                    break;
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
                return "Oxygen purge sequence missing.";
            }

            int index = forcedIndex >= 0 ? forcedIndex : Mathf.Clamp(CurrentStepIndex, 0, requiredStepOrder.Length - 1);
            return $"Run oxygen purge step: {FormatStep(requiredStepOrder[index])}.";
        }

        private static string FormatStep(string stepId)
        {
            return string.IsNullOrWhiteSpace(stepId) ? "UNKNOWN" : stepId.Replace("_", " ").ToUpperInvariant();
        }
    }
}
