// File: Assets/_Project/Flood/Integration/PlayerFloodZoneTracker.cs
using System;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Flood.Integration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerFloodZoneTracker : NetworkBehaviour
    {
        [SerializeField] private float probeRadius = 0.35f;
        [SerializeField] private LayerMask zoneMask = ~0;
        [SerializeField] private int overlapBufferSize = 8;

        private Collider[] _overlapResults;
        private FloodZone _currentZone;

        public event Action<FloodZone> ZoneChanged;

        public FloodZone CurrentZone => _currentZone;
        public FloodZoneState CurrentZoneState => _currentZone == null ? FloodZoneState.Dry : _currentZone.CurrentState;
        public bool IsCurrentZoneSafe => _currentZone == null || _currentZone.IsSafe;

        private void Awake()
        {
            _overlapResults = new Collider[Mathf.Max(1, overlapBufferSize)];
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
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
