// File: Assets/_Project/Gameplay/Beta/BetaPlayerMovementStuckGuard.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(650)]
    public sealed class BetaPlayerMovementStuckGuard : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float scanIntervalSeconds = 0.35f;
        [SerializeField] private float minimumY = -4f;
        [SerializeField] private float maximumHorizontalDistance = 70f;
        [SerializeField] private float depenetrationSkin = 0.04f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private readonly Dictionary<NetworkPlayerAuthoritativeMover, Vector3> _lastPositions = new();
        private readonly Collider[] _overlapBuffer = new Collider[16];
        private float _nextScanTime;

        private void Update()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && !networkManager.IsServer)
            {
                return;
            }

            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            ScanPlayers();
        }

        private void ScanPlayers()
        {
            NetworkPlayerAuthoritativeMover[] movers = FindObjectsByType<NetworkPlayerAuthoritativeMover>(FindObjectsSortMode.None);
            for (int i = 0; i < movers.Length; i++)
            {
                NetworkPlayerAuthoritativeMover mover = movers[i];
                if (mover == null || !mover.IsSpawned)
                {
                    continue;
                }

                Vector3 position = mover.transform.position;
                if (position.y < minimumY || new Vector2(position.x, position.z).magnitude > maximumHorizontalDistance)
                {
                    TeleportToNearestSafeFloor(mover, position);
                    continue;
                }

                TryDepenetrate(mover);
                _lastPositions[mover] = mover.transform.position;
            }
        }

        private void TeleportToNearestSafeFloor(NetworkPlayerAuthoritativeMover mover, Vector3 fromPosition)
        {
            Vector3 probe = fromPosition;
            probe.x = Mathf.Clamp(probe.x, -24f, 24f);
            probe.z = Mathf.Clamp(probe.z, -18f, 18f);
            probe.y = 24f;

            Vector3 target = new Vector3(0f, 2f, 0f);
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 60f, collisionMask, QueryTriggerInteraction.Ignore))
            {
                target = hit.point + Vector3.up * 1.25f;
            }

            mover.ServerTeleportTo(target, mover.transform.eulerAngles.y);
        }

        private void TryDepenetrate(NetworkPlayerAuthoritativeMover mover)
        {
            CapsuleCollider capsule = mover.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                return;
            }

            Transform t = mover.transform;
            Vector3 scale = t.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(capsule.height * Mathf.Abs(scale.y), radius * 2f);
            Vector3 center = t.position + t.rotation * Vector3.Scale(capsule.center, scale);
            float segmentHalf = Mathf.Max(0f, (height * 0.5f) - radius);
            Vector3 pointA = center + (t.up * segmentHalf);
            Vector3 pointB = center - (t.up * segmentHalf);

            int hitCount = Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, _overlapBuffer, collisionMask, QueryTriggerInteraction.Ignore);
            Vector3 resolved = t.position;
            bool moved = false;

            for (int i = 0; i < hitCount; i++)
            {
                Collider other = _overlapBuffer[i];
                if (other == null || other == capsule || other.transform.IsChildOf(t))
                {
                    continue;
                }

                if (Physics.ComputePenetration(
                        capsule, resolved, t.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float distance))
                {
                    resolved += direction * (distance + depenetrationSkin);
                    moved = true;
                }
            }

            if (moved)
            {
                mover.ServerTeleportTo(resolved, t.eulerAngles.y);
            }
        }
    }
}
