// File: Assets/_Project/Gameplay/Beta/BetaSafeStartDirector.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Corrects bad initial placement during the first few seconds after the scene starts. This catches cases
    /// where players spawned before the map/layout repair finished, or where a floor probe landed on a seam.
    /// </summary>
    [DefaultExecutionOrder(-690)]
    [DisallowMultipleComponent]
    public sealed class BetaSafeStartDirector : MonoBehaviour
    {
        private static readonly Vector3[] SafeStartSlots =
        {
            new(-4.5f, 1.25f, -4.5f),
            new(4.5f, 1.25f, -4.5f),
            new(-4.5f, 1.25f, 4.5f),
            new(4.5f, 1.25f, 4.5f),
            new(0f, 1.25f, -6.25f),
            new(0f, 1.25f, 6.25f),
            new(-6.25f, 1.25f, 0f),
            new(6.25f, 1.25f, 0f)
        };

        [SerializeField, Min(0.1f)] private float checkIntervalSeconds = 0.5f;
        [SerializeField, Min(1f)] private float activeForSeconds = 10f;
        [SerializeField, Min(1f)] private float maxSafeStartRadius = 13.5f;
        [SerializeField] private LayerMask floorProbeMask = ~0;

        private float _startTime;
        private float _nextCheckTime;

        private void OnEnable()
        {
            _startTime = Time.unscaledTime;
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                return;
            }

            if (Time.unscaledTime - _startTime > activeForSeconds || Time.unscaledTime < _nextCheckTime)
            {
                return;
            }

            _nextCheckTime = Time.unscaledTime + checkIntervalSeconds;
            CheckPlayers(manager);
        }

        private void CheckPlayers(NetworkManager manager)
        {
            int slotIndex = 0;
            foreach (System.Collections.Generic.KeyValuePair<ulong, NetworkClient> pair in manager.ConnectedClients)
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
                bool tooFar = new Vector2(position.x, position.z).magnitude > maxSafeStartRadius;
                bool tooLow = position.y < 0.25f;
                bool unsupported = !Physics.Raycast(position + Vector3.up * 0.35f, Vector3.down, 2.25f, floorProbeMask, QueryTriggerInteraction.Ignore);
                if (!tooFar && !tooLow && !unsupported)
                {
                    slotIndex++;
                    continue;
                }

                Vector3 slot = SafeStartSlots[slotIndex % SafeStartSlots.Length];
                slotIndex++;
                Teleport(playerObject, slot);
            }
        }

        private static void Teleport(NetworkObject playerObject, Vector3 position)
        {
            float yaw = playerObject.transform.eulerAngles.y;
            Vector3 toCenter = Vector3.zero - position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.001f)
            {
                yaw = Quaternion.LookRotation(toCenter.normalized, Vector3.up).eulerAngles.y;
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
    }
}
