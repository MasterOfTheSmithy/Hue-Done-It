// File: Assets/_Project/Gameplay/Players/SimpleCpuOpponentAgent.cs
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkPlayerAuthoritativeMover))]
    [RequireComponent(typeof(PlayerKillInputController))]
    [RequireComponent(typeof(PlayerFloodZoneTracker))]
    public sealed class SimpleCpuOpponentAgent : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float repathIntervalSeconds = 0.45f;
        [SerializeField, Min(0.5f)] private float wanderRadius = 8.5f;
        [SerializeField, Min(0.1f)] private float jumpIntervalMin = 2.2f;
        [SerializeField, Min(0.1f)] private float jumpIntervalMax = 5.6f;
        [SerializeField, Min(0.5f)] private float interactDistance = 2.15f;
        [SerializeField, Min(0.5f)] private float eliminationCheckDistance = 2.7f;
        [SerializeField, Min(0.2f)] private float floodEvadeBiasDistance = 4.6f;

        private NetworkPlayerAuthoritativeMover _mover;
        private PlayerKillInputController _killer;
        private PlayerFloodZoneTracker _flood;
        private NetworkRoundState _round;
        private PumpRepairTask _pump;

        private Vector3 _moveTarget;
        private float _nextRepathTime;
        private float _nextJumpTime;

        private void Awake()
        {
            _mover = GetComponent<NetworkPlayerAuthoritativeMover>();
            _killer = GetComponent<PlayerKillInputController>();
            _flood = GetComponent<PlayerFloodZoneTracker>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (TryGetComponent(out LocalPlayerCameraBinder cameraBinder))
            {
                cameraBinder.enabled = false;
            }

            if (TryGetComponent(out NetworkPlayerInputReader inputReader))
            {
                inputReader.enabled = false;
            }

            if (TryGetComponent(out PlayerInteractionController interactionController))
            {
                interactionController.enabled = false;
            }

            if (!IsServer)
            {
                enabled = false;
                return;
            }

            _nextRepathTime = Time.time + 0.05f;
            _nextJumpTime = Time.time + Random.Range(jumpIntervalMin, jumpIntervalMax);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && _mover != null)
            {
                _mover.ServerClearExternalInput();
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            ResolveReferences();
            if (_round == null || _mover == null)
            {
                return;
            }

            if (_round.CurrentPhase != RoundPhase.FreeRoam)
            {
                _mover.ServerSetExternalInput(Vector3.zero, transform.eulerAngles.y, false, false, false, 0.2f);
                return;
            }

            if (_killer != null && _killer.CurrentRole == PlayerRole.Bleach)
            {
                TryServerElimination();
            }

            if (_pump != null && !_pump.IsCompleted && !_pump.IsLocked)
            {
                HandlePumpObjective();
                return;
            }

            HandleWander();
        }

        private void HandlePumpObjective()
        {
            bool inDanger = _flood != null && _flood.CurrentZoneState is FloodZoneState.Flooding or FloodZoneState.Submerged;
            Vector3 desiredTarget = _pump.transform.position;
            if (inDanger)
            {
                desiredTarget += (transform.position - _pump.transform.position).normalized * floodEvadeBiasDistance;
            }

            MoveToward(desiredTarget);

            float distance = Vector3.Distance(transform.position, _pump.transform.position);
            if (distance > interactDistance)
            {
                return;
            }

            if (_pump.CurrentState == RepairTaskState.Idle || _pump.CurrentState == RepairTaskState.Cancelled || _pump.CurrentState == RepairTaskState.FailedAttempt)
            {
                InteractionContext context = new(NetworkObject, OwnerClientId, true);
                _pump.TryBeginTask(context);
                return;
            }

            if (_pump.CurrentState == RepairTaskState.InProgress && _pump.ActiveClientId == OwnerClientId)
            {
                float progress = _pump.GetCurrentProgress01();
                float commitMid = (_pump.ConfirmationWindowStartNormalized + _pump.ConfirmationWindowEndNormalized) * 0.5f;
                if (progress >= commitMid)
                {
                    float elapsedSeconds = _pump.TaskDurationSeconds * progress;
                    _pump.ServerTryResolveConfirmation(elapsedSeconds, OwnerClientId);
                }
            }
        }

        private void HandleWander()
        {
            if (Time.time >= _nextRepathTime || Vector3.Distance(transform.position, _moveTarget) < 1.1f)
            {
                _nextRepathTime = Time.time + repathIntervalSeconds;
                Vector2 ring = Random.insideUnitCircle * wanderRadius;
                _moveTarget = new Vector3(ring.x, transform.position.y, ring.y);
            }

            MoveToward(_moveTarget);
        }

        private void MoveToward(Vector3 target)
        {
            Vector3 planarDelta = target - transform.position;
            planarDelta.y = 0f;

            if (planarDelta.sqrMagnitude < 0.15f)
            {
                _mover.ServerSetExternalInput(Vector3.zero, transform.eulerAngles.y, false, false, false, 0.2f);
                return;
            }

            Vector3 direction = planarDelta.normalized;
            float yaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
            bool shouldJump = Time.time >= _nextJumpTime;
            if (shouldJump)
            {
                _nextJumpTime = Time.time + Random.Range(jumpIntervalMin, jumpIntervalMax);
            }

            _mover.ServerSetExternalInput(direction, yaw, shouldJump, false, false, 0.25f);
        }

        private void TryServerElimination()
        {
            EliminationManager manager = FindFirstObjectByType<EliminationManager>();
            if (manager == null)
            {
                return;
            }

            PlayerLifeState[] lifeStates = FindObjectsByType<PlayerLifeState>(FindObjectsSortMode.None);
            PlayerLifeState target = null;
            float bestDist = float.MaxValue;
            foreach (PlayerLifeState life in lifeStates)
            {
                if (life == null || !life.IsAlive || life.gameObject == gameObject)
                {
                    continue;
                }

                if (!life.TryGetComponent(out PlayerKillInputController targetKill) || targetKill.CurrentRole != PlayerRole.Color)
                {
                    continue;
                }

                float dist = Vector3.Distance(transform.position, life.transform.position);
                if (dist < eliminationCheckDistance && dist < bestDist)
                {
                    bestDist = dist;
                    target = life;
                }
            }

            if (target != null && target.TryGetComponent(out NetworkObject targetNetworkObject))
            {
                manager.TryHandleEliminationRequest(NetworkObject, targetNetworkObject.NetworkObjectId);
            }
        }

        private void ResolveReferences()
        {
            _round ??= FindFirstObjectByType<NetworkRoundState>();
            _pump ??= FindFirstObjectByType<PumpRepairTask>();
        }
    }
}
