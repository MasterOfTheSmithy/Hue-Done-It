// File: Assets/_Project/Gameplay/Elimination/PlayerKillInputController.cs
using HueDoneIt.Gameplay.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Gameplay.Elimination
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerLifeState))]
    public sealed class PlayerKillInputController : NetworkBehaviour
    {
        [SerializeField] private Key killKey = Key.F;
        [SerializeField, Min(0.1f)] private float targetSearchRange = 2.5f;
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private bool isTestKillerEnabled;

        private PlayerLifeState _lifeState;
        private Collider[] _overlapResults;
        private const int MaxSearchHits = 16;

        public bool IsTestKillerEnabled => isTestKillerEnabled;

        private void Awake()
        {
            _lifeState = GetComponent<PlayerLifeState>();
            _overlapResults = new Collider[MaxSearchHits];
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient || !isTestKillerEnabled || !_lifeState.IsAlive)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[killKey].wasPressedThisFrame)
            {
                return;
            }

            if (!TryFindClosestTarget(out NetworkObject target))
            {
                return;
            }

            RequestEliminateTargetServerRpc(target.NetworkObjectId);
        }

        private bool TryFindClosestTarget(out NetworkObject target)
        {
            target = null;
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, targetSearchRange, _overlapResults, targetMask, QueryTriggerInteraction.Collide);
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapResults[i];
                if (hit == null)
                {
                    continue;
                }

                NetworkObject candidate = hit.GetComponentInParent<NetworkObject>();
                if (candidate == null || candidate == NetworkObject)
                {
                    continue;
                }

                if (!candidate.TryGetComponent(out PlayerLifeState candidateLifeState) || !candidateLifeState.IsAlive)
                {
                    continue;
                }

                float distanceSqr = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                target = candidate;
            }

            return target != null;
        }

        [ServerRpc]
        private void RequestEliminateTargetServerRpc(ulong targetNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (senderClientId != OwnerClientId)
            {
                return;
            }

            if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient) || senderClient.PlayerObject == null)
            {
                return;
            }

            if (senderClient.PlayerObject != NetworkObject)
            {
                return;
            }

            if (!_lifeState.IsAlive || !isTestKillerEnabled)
            {
                return;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState != null && !roundState.IsFreeRoam)
            {
                return;
            }

            EliminationManager eliminationManager = FindFirstObjectByType<EliminationManager>();
            if (eliminationManager == null)
            {
                Debug.LogError("No EliminationManager found in scene.");
                return;
            }

            eliminationManager.TryHandleEliminationRequest(NetworkObject, targetNetworkObjectId);
        }
    }
}
