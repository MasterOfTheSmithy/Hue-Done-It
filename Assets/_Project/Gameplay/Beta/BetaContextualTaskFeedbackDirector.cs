// File: Assets/_Project/Gameplay/Beta/BetaContextualTaskFeedbackDirector.cs
using System.Collections.Generic;
using HueDoneIt.Tasks;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Adds lightweight contextual task animations around existing task objects: progress halos, success pulses,
    /// failure pulses, and in-progress motion. It is visual-only and never blocks interaction.
    /// </summary>
    [DefaultExecutionOrder(875)]
    [DisallowMultipleComponent]
    public sealed class BetaContextualTaskFeedbackDirector : MonoBehaviour
    {
        private const string MarkerName = "__BetaContextualTaskFeedback";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Min(0.25f)] private float rebuildIntervalSeconds = 2.5f;
        [SerializeField] private Color idleColor = new(0.18f, 0.72f, 1f, 0.72f);
        [SerializeField] private Color activeColor = new(1f, 0.78f, 0.12f, 0.95f);
        [SerializeField] private Color completeColor = new(0.25f, 1f, 0.32f, 0.95f);
        [SerializeField] private Color failedColor = new(1f, 0.18f, 0.16f, 0.95f);

        private readonly List<TaskFx> _fx = new();
        private Material _material;
        private MaterialPropertyBlock _block;
        private float _nextRebuildTime;

        private sealed class TaskFx
        {
            public Transform Root;
            public Transform Halo;
            public Transform Needle;
            public Renderer[] Renderers;
            public NetworkRepairTask RepairTask;
            public TaskObjectiveBase ObjectiveTask;
            public RepairTaskState LastState;
            public float Pulse;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRebuildTime)
            {
                _nextRebuildTime = Time.unscaledTime + rebuildIntervalSeconds;
                RebuildMissingFx();
            }

            TickFx(Time.deltaTime);
        }

        private void RebuildMissingFx()
        {
            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task != null && task.transform.Find(MarkerName) == null)
                {
                    _fx.Add(CreateFx(task.transform, task, null));
                }
            }

            TaskObjectiveBase[] objectiveTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < objectiveTasks.Length; i++)
            {
                TaskObjectiveBase task = objectiveTasks[i];
                if (task != null && task.transform.Find(MarkerName) == null)
                {
                    _fx.Add(CreateFx(task.transform, null, task));
                }
            }

            _fx.RemoveAll(item => item == null || item.Root == null);
        }

        private TaskFx CreateFx(Transform parent, NetworkRepairTask repairTask, TaskObjectiveBase objectiveTask)
        {
            GameObject root = new GameObject(MarkerName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 0.08f;

            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            halo.name = "ProgressHalo";
            halo.transform.SetParent(root.transform, false);
            halo.transform.localPosition = Vector3.zero;
            halo.transform.localScale = new Vector3(1.35f, 0.018f, 1.35f);
            DestroyCollider(halo);
            ApplyMaterial(halo);

            GameObject needle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            needle.name = "TaskMotionNeedle";
            needle.transform.SetParent(root.transform, false);
            needle.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            needle.transform.localScale = new Vector3(0.14f, 0.78f, 0.14f);
            DestroyCollider(needle);
            ApplyMaterial(needle);

            TaskFx fx = new()
            {
                Root = root.transform,
                Halo = halo.transform,
                Needle = needle.transform,
                Renderers = root.GetComponentsInChildren<Renderer>(true),
                RepairTask = repairTask,
                ObjectiveTask = objectiveTask,
                LastState = ResolveState(repairTask, objectiveTask),
                Pulse = 0f
            };

            return fx;
        }

        private void TickFx(float deltaTime)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                TaskFx fx = _fx[i];
                if (fx == null || fx.Root == null)
                {
                    _fx.RemoveAt(i);
                    continue;
                }

                RepairTaskState state = ResolveState(fx.RepairTask, fx.ObjectiveTask);
                if (state != fx.LastState)
                {
                    fx.LastState = state;
                    fx.Pulse = 1f;
                }

                fx.Pulse = Mathf.MoveTowards(fx.Pulse, 0f, deltaTime * 1.65f);
                float progress = ResolveProgress(fx.RepairTask, fx.ObjectiveTask);
                Color color = ResolveColor(state);

                if (fx.Halo != null)
                {
                    float radius = Mathf.Lerp(0.95f, 1.65f, progress) + fx.Pulse * 0.55f;
                    fx.Halo.localScale = new Vector3(radius, 0.018f, radius);
                    fx.Halo.Rotate(Vector3.up, ResolveRotationSpeed(state) * deltaTime, Space.Self);
                }

                if (fx.Needle != null)
                {
                    float bob = Mathf.Sin(Time.time * 5.25f + fx.Root.GetInstanceID()) * 0.12f;
                    fx.Needle.localPosition = new Vector3(0f, 1.08f + bob + fx.Pulse * 0.38f, 0f);
                    fx.Needle.localScale = new Vector3(
                        0.14f + fx.Pulse * 0.10f,
                        Mathf.Lerp(0.45f, 0.95f, progress) + fx.Pulse * 0.32f,
                        0.14f + fx.Pulse * 0.10f);
                    fx.Needle.Rotate(Vector3.up, ResolveRotationSpeed(state) * 1.7f * deltaTime, Space.Self);
                }

                ApplyColor(fx, color);
            }
        }

        private static RepairTaskState ResolveState(NetworkRepairTask repairTask, TaskObjectiveBase objectiveTask)
        {
            if (repairTask != null)
            {
                return repairTask.CurrentState;
            }

            if (objectiveTask != null)
            {
                return objectiveTask.CurrentState;
            }

            return RepairTaskState.Locked;
        }

        private static float ResolveProgress(NetworkRepairTask repairTask, TaskObjectiveBase objectiveTask)
        {
            if (repairTask != null)
            {
                return repairTask.GetCurrentProgress01();
            }

            if (objectiveTask != null)
            {
                return objectiveTask.CurrentState == RepairTaskState.Completed ? 1f : Mathf.Clamp01((objectiveTask.CurrentStepIndex + 1f) / 4f);
            }

            return 0f;
        }

        private Color ResolveColor(RepairTaskState state)
        {
            switch (state)
            {
                case RepairTaskState.Completed:
                    return completeColor;
                case RepairTaskState.InProgress:
                    return activeColor;
                case RepairTaskState.FailedAttempt:
                case RepairTaskState.Cancelled:
                case RepairTaskState.Locked:
                    return failedColor;
                default:
                    return idleColor;
            }
        }

        private static float ResolveRotationSpeed(RepairTaskState state)
        {
            switch (state)
            {
                case RepairTaskState.InProgress:
                    return 145f;
                case RepairTaskState.Completed:
                    return 70f;
                case RepairTaskState.FailedAttempt:
                case RepairTaskState.Cancelled:
                case RepairTaskState.Locked:
                    return -120f;
                default:
                    return 32f;
            }
        }

        private void ApplyColor(TaskFx fx, Color color)
        {
            if (fx.Renderers == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            for (int i = 0; i < fx.Renderers.Length; i++)
            {
                Renderer renderer = fx.Renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_block);
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(BaseColorId))
                {
                    _block.SetColor(BaseColorId, color);
                }
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(ColorId))
                {
                    _block.SetColor(ColorId, color);
                }
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(EmissionColorId))
                {
                    _block.SetColor(EmissionColorId, color * 1.2f);
                }
                renderer.SetPropertyBlock(_block);
            }
        }

        private void ApplyMaterial(GameObject go)
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                _material = new Material(shader);
                _material.name = "HDI Beta Contextual Task FX";
            }

            Renderer renderer = go != null ? go.GetComponent<Renderer>() : null;
            if (renderer != null)
            {
                renderer.sharedMaterial = _material;
            }
        }

        private static void DestroyCollider(GameObject go)
        {
            Collider collider = go != null ? go.GetComponent<Collider>() : null;
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }
    }
}
