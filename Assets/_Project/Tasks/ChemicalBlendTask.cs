// File: Assets/_Project/Tasks/ChemicalBlendTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class ChemicalBlendTask : TaskObjectiveBase
    {
        [Header("Blend Order")]
        [SerializeField] private string[] requiredStepOrder = { "prime", "stabilizer", "neutralizer", "flush" };

        [Header("World Impact")]
        [SerializeField] private FloodZone[] stabilizedZones = new FloodZone[0];
        [SerializeField] private FloodZone[] failureZones = new FloodZone[0];
        [SerializeField] private NetworkBleachLeakHazard[] suppressedHazardsOnComplete = new NetworkBleachLeakHazard[0];
        [SerializeField, Min(0f)] private float completionSuppressionSeconds = 44f;

        protected override void OnServerResetTask()
        {
            SetObjective(BuildObjectiveText());
            SetStatus("Blend neutralizer in order. A bad mixture feeds bleach vents.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            string expected = GetExpectedStepId();
            if (string.Equals(stepId, expected))
            {
                return $"Blend {FormatStep(expected)} // Correct";
            }

            return $"Wrong reagent // expected {FormatStep(expected)}";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            string expected = GetExpectedStepId();
            if (!string.Equals(stepId, expected))
            {
                EscalateFailure(false);
                RegisterFailure($"Chemical mismatch. Expected {FormatStep(expected)}.");
                return false;
            }

            int nextIndex = CurrentStepIndex + 1;
            if (requiredStepOrder == null || nextIndex >= requiredStepOrder.Length)
            {
                ApplyCompletionState();
                CompleteTask("Neutralizer blend complete. Bleach spread dampened.");
                return true;
            }

            AdvanceToNextStep(BuildObjectiveText(nextIndex), $"{FormatStep(stepId)} mixed.");
            return true;
        }

        protected override void OnLockedOut()
        {
            EscalateFailure(true);
        }

        private void ApplyCompletionState()
        {
            for (int i = 0; i < stabilizedZones.Length; i++)
            {
                FloodZone zone = stabilizedZones[i];
                if (zone != null)
                {
                    zone.TrySetState(FloodZoneState.SealedSafe);
                }
            }

            for (int i = 0; i < suppressedHazardsOnComplete.Length; i++)
            {
                NetworkBleachLeakHazard hazard = suppressedHazardsOnComplete[i];
                if (hazard != null)
                {
                    hazard.ServerSuppressFor(completionSuppressionSeconds, hazard.DisplayName + " neutralized");
                }
            }
        }

        private void EscalateFailure(bool hard)
        {
            FloodZoneState target = hard ? FloodZoneState.Submerged : FloodZoneState.Flooding;
            for (int i = 0; i < failureZones.Length; i++)
            {
                FloodZone zone = failureZones[i];
                if (zone != null && zone.CurrentState != FloodZoneState.SealedSafe)
                {
                    zone.TrySetState(target);
                }
            }

            if (!hard || suppressedHazardsOnComplete == null)
            {
                return;
            }

            for (int i = 0; i < suppressedHazardsOnComplete.Length; i++)
            {
                NetworkBleachLeakHazard hazard = suppressedHazardsOnComplete[i];
                if (hazard != null)
                {
                    hazard.ServerReactivate(DisplayName + " failed and reopened " + hazard.DisplayName);
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
                return "Chemical recipe missing.";
            }

            int index = forcedIndex >= 0 ? forcedIndex : Mathf.Clamp(CurrentStepIndex, 0, requiredStepOrder.Length - 1);
            return $"Add {FormatStep(requiredStepOrder[index])} to the neutralizer vat.";
        }

        private static string FormatStep(string stepId)
        {
            return string.IsNullOrWhiteSpace(stepId)
                ? "UNKNOWN"
                : stepId.Replace("_", " ").ToUpperInvariant();
        }
    }
}
