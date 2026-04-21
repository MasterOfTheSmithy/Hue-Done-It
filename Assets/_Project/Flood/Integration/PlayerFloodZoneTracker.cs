// File: Assets/_Project/Flood/Integration/PlayerFloodZoneTracker.cs
using System;
using HueDoneIt.Gameplay.Elimination;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Flood.Integration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerLifeState))]
    public sealed class PlayerFloodZoneTracker : NetworkBehaviour
    {
        [Header("Detection")]
        [SerializeField] private float probeRadius = 0.35f;
        [SerializeField] private LayerMask zoneMask = ~0;
        [SerializeField] private int overlapBufferSize = 8;
        [SerializeField] private float reportIntervalSeconds = 0.15f;

        [Header("Saturation")]
        [SerializeField] private float dryRecoveryPerSecond = 0.2f;
        [SerializeField] private float wetSaturationPerSecond = 0.08f;
        [SerializeField] private float floodingSaturationPerSecond = 0.22f;
        [SerializeField] private float submergedSaturationPerSecond = 0.45f;
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.7f;
        [SerializeField, Range(0f, 1f)] private float deathThreshold = 1f;
        [SerializeField] private bool instantlyDiffuseInSubmerged = true;

        private readonly NetworkVariable<ulong> _serverZoneNetworkObjectId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> _serverZoneState =
            new((byte)FloodZoneState.Dry, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _saturation =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Collider[] _overlapResults;
        private FloodZone _currentZone;
        private PlayerLifeState _lifeState;
        private float _nextReportTime;

        public event Action<FloodZone> ZoneChanged;

        public FloodZone CurrentZone => _currentZone;
        public FloodZoneState CurrentZoneState => (FloodZoneState)_serverZoneState.Value;
        public bool IsCurrentZoneSafe => CurrentZone == null || CurrentZone.IsSafe;
        public float Saturation01 => _saturation.Value;
        public bool IsCritical => Saturation01 >= criticalThreshold;

        private void Awake()
        {
            _overlapResults = new Collider[Mathf.Max(1, overlapBufferSize)];
            _lifeState = GetComponent<PlayerLifeState>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                _serverZoneNetworkObjectId.Value = ulong.MaxValue;
                _serverZoneState.Value = (byte)FloodZoneState.Dry;
                _saturation.Value = 0f;
            }
        }

        private void Update()
        {
            if (IsOwner && IsClient)
            {
                UpdateLocalObservedZone();
                SendZoneToServerIfNeeded();
            }

            if (IsServer)
            {
                SimulateServerSaturation(Time.deltaTime);
            }
        }

        public void ServerResetFloodState()
        {
            if (!IsServer)
            {
                return;
            }

            _serverZoneNetworkObjectId.Value = ulong.MaxValue;
            _serverZoneState.Value = (byte)FloodZoneState.Dry;
            _saturation.Value = 0f;
        }

        private void UpdateLocalObservedZone()
        {
            if (!IsSpawned)
            {
                SetCurrentZone(null);
                return;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, probeRadius, _overlapResults, zoneMask, QueryTriggerInteraction.Collide);

            FloodZone bestZone = null;
            float bestDistanceSqr = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapResults[i];
                if (hit == null)
                {
                    continue;
                }

                FloodZone zone = hit.GetComponentInParent<FloodZone>();
                if (zone == null)
                {
                    continue;
                }

                float distanceSqr = (hit.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestZone = zone;
                }
            }

            SetCurrentZone(bestZone);
        }

        private void SendZoneToServerIfNeeded()
        {
            if (!IsSpawned || Time.unscaledTime < _nextReportTime)
            {
                return;
            }

            _nextReportTime = Time.unscaledTime + Mathf.Max(0.05f, reportIntervalSeconds);
            ulong zoneId = _currentZone != null && _currentZone.NetworkObject != null
                ? _currentZone.NetworkObjectId
                : ulong.MaxValue;

            ReportObservedZoneServerRpc(zoneId);
        }

        private void SimulateServerSaturation(float deltaTime)
        {
            if (_lifeState != null && !_lifeState.IsAlive)
            {
                return;
            }

            FloodZoneState state = (FloodZoneState)_serverZoneState.Value;
            if (instantlyDiffuseInSubmerged && state == FloodZoneState.Submerged)
            {
                if (_lifeState != null && _lifeState.ServerTrySetDiffused("Caught in fast-moving flood"))
                {
                    _saturation.Value = deathThreshold;
                }

                return;
            }

            float delta = state switch
            {
                FloodZoneState.Wet => wetSaturationPerSecond,
                FloodZoneState.Flooding => floodingSaturationPerSecond,
                FloodZoneState.Submerged => submergedSaturationPerSecond,
                FloodZoneState.SealedSafe => -dryRecoveryPerSecond,
                _ => -dryRecoveryPerSecond
            };

            _saturation.Value = Mathf.Clamp01(_saturation.Value + (delta * deltaTime));
            if (_saturation.Value >= deathThreshold && _lifeState != null)
            {
                _lifeState.ServerTrySetDiffused("Flood saturation reached critical mass");
            }
        }

        [ServerRpc]
        private void ReportObservedZoneServerRpc(ulong zoneNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (zoneNetworkObjectId == ulong.MaxValue)
            {
                _serverZoneNetworkObjectId.Value = zoneNetworkObjectId;
                _serverZoneState.Value = (byte)FloodZoneState.Dry;
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(zoneNetworkObjectId, out NetworkObject zoneObject) ||
                !zoneObject.TryGetComponent(out FloodZone zone))
            {
                return;
            }

            float validationDistance = Vector3.Distance(transform.position, zone.transform.position);
            if (validationDistance > 8f)
            {
                return;
            }

            _serverZoneNetworkObjectId.Value = zoneNetworkObjectId;
            _serverZoneState.Value = (byte)zone.CurrentState;
        }

        private void SetCurrentZone(FloodZone zone)
        {
            if (_currentZone == zone)
            {
                return;
            }

            _currentZone = zone;
            ZoneChanged?.Invoke(_currentZone);
        }
    }
}
