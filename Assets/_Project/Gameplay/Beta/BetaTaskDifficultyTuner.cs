// File: Assets/_Project/Gameplay/Beta/BetaTaskDifficultyTuner.cs
using System.Reflection;
using HueDoneIt.Tasks;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Friend-test tuning layer. It clamps task durations/timeouts into a playable beta range so one stuck
    /// or overlong terminal does not dominate the entire match.
    /// </summary>
    [DefaultExecutionOrder(-710)]
    [DisallowMultipleComponent]
    public sealed class BetaTaskDifficultyTuner : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float retuneIntervalSeconds = 4f;
        [SerializeField, Min(1f)] private float minRepairDurationSeconds = 4.5f;
        [SerializeField, Min(1f)] private float maxRepairDurationSeconds = 11.5f;
        [SerializeField, Min(1f)] private float repairTimeoutSeconds = 18f;
        [SerializeField, Min(5f)] private float advancedTaskReleaseTimeoutSeconds = 28f;
        [SerializeField, Min(1)] private int advancedTaskMaxFailures = 4;

        private float _nextTuneTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextTuneTime)
            {
                return;
            }

            _nextTuneTime = Time.unscaledTime + retuneIntervalSeconds;
            TuneRepairTasks();
            TuneAdvancedTasks();
        }

        private void TuneRepairTasks()
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null)
                {
                    continue;
                }

                float currentDuration = Mathf.Max(0f, task.TaskDurationSeconds);
                float tunedDuration = Mathf.Clamp(currentDuration <= 0.01f ? minRepairDurationSeconds : currentDuration, minRepairDurationSeconds, maxRepairDurationSeconds);
                SetPrivateFloat(task, "taskDurationSeconds", tunedDuration);
                SetPrivateFloat(task, "minimumInProgressTimeoutSeconds", Mathf.Max(repairTimeoutSeconds, tunedDuration + 6f));
                SetPrivateFloat(task, "inProgressTimeoutMultiplier", 1.75f);
            }
        }

        private void TuneAdvancedTasks()
        {
            TaskObjectiveBase[] tasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < tasks.Length; i++)
            {
                TaskObjectiveBase task = tasks[i];
                if (task == null)
                {
                    continue;
                }

                SetPrivateInt(task, "maxFailuresBeforeLock", advancedTaskMaxFailures);
                SetPrivateFloat(task, "interactReleaseTimeoutSeconds", advancedTaskReleaseTimeoutSeconds);
            }
        }

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
            }
        }

        private static void SetPrivateInt(object target, string fieldName, int value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(int))
            {
                field.SetValue(target, value);
            }
        }
    }
}
