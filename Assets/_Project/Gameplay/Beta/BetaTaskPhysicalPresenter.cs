// File: Assets/_Project/Gameplay/Beta/BetaTaskPhysicalPresenter.cs
using System.Collections.Generic;
using HueDoneIt.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(900)]
    public sealed class BetaTaskPhysicalPresenter : MonoBehaviour
    {
        [SerializeField, Min(0.25f)] private float rebuildIntervalSeconds = 3f;
        [SerializeField] private Color idleColor = new(0.55f, 0.55f, 0.58f, 1f);
        [SerializeField] private Color activeColor = new(0.18f, 0.72f, 1f, 1f);
        [SerializeField] private Color completeColor = new(0.18f, 1f, 0.42f, 1f);
        [SerializeField] private Color failedColor = new(1f, 0.28f, 0.18f, 1f);

        private readonly List<TaskProp> _props = new();
        private readonly MaterialPropertyBlock _block = new();
        private float _nextRebuildTime;

        private sealed class TaskProp
        {
            public Transform Root;
            public Renderer Renderer;
            public NetworkRepairTask RepairTask;
            public TaskObjectiveBase ObjectiveTask;
            public Vector3 BaseLocalPosition;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRebuildTime)
            {
                _nextRebuildTime = Time.unscaledTime + rebuildIntervalSeconds;
                RebuildMissingProps();
            }

            TickProps();
        }

        private void RebuildMissingProps()
        {
            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task != null && !HasProp(task.transform))
                {
                    CreateRepairProp(task);
                }
            }

            TaskObjectiveBase[] objectiveTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < objectiveTasks.Length; i++)
            {
                TaskObjectiveBase task = objectiveTasks[i];
                if (task != null && !HasProp(task.transform))
                {
                    CreateObjectiveProp(task);
                }
            }
        }

        private bool HasProp(Transform parent)
        {
            return parent != null && parent.Find("__BetaTaskPhysicalProp") != null;
        }

        private void CreateRepairProp(NetworkRepairTask task)
        {
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            prop.name = "__BetaTaskPhysicalProp";
            prop.transform.SetParent(task.transform, false);
            prop.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            prop.transform.localScale = new Vector3(0.32f, 0.08f, 0.32f);
            DestroyCollider(prop);

            Renderer renderer = prop.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            _props.Add(new TaskProp
            {
                Root = prop.transform,
                Renderer = renderer,
                RepairTask = task,
                BaseLocalPosition = prop.transform.localPosition
            });
        }

        private void CreateObjectiveProp(TaskObjectiveBase task)
        {
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.name = "__BetaTaskPhysicalProp";
            prop.transform.SetParent(task.transform, false);
            prop.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            prop.transform.localScale = new Vector3(0.45f, 0.12f, 0.45f);
            DestroyCollider(prop);

            Renderer renderer = prop.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            _props.Add(new TaskProp
            {
                Root = prop.transform,
                Renderer = renderer,
                ObjectiveTask = task,
                BaseLocalPosition = prop.transform.localPosition
            });
        }

        private void TickProps()
        {
            float now = Time.time;
            for (int i = _props.Count - 1; i >= 0; i--)
            {
                TaskProp prop = _props[i];
                if (prop == null || prop.Root == null)
                {
                    _props.RemoveAt(i);
                    continue;
                }

                RepairTaskState state = ResolveState(prop);
                float progress = ResolveProgress(prop);
                float pulse = state == RepairTaskState.InProgress ? Mathf.Sin(now * 9f) * 0.08f : 0f;
                prop.Root.localPosition = prop.BaseLocalPosition + Vector3.up * pulse;
                prop.Root.localRotation = Quaternion.Euler(0f, now * (state == RepairTaskState.InProgress ? 140f : 30f), 0f);
                float scale = Mathf.Lerp(0.85f, 1.25f, progress);
                prop.Root.localScale = new Vector3(prop.Root.localScale.x, Mathf.Max(0.06f, 0.08f * scale), prop.Root.localScale.z);

                ApplyColor(prop.Renderer, ResolveColor(state, progress));
            }
        }

        private RepairTaskState ResolveState(TaskProp prop)
        {
            if (prop.RepairTask != null)
            {
                return prop.RepairTask.CurrentState;
            }

            if (prop.ObjectiveTask != null)
            {
                return prop.ObjectiveTask.CurrentState;
            }

            return RepairTaskState.Idle;
        }

        private float ResolveProgress(TaskProp prop)
        {
            if (prop.RepairTask != null)
            {
                return Mathf.Clamp01(prop.RepairTask.GetCurrentProgress01());
            }

            if (prop.ObjectiveTask != null)
            {
                int step = Mathf.Max(0, prop.ObjectiveTask.CurrentStepIndex);
                return prop.ObjectiveTask.IsCompleted ? 1f : Mathf.Clamp01(step / 4f);
            }

            return 0f;
        }

        private Color ResolveColor(RepairTaskState state, float progress)
        {
            return state switch
            {
                RepairTaskState.Completed => completeColor,
                RepairTaskState.InProgress => Color.Lerp(activeColor, completeColor, progress * 0.5f),
                RepairTaskState.Cancelled or RepairTaskState.FailedAttempt or RepairTaskState.Locked => failedColor,
                _ => idleColor
            };
        }

        private void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            _block.SetColor("_EmissionColor", color * 0.35f);
            renderer.SetPropertyBlock(_block);
        }

        private static void DestroyCollider(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }
    }
}
