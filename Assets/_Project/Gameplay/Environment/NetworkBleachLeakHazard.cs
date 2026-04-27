// File: Assets/_Project/Gameplay/Environment/NetworkBleachLeakHazard.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Environment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider))]
    public sealed class NetworkBleachLeakHazard : NetworkBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string hazardId = "bleach-leak";
        [SerializeField] private string displayName = "Bleach Leak";

        [Header("Threat")]
        [SerializeField, Min(0.1f)] private float exposureSecondsToDiffuse = 5.5f;
        [SerializeField, Min(0f)] private float exposureRecoveryPerSecond = 1.35f;
        [SerializeField, Range(0f, 1f)] private float warningExposure01 = 0.55f;
        [SerializeField, Min(0.1f)] private float paintPulseIntervalSeconds = 0.45f;
        [SerializeField, Min(0f)] private float knockbackOnDiffuse = 3.5f;

        [Header("Counterplay")]
        [SerializeField, Min(0.1f)] private float defaultSuppressionSeconds = 38f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color activeColor = new(0.94f, 0.96f, 1f, 0.38f);
        [SerializeField] private Color suppressedColor = new(0.18f, 0.68f, 1f, 0.18f);
        [SerializeField] private Color warningColor = new(1f, 0.35f, 0.35f, 0.5f);

        private readonly NetworkVariable<float> _suppressedUntilServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _statusText =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Dictionary<ulong, float> _exposures = new();
        private readonly HashSet<ulong> _seenThisFrame = new();
        private MaterialPropertyBlock _block;
        private float _nextPaintPulseTime;
        private NetworkRoundState _roundState;

        public string HazardId => hazardId;
        public string DisplayName => displayName;
        public bool IsSuppressed => GetServerTime() < _suppressedUntilServerTime.Value;
        public float SuppressionRemaining => Mathf.Max(0f, _suppressedUntilServerTime.Value - GetServerTime());
        public string StatusText => _statusText.Value.ToString();

        private void Awake()
        {
            Collider colliderRef = GetComponent<Collider>();
            colliderRef.isTrigger = true;

            if (statusRenderer == null)
            {
                statusRenderer = GetComponentInChildren<Renderer>();
            }

            ApplyColor(activeColor);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _suppressedUntilServerTime.OnValueChanged += HandleSuppressionChanged;

            if (IsServer)
            {
                _statusText.Value = new FixedString64Bytes(displayName + " active");
            }

            ApplyColor(IsSuppressed ? suppressedColor : activeColor);
        }

        public override void OnNetworkDespawn()
        {
            _suppressedUntilServerTime.OnValueChanged -= HandleSuppressionChanged;
            base.OnNetworkDespawn();
        }

        private void FixedUpdate()
        {
            if (!IsServer)
            {
                ApplyColor(IsSuppressed ? suppressedColor : activeColor);
                return;
            }

            ResolveRoundState();
            DecayAbsentExposures(Time.fixedDeltaTime);
            _seenThisFrame.Clear();

            if (IsSuppressed)
            {
                _statusText.Value = new FixedString64Bytes($"{displayName} suppressed {Mathf.CeilToInt(SuppressionRemaining)}s");
                ApplyColor(suppressedColor);
            }
            else
            {
                _statusText.Value = new FixedString64Bytes(displayName + " active");
                ApplyColor(activeColor);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!IsServer || IsSuppressed || !IsRoundDangerous())
            {
                return;
            }

            NetworkObject playerObject = other.GetComponentInParent<NetworkObject>();
            if (playerObject == null || !playerObject.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
            {
                return;
            }

            ulong objectId = playerObject.NetworkObjectId;
            _seenThisFrame.Add(objectId);

            float exposure = _exposures.TryGetValue(objectId, out float existing) ? existing : 0f;
            exposure += Time.fixedDeltaTime;
            _exposures[objectId] = exposure;

            float exposure01 = exposureSecondsToDiffuse <= 0.001f ? 1f : Mathf.Clamp01(exposure / exposureSecondsToDiffuse);
            if (exposure01 >= warningExposure01)
            {
                ApplyColor(warningColor);
            }

            if (Time.time >= _nextPaintPulseTime && playerObject.TryGetComponent(out NetworkPlayerPaintEmitter paintEmitter))
            {
                _nextPaintPulseTime = Time.time + paintPulseIntervalSeconds;
                paintEmitter.ServerEmitPaint(
                    PaintEventKind.FloodDrip,
                    playerObject.transform.position + Vector3.up * 0.12f,
                    Vector3.up,
                    Mathf.Lerp(0.16f, 0.34f, exposure01),
                    Mathf.Lerp(0.35f, 0.85f, exposure01));
            }

            if (exposure < exposureSecondsToDiffuse)
            {
                return;
            }

            if (playerObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
            {
                Vector3 away = (playerObject.transform.position - transform.position).normalized;
                if (away.sqrMagnitude < 0.001f)
                {
                    away = Vector3.up;
                }

                mover.ServerApplyKnockback((away * knockbackOnDiffuse) + Vector3.up * 1.4f);
            }

            if (lifeState.ServerTrySetDiffused(displayName + " exposure"))
            {
                _exposures.Remove(objectId);
            }
        }

        public void ConfigureRuntime(string id, string label, float secondsToDiffuse, float suppressionSeconds, Color leakColor, Renderer renderer = null)
        {
            hazardId = string.IsNullOrWhiteSpace(id) ? hazardId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            exposureSecondsToDiffuse = Mathf.Max(0.1f, secondsToDiffuse);
            defaultSuppressionSeconds = Mathf.Max(0.1f, suppressionSeconds);
            activeColor = leakColor;
            activeColor.a = Mathf.Clamp(activeColor.a <= 0f ? 0.38f : activeColor.a, 0.12f, 0.7f);
            statusRenderer = renderer != null ? renderer : GetComponentInChildren<Renderer>();
            ApplyColor(IsSuppressed ? suppressedColor : activeColor);
        }

        public bool ServerSuppressFor(float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            float duration = seconds > 0f ? seconds : defaultSuppressionSeconds;
            _suppressedUntilServerTime.Value = Mathf.Max(_suppressedUntilServerTime.Value, GetServerTime() + duration);
            _exposures.Clear();
            _statusText.Value = new FixedString64Bytes(string.IsNullOrWhiteSpace(reason)
                ? displayName + " suppressed"
                : reason);
            ApplyColor(suppressedColor);
            return true;
        }

        public bool ServerReactivate(string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            _suppressedUntilServerTime.Value = 0f;
            _statusText.Value = new FixedString64Bytes(string.IsNullOrWhiteSpace(reason) ? displayName + " reactivated" : reason);
            ApplyColor(activeColor);
            return true;
        }

        private void DecayAbsentExposures(float deltaTime)
        {
            if (_exposures.Count == 0 || exposureRecoveryPerSecond <= 0f)
            {
                return;
            }

            List<ulong> keys = new(_exposures.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                ulong key = keys[i];
                if (_seenThisFrame.Contains(key))
                {
                    continue;
                }

                float next = _exposures[key] - exposureRecoveryPerSecond * deltaTime;
                if (next <= 0f)
                {
                    _exposures.Remove(key);
                }
                else
                {
                    _exposures[key] = next;
                }
            }
        }

        private bool IsRoundDangerous()
        {
            ResolveRoundState();
            return _roundState == null || _roundState.IsFreeRoam;
        }

        private void ResolveRoundState()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }
        }

        private void HandleSuppressionChanged(float previous, float current)
        {
            ApplyColor(IsSuppressed ? suppressedColor : activeColor);
        }

        private void ApplyColor(Color color)
        {
            if (statusRenderer == null)
            {
                statusRenderer = GetComponentInChildren<Renderer>();
            }

            if (statusRenderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            statusRenderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            statusRenderer.SetPropertyBlock(_block);
        }

        private float GetServerTime()
        {
            return NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.unscaledTime;
        }
    }
}
