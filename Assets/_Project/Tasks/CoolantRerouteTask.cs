// File: Assets/_Project/Tasks/CoolantRerouteTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class CoolantRerouteTask : TaskObjectiveBase
    {
        [Header("Coolant Order")]
        [SerializeField] private string[] requiredStepOrder = { "intake", "pump", "chiller", "return" };

        [Header("World Impact")]
        [SerializeField] private FloodZone[] cooledZones = new FloodZone[0];
        [SerializeField] private FloodZone[] failureZones = new FloodZone[0];
        [SerializeField] private NetworkBleachLeakHazard[] suppressedHazardsOnComplete = new NetworkBleachLeakHazard[0];
        [SerializeField, Min(0f)] private float hazardSuppressionSeconds = 24f;
        [SerializeField, Min(0f)] private float completionTimeBonusSeconds = 8f;
        [SerializeField, Min(0f)] private float failureTimePenaltySeconds = 5f;

        protected override void OnServerResetTask()
        {
            SetObjective(BuildObjectiveText());
            SetStatus("Reroute coolant in sequence. Wrong routing flashes steam into flood lanes.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            string expected = GetExpectedStepId();
            return string.Equals(stepId, expected)
                ? $"Route {FormatStep(expected)} // Correct"
                : $"Coolant path mismatch // expected {FormatStep(expected)}";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            string expected = GetExpectedStepId();
            if (!string.Equals(stepId, expected))
            {
                EscalateFailure(false);
                FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(DisplayName, "Coolant flash destabilized a flooded corridor.", failureTimePenaltySeconds);
                RegisterFailure($"Coolant route mismatch. Expected {FormatStep(expected)}.");
                return false;
            }

            int nextIndex = CurrentStepIndex + 1;
            if (requiredStepOrder == null || nextIndex >= requiredStepOrder.Length)
            {
                ApplyCompletionState(context.InteractorClientId);
                CompleteTask("Coolant rerouted. Heat sink stabilized flood-control hardware.");
                return true;
            }

            AdvanceToNextStep(BuildObjectiveText(nextIndex), FormatStep(stepId) + " route opened.");
            return true;
        }

        protected override void OnLockedOut()
        {
            EscalateFailure(true);
        }

        private void ApplyCompletionState(ulong clientId)
        {
            if (cooledZones != null)
            {
                for (int i = 0; i < cooledZones.Length; i++)
                {
                    FloodZone zone = cooledZones[i];
                    if (zone != null && zone.CurrentState != FloodZoneState.SealedSafe)
                    {
                        zone.TrySetState(FloodZoneState.Wet);
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
                        hazard.ServerSuppressFor(hazardSuppressionSeconds, DisplayName + " cooled " + hazard.DisplayName);
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
                return "Coolant sequence missing.";
            }

            int index = forcedIndex >= 0 ? forcedIndex : Mathf.Clamp(CurrentStepIndex, 0, requiredStepOrder.Length - 1);
            return $"Open coolant node: {FormatStep(requiredStepOrder[index])}.";
        }

        private static string FormatStep(string stepId)
        {
            return string.IsNullOrWhiteSpace(stepId) ? "UNKNOWN" : stepId.Replace("_", " ").ToUpperInvariant();
        }
    }
}
