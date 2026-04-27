// File: Assets/_Project/Tasks/FloodValveTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class FloodValveTask : TaskObjectiveBase
    {
        [Header("Valve Order")]
        [SerializeField] private string[] valveStepIds = { "valve_a", "valve_c", "valve_b" };
        [SerializeField] private string releaseStepId = "release";

        [Header("Affected Zones")]
        [SerializeField] private FloodZone[] controlledZones = new FloodZone[0];
        [SerializeField] private bool completeSetsSafe = true;

        protected override void OnServerResetTask()
        {
            SetObjective($"Set valve {GetValveLabel(0)} first.");
            SetStatus("Correct valve order prevents overpressure.");
        }

        protected override string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            if (CurrentStepIndex < valveStepIds.Length)
            {
                string expected = valveStepIds[CurrentStepIndex];
                return string.Equals(stepId, expected)
                    ? $"Set {GetValveLabel(CurrentStepIndex)}"
                    : "Wrong valve risks lockout";
            }

            return string.Equals(stepId, releaseStepId)
                ? "Release pressure"
                : "Wrong terminal";
        }

        protected override bool HandleStepInteraction(string stepId, in InteractionContext context)
        {
            if (CurrentStepIndex < valveStepIds.Length)
            {
                string expected = valveStepIds[CurrentStepIndex];
                if (!string.Equals(stepId, expected))
                {
                    EscalateZones(false);
                    RegisterFailure("Incorrect valve order. Pressure surged.");
                    return false;
                }

                int nextIndex = CurrentStepIndex + 1;
                if (nextIndex < valveStepIds.Length)
                {
                    AdvanceToNextStep($"Set valve {GetValveLabel(nextIndex)} next.", $"{GetValveLabel(CurrentStepIndex)} set.");
                    return true;
                }

                AdvanceToNextStep("Release pressure at the master outlet.", "Valve chain primed.");
                return true;
            }

            if (!string.Equals(stepId, releaseStepId))
            {
                EscalateZones(false);
                RegisterFailure("Pressure release attempted at the wrong terminal.");
                return false;
            }

            ApplyCompletionZoneState();
            CompleteTask("Flood pressure released successfully.");
            return true;
        }

        protected override void OnLockedOut()
        {
            EscalateZones(true);
        }

        private void ApplyCompletionZoneState()
        {
            FloodZoneState target = completeSetsSafe ? FloodZoneState.SealedSafe : FloodZoneState.Wet;
            for (int i = 0; i < controlledZones.Length; i++)
            {
                FloodZone zone = controlledZones[i];
                if (zone == null)
                {
                    continue;
                }

                zone.TrySetState(target);
            }
        }

        private void EscalateZones(bool hard)
        {
            FloodZoneState target = hard ? FloodZoneState.Submerged : FloodZoneState.Flooding;
            for (int i = 0; i < controlledZones.Length; i++)
            {
                FloodZone zone = controlledZones[i];
                if (zone == null)
                {
                    continue;
                }

                zone.TrySetState(target);
            }
        }

        private string GetValveLabel(int index)
        {
            if (index < 0 || index >= valveStepIds.Length)
            {
                return "Release";
            }

            string raw = valveStepIds[index];
            return raw.Replace("_", " ").ToUpperInvariant();
        }
    }
}
