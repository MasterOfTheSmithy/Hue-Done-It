using HueDoneIt.Gameplay.Paint;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class TaskAreaEnvironmentPresenter : MonoBehaviour
    {
        [SerializeField] private NetworkRepairTask task;
        [SerializeField] private PumpRepairTask pumpTask;
        [SerializeField] private Renderer[] indicatorRenderers;
        [SerializeField] private Light[] indicatorLights;
        [SerializeField] private StainReceiver[] nearbyReceivers;
        [SerializeField] private Transform evidenceAnchor;

        [Header("State Colors")]
        [SerializeField] private Color availableColor = new(0.72f, 0.72f, 0.74f, 1f);
        [SerializeField] private Color inProgressColor = new(0.2f, 0.75f, 1f, 1f);
        [SerializeField] private Color failedColor = new(1f, 0.35f, 0.3f, 1f);
        [SerializeField] private Color lockedColor = new(0.9f, 0.15f, 0.45f, 1f);
        [SerializeField] private Color completedColor = new(0.2f, 1f, 0.45f, 1f);

        [Header("Intensity")]
        [SerializeField, Min(0f)] private float maxLightIntensity = 2.6f;
        [SerializeField, Min(0f)] private float pulseSpeed = 2.2f;
        [SerializeField, Range(0f, 1f)] private float taskAreaMarkIntensity = 0.72f;
        [SerializeField] private bool spawnStateEvidence = true;

        [Header("Debug")]
        [SerializeField] private bool debugDrawAnchor;

        private readonly MaterialPropertyBlock _propertyBlock = new();
        private RepairTaskState _lastState = (RepairTaskState)255;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (task == null)
            {
                task = GetComponentInParent<NetworkRepairTask>();
            }

            if (pumpTask == null)
            {
                pumpTask = task as PumpRepairTask;
            }

            if ((indicatorRenderers == null || indicatorRenderers.Length == 0) && TryGetComponent(out Renderer selfRenderer))
            {
                indicatorRenderers = new[] { selfRenderer };
            }

            if (nearbyReceivers == null || nearbyReceivers.Length == 0)
            {
                nearbyReceivers = GetComponentsInChildren<StainReceiver>(true);
            }
        }

        private void OnEnable()
        {
            if (task != null)
            {
                task.TaskStateChanged += HandleTaskStateChanged;
                ApplyStateVisual(task.CurrentState, true);
                _lastState = task.CurrentState;
            }
        }

        private void OnDisable()
        {
            if (task != null)
            {
                task.TaskStateChanged -= HandleTaskStateChanged;
            }
        }

        private void Update()
        {
            if (task == null)
            {
                return;
            }

            RepairTaskState current = task.CurrentState;
            if (current != _lastState)
            {
                ApplyStateVisual(current, false);
                _lastState = current;
            }

            ApplyPulse(current);
        }

        private void HandleTaskStateChanged(RepairTaskState _, RepairTaskState current)
        {
            ApplyStateVisual(current, false);
            _lastState = current;
        }

        private void ApplyStateVisual(RepairTaskState state, bool immediate)
        {
            Color stateColor = GetColorForState(state);
            float intensity01 = GetIntensityForState(state);

            ApplyRendererState(stateColor, intensity01);
            ApplyLightState(stateColor, intensity01, immediate);

            if (!spawnStateEvidence || immediate)
            {
                return;
            }

            SpawnEvidenceForState(state, stateColor);
        }

        private void ApplyRendererState(Color stateColor, float intensity01)
        {
            if (indicatorRenderers == null)
            {
                return;
            }

            Color neutralBlend = Color.Lerp(availableColor, stateColor, intensity01);
            Color emission = neutralBlend * (0.2f + (intensity01 * 1.15f));

            for (int i = 0; i < indicatorRenderers.Length; i++)
            {
                Renderer renderer = indicatorRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, neutralBlend);
                _propertyBlock.SetColor(ColorId, neutralBlend);
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_EmissionColor"))
                {
                    _propertyBlock.SetColor(EmissionColorId, emission);
                }

                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void ApplyLightState(Color stateColor, float intensity01, bool immediate)
        {
            if (indicatorLights == null)
            {
                return;
            }

            float baseIntensity = maxLightIntensity * intensity01;
            for (int i = 0; i < indicatorLights.Length; i++)
            {
                Light lightRef = indicatorLights[i];
                if (lightRef == null)
                {
                    continue;
                }

                lightRef.color = stateColor;
                lightRef.intensity = immediate ? baseIntensity : Mathf.Lerp(lightRef.intensity, baseIntensity, 0.7f);
            }
        }

        private void ApplyPulse(RepairTaskState state)
        {
            if (indicatorLights == null)
            {
                return;
            }

            if (state is not (RepairTaskState.InProgress or RepairTaskState.FailedAttempt or RepairTaskState.Locked))
            {
                return;
            }

            float pulse = 0.76f + (Mathf.Sin(Time.time * pulseSpeed) * 0.24f);
            float baseIntensity = maxLightIntensity * GetIntensityForState(state);
            for (int i = 0; i < indicatorLights.Length; i++)
            {
                Light lightRef = indicatorLights[i];
                if (lightRef == null)
                {
                    continue;
                }

                lightRef.intensity = baseIntensity * pulse;
            }
        }

        private void SpawnEvidenceForState(RepairTaskState state, Color evidenceColor)
        {
            if (nearbyReceivers == null || nearbyReceivers.Length == 0)
            {
                return;
            }

            bool permanent = state is RepairTaskState.Completed or RepairTaskState.Locked;
            float baseRadius = state switch
            {
                RepairTaskState.InProgress => 0.2f,
                RepairTaskState.FailedAttempt => 0.28f,
                RepairTaskState.Locked => 0.34f,
                RepairTaskState.Completed => 0.26f,
                _ => 0.18f
            };

            float intensity = taskAreaMarkIntensity * (state switch
            {
                RepairTaskState.InProgress => 0.9f,
                RepairTaskState.FailedAttempt => 1.15f,
                RepairTaskState.Locked => 1.25f,
                RepairTaskState.Completed => 0.95f,
                _ => 0.65f
            });

            Vector3 anchor = evidenceAnchor != null ? evidenceAnchor.position : transform.position;
            Vector3 normal = Vector3.up;

            for (int i = 0; i < nearbyReceivers.Length; i++)
            {
                StainReceiver receiver = nearbyReceivers[i];
                if (receiver == null)
                {
                    continue;
                }

                Vector3 offset = new((i - (nearbyReceivers.Length * 0.5f)) * 0.12f, 0f, ((i & 1) == 0 ? -1f : 1f) * 0.08f);
                receiver.SpawnEnvironmentalEvidence(
                    evidenceColor,
                    anchor + offset,
                    normal,
                    state is RepairTaskState.FailedAttempt or RepairTaskState.Locked ? PaintEventKind.Punch : PaintEventKind.TaskInteract,
                    permanent,
                    baseRadius,
                    intensity);
            }
        }

        private Color GetColorForState(RepairTaskState state)
        {
            if (pumpTask != null && pumpTask.IsLocked)
            {
                return lockedColor;
            }

            return state switch
            {
                RepairTaskState.InProgress => inProgressColor,
                RepairTaskState.Completed => completedColor,
                RepairTaskState.FailedAttempt => failedColor,
                RepairTaskState.Locked => lockedColor,
                RepairTaskState.Cancelled => availableColor,
                _ => availableColor
            };
        }

        private static float GetIntensityForState(RepairTaskState state)
        {
            return state switch
            {
                RepairTaskState.InProgress => 1f,
                RepairTaskState.Completed => 0.75f,
                RepairTaskState.FailedAttempt => 0.9f,
                RepairTaskState.Locked => 1f,
                RepairTaskState.Cancelled => 0.45f,
                _ => 0.5f
            };
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugDrawAnchor)
            {
                return;
            }

            Vector3 anchor = evidenceAnchor != null ? evidenceAnchor.position : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(anchor + (Vector3.up * 0.05f), 0.15f);
        }
    }
}
