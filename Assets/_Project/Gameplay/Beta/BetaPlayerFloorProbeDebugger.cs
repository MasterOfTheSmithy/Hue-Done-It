// File: Assets/_Project/Gameplay/Beta/BetaPlayerFloorProbeDebugger.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Server-side floor-support debugger. Logs unsupported player positions and recovers living players only
    /// after repeated unsupported probes, so actual bugs are visible without leaving players falling forever.
    /// </summary>
    [DefaultExecutionOrder(-850)]
    [DisallowMultipleComponent]
    public sealed class BetaPlayerFloorProbeDebugger : MonoBehaviour
    {
        private static readonly Vector3[] SafeSlots =
        {
            new(-5.0f, 1.35f, -5.0f),
            new(5.0f, 1.35f, -5.0f),
            new(-5.0f, 1.35f, 5.0f),
            new(5.0f, 1.35f, 5.0f),
            new(0f, 1.35f, -7.0f),
            new(0f, 1.35f, 7.0f),
            new(-7.0f, 1.35f, 0f),
            new(7.0f, 1.35f, 0f),
            new(0f, 1.35f, 0f)
        };

        [SerializeField, Min(0.05f)] private float scanIntervalSeconds = 0.20f;
        [SerializeField, Min(1)] private int unsupportedScansBeforeRecovery = 2;
        [SerializeField] private float warningY = 0.10f;
        [SerializeField] private float immediateRecoverY = -1.25f;
        [SerializeField] private float horizontalRecoverLimit = 86f;
        [SerializeField] private LayerMask floorMask = ~0;

        private readonly Dictionary<ulong, int> _unsupportedCounts = new();
        private readonly Dictionary<ulong, float> _lastLogTimes = new();
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
            int slotIndex = 0;
            foreach (KeyValuePair<ulong, NetworkClient> pair in manager.ConnectedClients)
            {
                NetworkObject playerObject = pair.Value.PlayerObject;
                if (playerObject == null || !playerObject.IsSpawned)
                {
                    continue;
                }

                PlayerLifeState lifeState = playerObject.GetComponent<PlayerLifeState>();
                if (lifeState != null && !lifeState.IsAlive)
                {
                    _unsupportedCounts[pair.Key] = 0;
                    slotIndex++;
                    continue;
                }

                Vector3 position = playerObject.transform.position;
                bool hasFloor = HasFloor(position, out RaycastHit hit);
                bool tooLow = position.y < warningY;
                bool immediateRecover = position.y < immediateRecoverY;
                bool tooFar = new Vector2(position.x, position.z).magnitude > horizontalRecoverLimit;

                if (hasFloor && !tooFar && !immediateRecover)
                {
                    _unsupportedCounts[pair.Key] = 0;
                    slotIndex++;
                    continue;
                }

                int count = _unsupportedCounts.TryGetValue(pair.Key, out int previous) ? previous + 1 : 1;
                _unsupportedCounts[pair.Key] = count;

                LogRateLimited(pair.Key, "[FloorProbe] Player " + pair.Key +
                                         " unsupported/unsafe. count=" + count +
                                         " pos=" + position +
                                         " hasFloor=" + hasFloor +
                                         " hit=" + (hit.collider != null ? hit.collider.name : "<none>"));

                if (immediateRecover || tooFar || count >= unsupportedScansBeforeRecovery)
                {
                    Vector3 recovery = ResolveRecoveryPosition(position, slotIndex);
                    Teleport(playerObject, recovery);
                    _unsupportedCounts[pair.Key] = 0;
                    LogRateLimited(pair.Key + 13000, "[FloorProbe] Recovered player " + pair.Key + " to " + recovery);
                }

                slotIndex++;
            }
        }

        private bool HasFloor(Vector3 position, out RaycastHit hit)
        {
            Vector3 rayStart = position + Vector3.up * 0.55f;
            return Physics.Raycast(rayStart, Vector3.down, out hit, 3.25f, floorMask, QueryTriggerInteraction.Ignore);
        }

        private Vector3 ResolveRecoveryPosition(Vector3 fromPosition, int slotIndex)
        {
            Vector3 probe = fromPosition;
            probe.x = Mathf.Clamp(probe.x, -34f, 34f);
            probe.z = Mathf.Clamp(probe.z, -34f, 34f);
            probe.y = 16f;

            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 40f, floorMask, QueryTriggerInteraction.Ignore) &&
                hit.collider != null &&
                hit.collider.name.IndexOf("Global Invisible Catch", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return hit.point + Vector3.up * 1.35f;
            }

            return SafeSlots[Mathf.Abs(slotIndex) % SafeSlots.Length];
        }

        private static void Teleport(NetworkObject playerObject, Vector3 position)
        {
            float yaw = playerObject.transform.eulerAngles.y;
            Vector3 look = Vector3.zero - position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
            {
                yaw = Quaternion.LookRotation(look.normalized, Vector3.up).eulerAngles.y;
            }

            NetworkPlayerAuthoritativeMover mover = playerObject.GetComponent<NetworkPlayerAuthoritativeMover>();
            if (mover != null)
            {
                mover.ServerTeleportTo(position, yaw);
            }
            else
            {
                playerObject.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            }
        }

        private void LogRateLimited(ulong clientId, string message)
        {
            if (_lastLogTimes.TryGetValue(clientId, out float lastTime) && Time.unscaledTime - lastTime < 2.5f)
            {
                return;
            }

            _lastLogTimes[clientId] = Time.unscaledTime;
            Debug.Log(message);
        }
    }
}
