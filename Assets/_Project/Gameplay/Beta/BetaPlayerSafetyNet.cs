// File: Assets/_Project/Gameplay/Beta/BetaPlayerSafetyNet.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Server-side beta recovery system. It catches players that fall out of generated-map bounds or become stranded
    /// too far from the playable loop and returns them to a conservative safe anchor instead of leaving the match broken.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaPlayerSafetyNet : MonoBehaviour
    {
        [SerializeField, Min(0.25f)] private float tickIntervalSeconds = 0.75f;
        [SerializeField] private float fallRecoveryY = -8f;
        [SerializeField, Min(10f)] private float maxHorizontalDistanceFromOrigin = 95f;
        [SerializeField, Min(0.5f)] private float safeTeleportHeightOffset = 1.4f;
        [SerializeField, Min(0.25f)] private float minimumAnchorSeparation = 1.6f;
        [SerializeField] private LayerMask floorProbeMask = ~0;

        private readonly List<Vector3> _safeAnchors = new();
        private readonly Dictionary<ulong, float> _lastRecoveryLogTime = new();
        private float _nextTickTime;
        private float _nextAnchorRefreshTime;

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                return;
            }

            if (Time.unscaledTime < _nextTickTime)
            {
                return;
            }

            _nextTickTime = Time.unscaledTime + tickIntervalSeconds;
            RefreshAnchorsIfNeeded();
            CheckPlayers(manager);
        }

        private void CheckPlayers(NetworkManager manager)
        {
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
                    continue;
                }

                Vector3 position = playerObject.transform.position;
                bool belowWorld = position.y < fallRecoveryY;
                bool farAway = new Vector2(position.x, position.z).magnitude > maxHorizontalDistanceFromOrigin;
                if (!belowWorld && !farAway)
                {
                    continue;
                }

                RecoverPlayer(pair.Key, playerObject, belowWorld ? "fell below map" : "left playable bounds");
            }
        }

        private void RecoverPlayer(ulong clientId, NetworkObject playerObject, string reason)
        {
            if (!TryResolveSafeAnchor(playerObject.transform.position, out Vector3 safePosition))
            {
                safePosition = new Vector3(0f, safeTeleportHeightOffset, 0f);
            }

            float yaw = playerObject.transform.eulerAngles.y;
            if (playerObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
            {
                mover.ServerTeleportTo(safePosition, yaw);
            }
            else
            {
                playerObject.transform.SetPositionAndRotation(safePosition, Quaternion.Euler(0f, yaw, 0f));
            }

            if (!_lastRecoveryLogTime.TryGetValue(clientId, out float lastLog) || Time.unscaledTime - lastLog > 5f)
            {
                _lastRecoveryLogTime[clientId] = Time.unscaledTime;
                Debug.Log($"[BetaPlayerSafetyNet] Recovered client {clientId}: {reason} -> {safePosition}.");
            }
        }

        private void RefreshAnchorsIfNeeded()
        {
            if (Time.unscaledTime < _nextAnchorRefreshTime && _safeAnchors.Count > 0)
            {
                return;
            }

            _nextAnchorRefreshTime = Time.unscaledTime + 6f;
            _safeAnchors.Clear();

            AddNamedAnchors();
            AddTaskAnchors();

            if (_safeAnchors.Count == 0)
            {
                _safeAnchors.Add(new Vector3(0f, safeTeleportHeightOffset, 0f));
            }
        }

        private void AddNamedAnchors()
        {
            Transform[] transforms = FindObjectsOfType<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                if (t == null)
                {
                    continue;
                }

                string name = t.name.ToLowerInvariant();
                if (!name.Contains("spawn") && !name.Contains("meeting") && !name.Contains("hub") && !name.Contains("safe"))
                {
                    continue;
                }

                TryAddAnchor(t.position);
            }
        }

        private void AddTaskAnchors()
        {
            NetworkRepairTask[] tasks = FindObjectsOfType<NetworkRepairTask>();
            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null || !task.IsTaskEnvironmentSafe())
                {
                    continue;
                }

                Vector3 offset = task.transform.position + new Vector3(0.8f, 0f, 0.8f);
                TryAddAnchor(offset);
            }
        }

        private void TryAddAnchor(Vector3 candidate)
        {
            Vector3 projected = ProjectToFloor(candidate);
            for (int i = 0; i < _safeAnchors.Count; i++)
            {
                if (Vector3.Distance(_safeAnchors[i], projected) < minimumAnchorSeparation)
                {
                    return;
                }
            }

            _safeAnchors.Add(projected);
        }

        private Vector3 ProjectToFloor(Vector3 candidate)
        {
            Vector3 probeStart = candidate + Vector3.up * 8f;
            if (Physics.Raycast(probeStart, Vector3.down, out RaycastHit hit, 24f, floorProbeMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * safeTeleportHeightOffset;
            }

            return candidate + Vector3.up * safeTeleportHeightOffset;
        }

        private bool TryResolveSafeAnchor(Vector3 fromPosition, out Vector3 safePosition)
        {
            RefreshAnchorsIfNeeded();

            safePosition = Vector3.zero;
            float bestDistance = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < _safeAnchors.Count; i++)
            {
                Vector3 anchor = _safeAnchors[i];
                float distance = Vector3.SqrMagnitude(new Vector3(fromPosition.x - anchor.x, 0f, fromPosition.z - anchor.z));
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                safePosition = anchor;
                found = true;
            }

            return found;
        }
    }
}
