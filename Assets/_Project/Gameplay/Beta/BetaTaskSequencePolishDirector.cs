// File: Assets/_Project/Gameplay/Beta/BetaTaskSequencePolishDirector.cs
using System.Collections.Generic;
using System.Reflection;
using HueDoneIt.Tasks;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Light-touch beta task pass. It lengthens some ordered tasks by adding one repeated verification step
    /// without requiring any new scene wiring. This makes stations feel a little more deliberate while still
    /// staying readable because the repeat always echoes an earlier step that the player has already seen.
    /// </summary>
    [DefaultExecutionOrder(-705)]
    [DisallowMultipleComponent]
    public sealed class BetaTaskSequencePolishDirector : MonoBehaviour
    {
        [SerializeField, Min(0.25f)] private float retuneIntervalSeconds = 3f;

        private float _nextRetuneTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextRetuneTime)
            {
                return;
            }

            _nextRetuneTime = Time.unscaledTime + retuneIntervalSeconds;
            RetuneTasks();
        }

        private void RetuneTasks()
        {
            TuneSequenceField<PowerRelayTask>("requiredStepOrder");
            TuneSequenceField<SignalPatternTask>("requiredStepOrder");
            TuneSequenceField<ChemicalBlendTask>("requiredStepOrder");
            TuneSequenceField<CoolantRerouteTask>("requiredStepOrder");
            TuneSequenceField<HullPatchTask>("requiredStepOrder");
            TuneSequenceField<OxygenPurgeTask>("requiredStepOrder");
            TuneSequenceField<FloodValveTask>("valveStepIds");
        }

        private static void TuneSequenceField<T>(string fieldName) where T : TaskObjectiveBase
        {
            T[] tasks = FindObjectsByType<T>(FindObjectsSortMode.None);
            for (int i = 0; i < tasks.Length; i++)
            {
                T task = tasks[i];
                if (task == null)
                {
                    continue;
                }

                if (TryExpandSequence(task, fieldName) && task.IsServer && task.CurrentState == RepairTaskState.Idle)
                {
                    task.ServerResetTask();
                }
            }
        }

        private static bool TryExpandSequence(object task, string fieldName)
        {
            FieldInfo field = task.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(string[]))
            {
                return false;
            }

            string[] current = field.GetValue(task) as string[];
            if (current == null || current.Length < 2 || current.Length >= 5)
            {
                return false;
            }

            string[] expanded = CreateEchoSequence(current);
            if (expanded == null || expanded.Length <= current.Length)
            {
                return false;
            }

            field.SetValue(task, expanded);
            return true;
        }

        private static string[] CreateEchoSequence(string[] source)
        {
            if (source == null || source.Length < 2)
            {
                return source;
            }

            List<string> output = new List<string>(source.Length + 1);
            for (int i = 0; i < source.Length - 1; i++)
            {
                output.Add(source[i]);
            }

            int echoIndex = Mathf.Clamp(source.Length > 2 ? 1 : 0, 0, source.Length - 2);
            output.Add(source[echoIndex]);
            output.Add(source[source.Length - 1]);
            return output.ToArray();
        }
    }
}
