using HueDoneIt.Gameplay.Paint;
using UnityEngine;

namespace HueDoneIt.Flood
{
    [DisallowMultipleComponent]
    public sealed class FloodZoneEnvironmentPresenter : MonoBehaviour
    {
        [SerializeField] private FloodZone zone;
        [SerializeField] private Renderer[] roomRenderers;
        [SerializeField] private Light[] warningLights;
        [SerializeField] private StainReceiver[] stainReceivers;

        [Header("Room State Colors")]
        [SerializeField] private Color stableColor = new(0.58f, 0.58f, 0.58f, 1f);
        [SerializeField] private Color wetColor = new(0.52f, 0.56f, 0.62f, 1f);
        [SerializeField] private Color floodingColor = new(0.38f, 0.5f, 0.68f, 1f);
        [SerializeField] private Color submergedColor = new(0.3f, 0.45f, 0.63f, 1f);
        [SerializeField] private Color recentlyFloodedColor = new(0.45f, 0.53f, 0.61f, 1f);

        [Header("Hazard Response")]
        [SerializeField, Min(0f)] private float transitionSpeed = 2.4f;
        [SerializeField, Min(0f)] private float recentFloodMemorySeconds = 12f;
        [SerializeField, Min(0f)] private float warningLightMaxIntensity = 3f;
        [SerializeField, Min(0f)] private float warningPulseSpeed = 2.4f;
        [SerializeField, Range(0f, 1f)] private float washoutWhenFlooding = 0.5f;
        [SerializeField, Range(0f, 1f)] private float washoutWhenSubmerged = 0.82f;

        [Header("Debug")]
        [SerializeField] private bool debugShowState;

        private readonly MaterialPropertyBlock _propertyBlock = new();
        private float _targetHazard;
        private float _currentHazard;
        private float _targetWashout;
        private float _currentWashout;
        private float _recentFloodTimer;
        private Color _targetColor;
        private Color _currentColor;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (zone == null)
            {
                zone = GetComponentInParent<FloodZone>();
            }

            if ((roomRenderers == null || roomRenderers.Length == 0) && TryGetComponent(out Renderer selfRenderer))
            {
                roomRenderers = new[] { selfRenderer };
            }

            if (stainReceivers == null || stainReceivers.Length == 0)
            {
                stainReceivers = GetComponentsInChildren<StainReceiver>(true);
            }

            _targetColor = stableColor;
            _currentColor = stableColor;
        }

        private void OnEnable()
        {
            if (zone != null)
            {
                zone.StateChanged += HandleZoneStateChanged;
                ApplyStateTargets(zone.CurrentState, true);
            }
        }

        private void OnDisable()
        {
            if (zone != null)
            {
                zone.StateChanged -= HandleZoneStateChanged;
            }
        }

        private void Update()
        {
            if (zone == null)
            {
                return;
            }

            if (_recentFloodTimer > 0f)
            {
                _recentFloodTimer = Mathf.Max(0f, _recentFloodTimer - Time.deltaTime);
            }

            _currentHazard = Mathf.MoveTowards(_currentHazard, _targetHazard, transitionSpeed * Time.deltaTime);
            _currentWashout = Mathf.MoveTowards(_currentWashout, _targetWashout, transitionSpeed * Time.deltaTime);
            _currentColor = Color.Lerp(_currentColor, _targetColor, transitionSpeed * Time.deltaTime);

            ApplyRoomMaterialPresentation();
            ApplyWarningLights();
            ApplyStainReceiverState();
        }

        private void HandleZoneStateChanged(FloodZoneState previous, FloodZoneState current)
        {
            if (previous is FloodZoneState.Flooding or FloodZoneState.Submerged)
            {
                _recentFloodTimer = recentFloodMemorySeconds;
            }

            ApplyStateTargets(current, false);
        }

        private void ApplyStateTargets(FloodZoneState state, bool immediate)
        {
            switch (state)
            {
                case FloodZoneState.Dry:
                case FloodZoneState.SealedSafe:
                    _targetHazard = _recentFloodTimer > 0f ? 0.2f : 0f;
                    _targetWashout = _recentFloodTimer > 0f ? 0.2f : 0.05f;
                    _targetColor = _recentFloodTimer > 0f ? recentlyFloodedColor : stableColor;
                    break;
                case FloodZoneState.Wet:
                    _targetHazard = 0.25f;
                    _targetWashout = 0.22f;
                    _targetColor = wetColor;
                    break;
                case FloodZoneState.Flooding:
                    _targetHazard = 0.7f;
                    _targetWashout = washoutWhenFlooding;
                    _targetColor = floodingColor;
                    break;
                case FloodZoneState.Submerged:
                    _targetHazard = 1f;
                    _targetWashout = washoutWhenSubmerged;
                    _targetColor = submergedColor;
                    break;
                default:
                    _targetHazard = 0f;
                    _targetWashout = 0f;
                    _targetColor = stableColor;
                    break;
            }

            if (immediate)
            {
                _currentHazard = _targetHazard;
                _currentWashout = _targetWashout;
                _currentColor = _targetColor;
            }
        }

        private void ApplyRoomMaterialPresentation()
        {
            if (roomRenderers == null)
            {
                return;
            }

            float emissionPulse = 0.85f + (Mathf.Sin(Time.time * warningPulseSpeed) * 0.15f);
            for (int i = 0; i < roomRenderers.Length; i++)
            {
                Renderer renderer = roomRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, _currentColor);
                _propertyBlock.SetColor(ColorId, _currentColor);
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_EmissionColor"))
                {
                    Color emission = _currentColor * (_currentHazard * emissionPulse * 0.8f);
                    _propertyBlock.SetColor(EmissionColorId, emission);
                }

                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void ApplyWarningLights()
        {
            if (warningLights == null)
            {
                return;
            }

            float pulse = 0.82f + (Mathf.Sin(Time.time * warningPulseSpeed) * 0.18f);
            for (int i = 0; i < warningLights.Length; i++)
            {
                Light lightRef = warningLights[i];
                if (lightRef == null)
                {
                    continue;
                }

                lightRef.color = Color.Lerp(stableColor, floodingColor, _currentHazard);
                lightRef.intensity = warningLightMaxIntensity * _currentHazard * pulse;
            }
        }

        private void ApplyStainReceiverState()
        {
            if (stainReceivers == null)
            {
                return;
            }

            for (int i = 0; i < stainReceivers.Length; i++)
            {
                StainReceiver receiver = stainReceivers[i];
                if (receiver == null)
                {
                    continue;
                }

                receiver.ApplyRoomState(_currentHazard, _currentWashout);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugShowState)
            {
                return;
            }

            Gizmos.color = Color.Lerp(Color.green, Color.red, _currentHazard);
            Gizmos.DrawWireSphere(transform.position + (Vector3.up * 1.5f), 0.28f + (_currentHazard * 0.3f));
        }
    }
}
