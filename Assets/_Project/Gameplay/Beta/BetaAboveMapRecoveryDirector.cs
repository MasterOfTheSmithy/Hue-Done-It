// File: Assets/_Project/Gameplay/Beta/BetaAboveMapRecoveryDirector.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Prevents players from getting stuck above the generated map/ceiling layer. If a living player is above
    /// the intended play volume, the server snaps them back down at the same X/Z into the nearest valid floor pocket.
    /// </summary>
    [DefaultExecutionOrder(-845)]
    [DisallowMultipleComponent]
    public sealed class BetaAboveMapRecoveryDirector : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float scanIntervalSeconds = 0.20f;
        [SerializeField] private float maxPlayableY = 6.75f;
        [SerializeField] private float recoveryProbeStartY = 18f;
        [SerializeField] private float recoveryProbeDistance = 32f;
        [SerializeField] private float playerHeightOffset = 1.35f;
        [SerializeField] private float horizontalClamp = 38f;
        [SerializeField] private LayerMask floorMask = ~0;

        private readonly Dictionary<ulong, Vector3> _lastSafePositionByClient = new();
        private readonly Dictionary<ulong, float> _lastSafeYawByClient = new();
        private float _nextScanTime;

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                return;
            }

            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            ScanPlayers(manager);
        }

        private void ScanPlayers(NetworkManager manager)
        {
            foreach (System.Collections.Generic.KeyValuePair<ulong, NetworkClient> pair in manager.ConnectedClients)
            {
                NetworkObject playerObject = pair.Value.PlayerObject;
                if (playerObject == null || !playerObject.IsSpawned)
                {
                    continue;
                }

                PlayerLifeState life = playerObject.GetComponent<PlayerLifeState>();
                if (life != null && !life.IsAlive)
                {
                    continue;
                }

                Vector3 position = playerObject.transform.position;
                if (position.y <= maxPlayableY)
                {
                    CacheLastSafePosition(pair.Key, playerObject, position);
                    continue;
                }

                Vector3 recovery = ResolveRecoveryPosition(pair.Key, playerObject, position);
                float yaw = _lastSafeYawByClient.TryGetValue(pair.Key, out float cachedYaw)
                    ? cachedYaw
                    : playerObject.transform.eulerAngles.y;
                Teleport(playerObject, recovery, yaw);
            }
        }

        private Vector3 ResolveRecoveryPosition(ulong clientId, NetworkObject playerObject, Vector3 currentPosition)
        {
            if (_lastSafePositionByClient.TryGetValue(clientId, out Vector3 safePosition))
            {
                return safePosition;
            }

            float x = Mathf.Clamp(currentPosition.x, -horizontalClamp, horizontalClamp);
            float z = Mathf.Clamp(currentPosition.z, -horizontalClamp, horizontalClamp);
            Vector3 probe = new Vector3(x, recoveryProbeStartY, z);

            RaycastHit[] hits = Physics.RaycastAll(probe, Vector3.down, recoveryProbeDistance, floorMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.point.y >= maxPlayableY - 0.5f)
                {
                    continue;
                }

                if (IsLikelyCeilingOrRoof(hit.collider))
                {
                    continue;
                }

                return hit.point + Vector3.up * playerHeightOffset;
            }

            return new Vector3(x, playerHeightOffset, z);
        }

        private void CacheLastSafePosition(ulong clientId, NetworkObject playerObject, Vector3 position)
        {
            if (playerObject == null)
            {
                return;
            }

            if (playerObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
            {
                NetworkPlayerAuthoritativeMover.LocomotionState state = mover.CurrentState;
                bool stateLooksSafe = state == NetworkPlayerAuthoritativeMover.LocomotionState.Grounded ||
                                      state == NetworkPlayerAuthoritativeMover.LocomotionState.WallStick ||
                                      state == NetworkPlayerAuthoritativeMover.LocomotionState.LowGravityFloat;
                if (!stateLooksSafe)
                {
                    return;
                }
            }

            Vector3 clamped = new Vector3(
                Mathf.Clamp(position.x, -horizontalClamp, horizontalClamp),
                Mathf.Min(position.y, maxPlayableY - 0.75f),
                Mathf.Clamp(position.z, -horizontalClamp, horizontalClamp));

            _lastSafePositionByClient[clientId] = clamped;
            _lastSafeYawByClient[clientId] = playerObject.transform.eulerAngles.y;
        }

        private bool IsLikelyCeilingOrRoof(Collider colliderRef)
        {
            string name = colliderRef.name.ToLowerInvariant();
            return name.Contains("ceiling") ||
                   name.Contains("roof") ||
                   name.Contains("top") ||
                   name.Contains("outer rail") ||
                   name.Contains("wall");
        }

        private static void Teleport(NetworkObject playerObject, Vector3 position, float yaw)
        {
            if (playerObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
            {
                mover.ServerTeleportTo(position, yaw);
                return;
            }

            playerObject.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
