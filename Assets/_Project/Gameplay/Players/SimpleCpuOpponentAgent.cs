// File: Assets/_Project/Gameplay/Players/SimpleCpuOpponentAgent.cs
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Gameplay.Sabotage;
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
        [Header("Navigation")]
        [SerializeField, Min(0.1f)] private float repathIntervalSeconds = 0.4f;
        [SerializeField, Min(0.5f)] private float wanderRadius = 8.5f;
        [SerializeField, Min(0.1f)] private float jumpIntervalMin = 2.2f;
        [SerializeField, Min(0.1f)] private float jumpIntervalMax = 5.6f;
        [SerializeField, Min(0.5f)] private float interactDistance = 2.15f;
        [SerializeField, Min(0.5f)] private float eliminationCheckDistance = 2.7f;
        [SerializeField, Min(0.2f)] private float floodEvadeBiasDistance = 4.6f;

        [Header("Task Logic")]
        [SerializeField, Min(0.1f)] private float taskRefreshIntervalSeconds = 0.75f;
        [SerializeField, Min(0.05f)] private float taskHoldPositionDistance = 0.8f;
        [SerializeField, Min(0.05f)] private float shipCheckpointBias = 0.02f;

        private NetworkPlayerAuthoritativeMover _mover;
        private PlayerKillInputController _killer;
        private PlayerFloodZoneTracker _flood;
        private NetworkRoundState _round;
        private PumpRepairTask _pump;

        private Vector3 _moveTarget;
        private Vector3 _lastPathSamplePosition;
        private Vector3 _avoidanceDirection;
        private float _nextRepathTime;
        private float _nextJumpTime;
        private float _nextTaskRefreshTime;
        private float _nextAvoidanceRefreshTime;
        private float _stuckSeconds;

        private NetworkRepairTask _currentTask;
        private TaskStepInteractable _currentAdvancedStep;
        private float _nextAdvancedTaskRefreshTime;
        private readonly List<float> _shipCheckpointFractions = new();
        private int _shipCheckpointIndex;
        private ulong _trackedTaskId = ulong.MaxValue;
        private float _shipCheckpointWindow;

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
            _nextTaskRefreshTime = Time.time + 0.2f;
            _lastPathSamplePosition = transform.position;
            _avoidanceDirection = transform.forward;
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

            if (_round.CurrentPhase == RoundPhase.Reported)
            {
                ResetTaskState();
                if (HandleMeetingVoteLoop())
                {
                    return;
                }

                _mover.ServerSetExternalInput(Vector3.zero, transform.eulerAngles.y, false, false, false, 0.2f);
                return;
            }

            if (_round.CurrentPhase != RoundPhase.FreeRoam)
            {
                ResetTaskState();
                _mover.ServerSetExternalInput(Vector3.zero, transform.eulerAngles.y, false, false, false, 0.2f);
                return;
            }

            if (ShouldEvadeFlood())
            {
                MoveToward(ResolveFloodEvadeTarget());
                return;
            }

            if (_killer != null && _killer.CurrentRole == PlayerRole.Bleach)
            {
                if (HandleBleachLoop())
                {
                    return;
                }
            }
            else
            {
                if (HandleSafeRoomLoop())
                {
                    return;
                }

                if (HandlePaintScannerLoop())
                {
                    return;
                }

                if (HandleSecurityCameraLoop())
                {
                    return;
                }

                if (HandleAlarmTripwireLoop())
                {
                    return;
                }

                if (HandleInkWellLoop())
                {
                    return;
                }

                if (HandleCrewRallyLoop())
                {
                    return;
                }

                if (HandleBulkheadLockLoop())
                {
                    return;
                }

                if (HandleCalloutBeaconLoop())
                {
                    return;
                }

                if (HandleVitalsLoop())
                {
                    return;
                }

                if (HandleBleachVentSealLoop())
                {
                    return;
                }

                if (HandleDecontaminationLoop())
                {
                    return;
                }

                if (HandleEmergencySealLoop())
                {
                    return;
                }

                if (HandleFloodgateLoop())
                {
                    return;
                }

                if (HandleAdvancedTaskLoop())
                {
                    return;
                }

                if (HandleTaskLoop())
                {
                    return;
                }
            }

            HandleWander();
        }

        private bool HandleMeetingVoteLoop()
        {
            if (_round == null || _round.CurrentPhase != RoundPhase.Reported || _round.HasMeetingVoteFrom(OwnerClientId))
            {
                return false;
            }

            NetworkVotingPodium[] podiums = FindObjectsByType<NetworkVotingPodium>(FindObjectsSortMode.None);
            NetworkVotingPodium bestPodium = null;
            float bestScore = float.MaxValue;
            bool preferAccuse = _killer != null && _killer.CurrentRole == PlayerRole.Bleach;

            for (int i = 0; i < podiums.Length; i++)
            {
                NetworkVotingPodium podium = podiums[i];
                if (podium == null)
                {
                    continue;
                }

                float score = Vector3.Distance(transform.position, podium.transform.position);
                if (preferAccuse && !podium.IsSkipVote)
                {
                    score -= 6f;
                }
                else if (!preferAccuse && podium.IsSkipVote)
                {
                    score -= 4f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPodium = podium;
                }
            }

            if (bestPodium == null)
            {
                return false;
            }

            MoveToward(bestPodium.transform.position);
            if (Vector3.Distance(transform.position, bestPodium.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (bestPodium.CanInteract(context))
            {
                bestPodium.TryInteract(context);
            }

            return true;
        }

        private bool HandleBleachLoop()
        {
            if (TryMoveTowardColorTarget(out PlayerLifeState _))
            {
                TryServerElimination();
                return true;
            }

            if (TryUseSabotageConsole())
            {
                return true;
            }

            if (TryUseFalseEvidenceStation())
            {
                return true;
            }

            if (TryUseCalloutBeacon())
            {
                return true;
            }

            if (TryUseBleachVentForTraversal())
            {
                return true;
            }

            if (_pump != null && !_pump.IsCompleted && !_pump.IsLocked)
            {
                MoveToward(_pump.transform.position);
                if (Vector3.Distance(transform.position, _pump.transform.position) <= interactDistance)
                {
                    _pump.ServerTryApplyCorrupt(OwnerClientId);
                }

                return true;
            }

            return false;
        }

        private bool TryUseSabotageConsole()
        {
            NetworkSabotageConsole[] consoles = FindObjectsByType<NetworkSabotageConsole>(FindObjectsSortMode.None);
            NetworkSabotageConsole bestConsole = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < consoles.Length; i++)
            {
                NetworkSabotageConsole sabotageConsole = consoles[i];
                if (sabotageConsole == null || !sabotageConsole.IsReady)
                {
                    continue;
                }

                float distanceSqr = (sabotageConsole.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestConsole = sabotageConsole;
                }
            }

            if (bestConsole == null)
            {
                return false;
            }

            MoveToward(bestConsole.transform.position);
            if (Vector3.Distance(transform.position, bestConsole.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            bestConsole.TryInteract(context);
            return true;
        }

        private bool TryUseFalseEvidenceStation()
        {
            NetworkFalseEvidenceStation[] stations = FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None);
            NetworkFalseEvidenceStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkFalseEvidenceStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            if (bestStation == null || bestDistanceSqr > 100f)
            {
                return false;
            }

            MoveToward(bestStation.transform.position);
            if (Vector3.Distance(transform.position, bestStation.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (bestStation.CanInteract(context))
            {
                bestStation.TryInteract(context);
            }

            return true;
        }

        private bool TryUseCalloutBeacon()
        {
            if (_round == null || _round.SabotageEventCount < 1)
            {
                return false;
            }

            NetworkCalloutBeacon[] beacons = FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None);
            NetworkCalloutBeacon bestBeacon = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < beacons.Length; i++)
            {
                NetworkCalloutBeacon beacon = beacons[i];
                if (beacon == null || !beacon.IsReady)
                {
                    continue;
                }

                float distanceSqr = (beacon.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestBeacon = beacon;
                }
            }

            if (bestBeacon == null || bestDistanceSqr > 144f)
            {
                return false;
            }

            MoveToward(bestBeacon.transform.position);
            if (Vector3.Distance(transform.position, bestBeacon.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (bestBeacon.CanInteract(context))
            {
                bestBeacon.TryInteract(context);
            }

            return true;
        }

        private bool HandleSecurityCameraLoop()
        {
            NetworkSecurityCameraStation station = ResolveBestSecurityCameraStation();
            if (station == null)
            {
                return false;
            }

            MoveToward(station.transform.position);
            if (Vector3.Distance(transform.position, station.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (station.CanInteract(context))
            {
                station.TryInteract(context);
            }

            return true;
        }

        private NetworkSecurityCameraStation ResolveBestSecurityCameraStation()
        {
            bool shouldSweep = _round != null && (_round.SabotageEventCount > 0 || _round.EnvironmentEventCount > 0 || _round.CurrentPressureStage != NetworkRoundState.PressureStage.Early);
            if (!shouldSweep)
            {
                return null;
            }

            NetworkSecurityCameraStation[] stations = FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None);
            NetworkSecurityCameraStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkSecurityCameraStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            return bestStation;
        }

        private bool HandleAlarmTripwireLoop()
        {
            NetworkAlarmTripwire tripwire = ResolveBestAlarmTripwire();
            if (tripwire == null)
            {
                return false;
            }

            MoveToward(tripwire.transform.position);
            if (Vector3.Distance(transform.position, tripwire.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (tripwire.CanInteract(context))
            {
                tripwire.TryInteract(context);
            }

            return true;
        }

        private NetworkAlarmTripwire ResolveBestAlarmTripwire()
        {
            bool shouldArm = _round != null && (_round.SabotageEventCount > 0 || _round.CurrentPressureStage == NetworkRoundState.PressureStage.Late);
            if (!shouldArm)
            {
                return null;
            }

            NetworkAlarmTripwire[] tripwires = FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None);
            NetworkAlarmTripwire bestTripwire = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < tripwires.Length; i++)
            {
                NetworkAlarmTripwire tripwire = tripwires[i];
                if (tripwire == null || !tripwire.IsReady || tripwire.IsArmed)
                {
                    continue;
                }

                float distanceSqr = (tripwire.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTripwire = tripwire;
                }
            }

            return bestTripwire;
        }

        private bool HandleInkWellLoop()
        {
            NetworkInkWellStation station = ResolveBestInkWellStation();
            if (station == null)
            {
                return false;
            }

            MoveToward(station.transform.position);
            if (Vector3.Distance(transform.position, station.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (station.CanInteract(context))
            {
                station.TryInteract(context);
            }

            return true;
        }

        private NetworkInkWellStation ResolveBestInkWellStation()
        {
            float saturation = _flood != null ? _flood.Saturation01 : 0f;
            float stamina = _mover != null ? _mover.Stamina01 : 1f;
            bool needsRecovery = saturation >= 0.42f || stamina <= 0.24f;
            if (!needsRecovery)
            {
                return null;
            }

            NetworkInkWellStation[] stations = FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None);
            NetworkInkWellStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkInkWellStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            return bestStation;
        }

        private bool HandleSafeRoomLoop()
        {
            NetworkSafeRoomBeacon beacon = ResolveBestSafeRoomBeacon();
            if (beacon == null)
            {
                return false;
            }

            MoveToward(beacon.transform.position);
            if (Vector3.Distance(transform.position, beacon.transform.position) > interactDistance)
            {
                return true;
            }

            if (beacon.IsReady)
            {
                InteractionContext context = new(NetworkObject, OwnerClientId, true);
                if (beacon.CanInteract(context))
                {
                    beacon.TryInteract(context);
                }
            }

            return true;
        }

        private NetworkSafeRoomBeacon ResolveBestSafeRoomBeacon()
        {
            float saturation = _flood != null ? _flood.Saturation01 : 0f;
            float stamina = _mover != null ? _mover.Stamina01 : 1f;
            bool pressured = _round != null && _round.CurrentPressureStage == NetworkRoundState.PressureStage.Late;
            bool needsRefuge = saturation >= 0.48f || stamina <= 0.22f || pressured;
            if (!needsRefuge)
            {
                return null;
            }

            NetworkSafeRoomBeacon[] beacons = FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None);
            NetworkSafeRoomBeacon bestBeacon = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < beacons.Length; i++)
            {
                NetworkSafeRoomBeacon beacon = beacons[i];
                if (beacon == null || (!beacon.IsReady && !beacon.IsActive))
                {
                    continue;
                }

                float distanceSqr = (beacon.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestBeacon = beacon;
                }
            }

            return bestBeacon;
        }

        private bool HandlePaintScannerLoop()
        {
            NetworkPaintScannerStation scanner = ResolveBestPaintScannerStation();
            if (scanner == null)
            {
                return false;
            }

            MoveToward(scanner.transform.position);
            if (Vector3.Distance(transform.position, scanner.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (scanner.CanInteract(context))
            {
                scanner.TryInteract(context);
            }

            return true;
        }

        private NetworkPaintScannerStation ResolveBestPaintScannerStation()
        {
            bool shouldScan = _round != null && (_round.SabotageEventCount > 0 || _round.CurrentPressureStage != NetworkRoundState.PressureStage.Early);
            if (!shouldScan)
            {
                return null;
            }

            NetworkPaintScannerStation[] scanners = FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None);
            NetworkPaintScannerStation bestScanner = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < scanners.Length; i++)
            {
                NetworkPaintScannerStation scanner = scanners[i];
                if (scanner == null || !scanner.IsReady)
                {
                    continue;
                }

                float distanceSqr = (scanner.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestScanner = scanner;
                }
            }

            return bestScanner;
        }

        private bool HandleCrewRallyLoop()
        {
            NetworkCrewRallyStation station = ResolveBestCrewRallyStation();
            if (station == null)
            {
                return false;
            }

            MoveToward(station.transform.position);
            if (Vector3.Distance(transform.position, station.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (station.CanInteract(context))
            {
                station.TryInteract(context);
            }

            return true;
        }

        private NetworkCrewRallyStation ResolveBestCrewRallyStation()
        {
            float saturation = _flood != null ? _flood.Saturation01 : 0f;
            float stamina = _mover != null ? _mover.Stamina01 : 1f;
            bool shouldRally = saturation >= 0.28f || stamina <= 0.35f || (_round != null && _round.CurrentPressureStage == NetworkRoundState.PressureStage.Late);
            if (!shouldRally)
            {
                return null;
            }

            NetworkCrewRallyStation[] stations = FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None);
            NetworkCrewRallyStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;
            for (int i = 0; i < stations.Length; i++)
            {
                NetworkCrewRallyStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            return bestStation;
        }

        private bool HandleBulkheadLockLoop()
        {
            NetworkBulkheadLockStation station = ResolveBestBulkheadLockStation();
            if (station == null)
            {
                return false;
            }

            MoveToward(station.transform.position);
            if (Vector3.Distance(transform.position, station.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (station.CanInteract(context))
            {
                station.TryInteract(context);
            }

            return true;
        }

        private NetworkBulkheadLockStation ResolveBestBulkheadLockStation()
        {
            if (_round == null || _round.CurrentPressureStage == NetworkRoundState.PressureStage.Early)
            {
                return null;
            }

            NetworkBulkheadLockStation[] stations = FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None);
            NetworkBulkheadLockStation bestStation = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkBulkheadLockStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float score = Vector3.Distance(transform.position, station.transform.position);
                if (station.LinkedZone != null && station.LinkedZone.CurrentState is FloodZoneState.Flooding or FloodZoneState.Submerged)
                {
                    score -= 7f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestStation = station;
                }
            }

            return bestStation;
        }

        private bool HandleCalloutBeaconLoop()
        {
            if (_round == null || (_round.SabotageEventCount <= 0 && _round.EnvironmentEventCount <= 0))
            {
                return false;
            }

            NetworkCalloutBeacon[] beacons = FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None);
            NetworkCalloutBeacon bestBeacon = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < beacons.Length; i++)
            {
                NetworkCalloutBeacon beacon = beacons[i];
                if (beacon == null || !beacon.IsReady)
                {
                    continue;
                }

                float distanceSqr = (beacon.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestBeacon = beacon;
                }
            }

            if (bestBeacon == null || bestDistanceSqr > 169f)
            {
                return false;
            }

            MoveToward(bestBeacon.transform.position);
            if (Vector3.Distance(transform.position, bestBeacon.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (bestBeacon.CanInteract(context))
            {
                bestBeacon.TryInteract(context);
            }

            return true;
        }

        private bool HandleVitalsLoop()
        {
            NetworkVitalsStation station = ResolveBestVitalsStation();
            if (station == null)
            {
                return false;
            }

            MoveToward(station.transform.position);
            if (Vector3.Distance(transform.position, station.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (station.CanInteract(context))
            {
                station.TryInteract(context);
            }

            return true;
        }

        private NetworkVitalsStation ResolveBestVitalsStation()
        {
            bool shouldCheckVitals = _round != null && (_round.SabotageEventCount > 0 || _round.EnvironmentEventCount > 0 || _round.CurrentPressureStage == NetworkRoundState.PressureStage.Late);
            if (!shouldCheckVitals)
            {
                PlayerRemains[] remains = FindObjectsByType<PlayerRemains>(FindObjectsSortMode.None);
                for (int i = 0; i < remains.Length; i++)
                {
                    if (remains[i] != null && !remains[i].IsReported)
                    {
                        shouldCheckVitals = true;
                        break;
                    }
                }
            }

            if (!shouldCheckVitals)
            {
                return null;
            }

            NetworkVitalsStation[] stations = FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None);
            NetworkVitalsStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;
            for (int i = 0; i < stations.Length; i++)
            {
                NetworkVitalsStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            return bestStation;
        }

        private bool HandleBleachVentSealLoop()
        {
            NetworkBleachVent vent = ResolveBestOpenBleachVent();
            if (vent == null)
            {
                return false;
            }

            MoveToward(vent.transform.position);
            if (Vector3.Distance(transform.position, vent.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (vent.CanInteract(context))
            {
                vent.TryInteract(context);
            }

            return true;
        }

        private NetworkBleachVent ResolveBestOpenBleachVent()
        {
            if (_round == null || _round.SabotageEventCount <= 0 && _round.CurrentPressureStage != NetworkRoundState.PressureStage.Late)
            {
                return null;
            }

            NetworkBleachVent[] vents = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            NetworkBleachVent bestVent = null;
            float bestDistanceSqr = float.MaxValue;
            for (int i = 0; i < vents.Length; i++)
            {
                NetworkBleachVent vent = vents[i];
                if (vent == null || vent.IsSealed)
                {
                    continue;
                }

                float distanceSqr = (vent.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestVent = vent;
                }
            }

            return bestVent;
        }

        private bool TryUseBleachVentForTraversal()
        {
            NetworkBleachVent[] vents = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            NetworkBleachVent bestVent = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < vents.Length; i++)
            {
                NetworkBleachVent vent = vents[i];
                if (vent == null || !vent.IsReady)
                {
                    continue;
                }

                float distanceSqr = (vent.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestVent = vent;
                }
            }

            if (bestVent == null || bestDistanceSqr > 81f)
            {
                return false;
            }

            MoveToward(bestVent.transform.position);
            if (Vector3.Distance(transform.position, bestVent.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (bestVent.CanInteract(context))
            {
                bestVent.TryInteract(context);
            }

            return true;
        }

        private bool HandleDecontaminationLoop()
        {
            NetworkDecontaminationStation station = ResolveBestDecontaminationStation();
            if (station == null)
            {
                return false;
            }

            MoveToward(station.transform.position);
            if (Vector3.Distance(transform.position, station.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (station.CanInteract(context))
            {
                station.TryInteract(context);
            }

            return true;
        }

        private NetworkDecontaminationStation ResolveBestDecontaminationStation()
        {
            float saturation = _flood != null ? _flood.Saturation01 : 0f;
            float stamina = _mover != null ? _mover.Stamina01 : 1f;
            bool needsRecovery = saturation >= 0.34f || stamina <= 0.28f;
            if (!needsRecovery)
            {
                return null;
            }

            NetworkDecontaminationStation[] stations = FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None);
            NetworkDecontaminationStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkDecontaminationStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            return bestStation;
        }

        private bool HandleEmergencySealLoop()
        {
            NetworkEmergencySealStation bestStation = ResolveBestEmergencySealStation();
            if (bestStation == null)
            {
                return false;
            }

            MoveToward(bestStation.transform.position);
            if (Vector3.Distance(transform.position, bestStation.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (bestStation.CanInteract(context))
            {
                bestStation.TryInteract(context);
            }

            return true;
        }

        private NetworkEmergencySealStation ResolveBestEmergencySealStation()
        {
            NetworkEmergencySealStation[] stations = FindObjectsByType<NetworkEmergencySealStation>(FindObjectsSortMode.None);
            NetworkEmergencySealStation bestStation = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkEmergencySealStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float score = Vector3.Distance(transform.position, station.transform.position);

                NetworkBleachLeakHazard hazard = station.LinkedHazard;
                bool activeHazard = hazard != null && !hazard.IsSuppressed;
                if (activeHazard)
                {
                    score -= 8f;
                }

                FloodZone zone = station.TargetZone;
                bool dangerousZone = zone != null && zone.CurrentState is FloodZoneState.Flooding or FloodZoneState.Submerged;
                if (dangerousZone)
                {
                    score -= 5f;
                }

                bool highPressure = _round != null && _round.CurrentPressureStage == NetworkRoundState.PressureStage.Late;
                if (highPressure)
                {
                    score -= 3f;
                }

                if (!activeHazard && !dangerousZone && !highPressure)
                {
                    continue;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestStation = station;
                }
            }

            return bestStation;
        }

        private bool HandleFloodgateLoop()
        {
            NetworkFloodgateStation station = ResolveBestFloodgateStation();
            if (station == null)
            {
                return false;
            }

            MoveToward(station.transform.position);
            if (Vector3.Distance(transform.position, station.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (station.CanInteract(context))
            {
                station.TryInteract(context);
            }

            return true;
        }

        private NetworkFloodgateStation ResolveBestFloodgateStation()
        {
            NetworkFloodgateStation[] stations = FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None);
            NetworkFloodgateStation bestStation = null;
            float bestScore = float.MaxValue;
            bool highPressure = _round != null && _round.CurrentPressureStage != NetworkRoundState.PressureStage.Early;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkFloodgateStation station = stations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float score = Vector3.Distance(transform.position, station.transform.position);
                if (highPressure)
                {
                    score -= 4f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestStation = station;
                }
            }

            return highPressure ? bestStation : null;
        }

        private bool HandleAdvancedTaskLoop()
        {
            RefreshAdvancedTaskSelectionIfNeeded();
            if (_currentAdvancedStep == null || _currentAdvancedStep.OwnerTask == null)
            {
                return false;
            }

            TaskObjectiveBase ownerTask = _currentAdvancedStep.OwnerTask;
            if (ownerTask.IsCompleted || ownerTask.IsLocked)
            {
                _currentAdvancedStep = null;
                return false;
            }

            MoveToward(_currentAdvancedStep.transform.position);
            if (Vector3.Distance(transform.position, _currentAdvancedStep.transform.position) > interactDistance)
            {
                return true;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            if (_currentAdvancedStep.CanInteract(context))
            {
                _currentAdvancedStep.TryInteract(context);
            }

            _currentAdvancedStep = null;
            _nextAdvancedTaskRefreshTime = Time.time + 0.15f;
            return true;
        }

        private void RefreshAdvancedTaskSelectionIfNeeded()
        {
            if (Time.time < _nextAdvancedTaskRefreshTime && IsAdvancedStepStillUseful(_currentAdvancedStep))
            {
                return;
            }

            _nextAdvancedTaskRefreshTime = Time.time + taskRefreshIntervalSeconds;
            _currentAdvancedStep = ResolveBestAdvancedTaskStep();
        }

        private bool IsAdvancedStepStillUseful(TaskStepInteractable step)
        {
            if (step == null || step.OwnerTask == null || step.OwnerTask.IsCompleted || step.OwnerTask.IsLocked)
            {
                return false;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, true);
            return step.CanInteract(context) && IsPromptActionable(step.GetPromptText(context));
        }

        private TaskStepInteractable ResolveBestAdvancedTaskStep()
        {
            TaskStepInteractable[] steps = FindObjectsByType<TaskStepInteractable>(FindObjectsSortMode.None);
            TaskStepInteractable bestStep = null;
            float bestScore = float.MaxValue;
            InteractionContext context = new(NetworkObject, OwnerClientId, true);

            for (int i = 0; i < steps.Length; i++)
            {
                TaskStepInteractable step = steps[i];
                if (step == null || step.OwnerTask == null || step.OwnerTask.IsCompleted || step.OwnerTask.IsLocked)
                {
                    continue;
                }

                if (!step.CanInteract(context))
                {
                    continue;
                }

                string prompt = step.GetPromptText(context);
                if (!IsPromptActionable(prompt))
                {
                    continue;
                }

                float score = Vector3.Distance(transform.position, step.transform.position);
                if (step.OwnerTask.CurrentState == RepairTaskState.InProgress && step.OwnerTask.ActiveClientId == OwnerClientId)
                {
                    score -= 6f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestStep = step;
                }
            }

            return bestStep;
        }

        private static bool IsPromptActionable(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return false;
            }

            return prompt.IndexOf("wrong", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   prompt.IndexOf("out of order", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   prompt.IndexOf("bring", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   prompt.IndexOf("already", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   prompt.IndexOf("in use", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   prompt.IndexOf("wait", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   prompt.IndexOf("locked", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   prompt.IndexOf("completed", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        private bool HandleTaskLoop()
        {
            RefreshTaskSelectionIfNeeded();
            if (_currentTask == null)
            {
                return false;
            }

            if (_currentTask.IsCompleted || _currentTask.CurrentState == RepairTaskState.Locked)
            {
                ResetTaskState();
                return false;
            }

            if (_currentTask.CurrentState == RepairTaskState.InProgress && _currentTask.ActiveClientId != OwnerClientId)
            {
                ResetTaskState();
                return false;
            }

            if (_currentTask.CurrentState is RepairTaskState.Idle or RepairTaskState.Cancelled or RepairTaskState.FailedAttempt)
            {
                MoveToward(_currentTask.transform.position);
                if (Vector3.Distance(transform.position, _currentTask.transform.position) > interactDistance)
                {
                    return true;
                }

                InteractionContext context = new(NetworkObject, OwnerClientId, true);
                if (_currentTask.CanStart(context) && _currentTask.TryBeginTask(context))
                {
                    BeginTaskTracking(_currentTask);
                }

                return true;
            }

            if (_currentTask.CurrentState == RepairTaskState.InProgress && _currentTask.ActiveClientId == OwnerClientId)
            {
                return DriveActiveTask(_currentTask);
            }

            return false;
        }

        private bool DriveActiveTask(NetworkRepairTask task)
        {
            Vector3 planarDelta = task.transform.position - transform.position;
            planarDelta.y = 0f;
            if (planarDelta.magnitude > taskHoldPositionDistance)
            {
                MoveToward(task.transform.position);
            }
            else
            {
                float yaw = planarDelta.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(planarDelta.normalized, Vector3.up).eulerAngles.y
                    : transform.eulerAngles.y;
                _mover.ServerSetExternalInput(Vector3.zero, yaw, false, false, false, 0.2f);
            }

            if (!task.IsTaskEnvironmentSafe())
            {
                task.TryCancelTask("CPU abandoned unsafe task");
                ResetTaskState();
                return false;
            }

            float progress = task.GetCurrentProgress01();
            if (task is PumpRepairTask pumpTask)
            {
                float commitMid = (pumpTask.ConfirmationWindowStartNormalized + pumpTask.ConfirmationWindowEndNormalized) * 0.5f;
                if (progress >= commitMid)
                {
                    float elapsedSeconds = pumpTask.TaskDurationSeconds * progress;
                    pumpTask.ServerTryResolveConfirmation(elapsedSeconds, OwnerClientId);
                    if (pumpTask.CurrentState != RepairTaskState.InProgress)
                    {
                        ResetTaskState();
                    }
                }

                return true;
            }

            if (task is ShipRepairTask shipRepairTask)
            {
                EnsureShipTaskConfig(shipRepairTask);
                if (_shipCheckpointIndex < _shipCheckpointFractions.Count)
                {
                    float target = _shipCheckpointFractions[_shipCheckpointIndex];
                    float pressTime = Mathf.Max(0f, target - _shipCheckpointWindow + shipCheckpointBias);
                    float failTime = target + _shipCheckpointWindow;
                    if (progress >= pressTime)
                    {
                        _shipCheckpointIndex++;
                    }
                    else if (progress > failTime)
                    {
                        task.TryCancelTask("CPU mistimed ship repair");
                        ResetTaskState();
                        return false;
                    }
                }

                if (progress >= 1f)
                {
                    if (_shipCheckpointIndex >= _shipCheckpointFractions.Count)
                    {
                        task.TryCompleteTask();
                    }
                    else
                    {
                        task.TryCancelTask("CPU ended ship repair early");
                    }

                    ResetTaskState();
                }

                return true;
            }

            if (progress >= 1f)
            {
                task.TryCompleteTask();
                ResetTaskState();
            }

            return true;
        }

        private void RefreshTaskSelectionIfNeeded()
        {
            bool requiresRefresh = Time.time >= _nextTaskRefreshTime || !IsTaskStillUseful(_currentTask);
            if (!requiresRefresh)
            {
                return;
            }

            _nextTaskRefreshTime = Time.time + taskRefreshIntervalSeconds;
            _currentTask = ResolveBestTask();
            if (_currentTask == null)
            {
                ResetTaskState();
                return;
            }

            if (_trackedTaskId != _currentTask.NetworkObjectId)
            {
                BeginTaskTracking(_currentTask);
            }
        }

        private NetworkRepairTask ResolveBestTask()
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            NetworkRepairTask bestTask = null;
            float bestScore = float.MaxValue;
            InteractionContext context = new(NetworkObject, OwnerClientId, true);

            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null || task.IsCompleted || task.CurrentState == RepairTaskState.Locked)
                {
                    continue;
                }

                if (task.CurrentState == RepairTaskState.InProgress && task.ActiveClientId != OwnerClientId)
                {
                    continue;
                }

                if (task.CurrentState is RepairTaskState.Idle or RepairTaskState.Cancelled or RepairTaskState.FailedAttempt)
                {
                    if (!task.CanStart(context))
                    {
                        continue;
                    }
                }

                float score = Vector3.Distance(transform.position, task.transform.position);
                if (task == _pump)
                {
                    score -= 4f;
                }

                if (task.CurrentState == RepairTaskState.InProgress && task.ActiveClientId == OwnerClientId)
                {
                    score -= 8f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTask = task;
                }
            }

            return bestTask;
        }

        private bool IsTaskStillUseful(NetworkRepairTask task)
        {
            if (task == null || task.IsCompleted || task.CurrentState == RepairTaskState.Locked)
            {
                return false;
            }

            if (task.CurrentState == RepairTaskState.InProgress && task.ActiveClientId != OwnerClientId)
            {
                return false;
            }

            return true;
        }

        private void BeginTaskTracking(NetworkRepairTask task)
        {
            _trackedTaskId = task != null ? task.NetworkObjectId : ulong.MaxValue;
            _shipCheckpointIndex = 0;
            _shipCheckpointWindow = 0f;
            _shipCheckpointFractions.Clear();
        }

        private void EnsureShipTaskConfig(ShipRepairTask shipRepairTask)
        {
            if (_trackedTaskId != shipRepairTask.NetworkObjectId || _shipCheckpointFractions.Count == 0)
            {
                BeginTaskTracking(shipRepairTask);
                switch (shipRepairTask.Difficulty)
                {
                    case ShipRepairTask.DifficultyTier.Easy:
                        _shipCheckpointWindow = 0.15f;
                        _shipCheckpointFractions.Add(0.55f);
                        break;

                    case ShipRepairTask.DifficultyTier.Medium:
                        _shipCheckpointWindow = 0.12f;
                        _shipCheckpointFractions.Add(0.35f);
                        _shipCheckpointFractions.Add(0.72f);
                        break;

                    default:
                        _shipCheckpointWindow = 0.10f;
                        _shipCheckpointFractions.Add(0.22f);
                        _shipCheckpointFractions.Add(0.48f);
                        _shipCheckpointFractions.Add(0.78f);
                        break;
                }
            }
        }

        private bool TryMoveTowardColorTarget(out PlayerLifeState target)
        {
            target = null;
            PlayerLifeState[] lifeStates = FindObjectsByType<PlayerLifeState>(FindObjectsSortMode.None);
            float bestDist = float.MaxValue;

            for (int i = 0; i < lifeStates.Length; i++)
            {
                PlayerLifeState life = lifeStates[i];
                if (life == null || !life.IsAlive || life.gameObject == gameObject)
                {
                    continue;
                }

                if (!life.TryGetComponent(out PlayerKillInputController targetKill) || targetKill.CurrentRole != PlayerRole.Color)
                {
                    continue;
                }

                float dist = Vector3.Distance(transform.position, life.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    target = life;
                }
            }

            if (target == null)
            {
                return false;
            }

            MoveToward(target.transform.position);
            return true;
        }

        private bool ShouldEvadeFlood()
        {
            return _flood != null && _flood.CurrentZoneState is FloodZoneState.Flooding or FloodZoneState.Submerged;
        }

        private Vector3 ResolveFloodEvadeTarget()
        {
            Vector3 away = Vector3.zero;
            if (_currentTask != null)
            {
                away = transform.position - _currentTask.transform.position;
            }
            else if (_pump != null)
            {
                away = transform.position - _pump.transform.position;
            }

            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
            {
                away = Random.insideUnitSphere;
                away.y = 0f;
            }

            return transform.position + away.normalized * floodEvadeBiasDistance;
        }

        private void HandleWander()
        {
            if (Time.time >= _nextRepathTime || Vector3.Distance(transform.position, _moveTarget) < 1.1f)
            {
                _nextRepathTime = Time.time + repathIntervalSeconds;
                Vector2 ring = Random.insideUnitCircle * wanderRadius;
                _moveTarget = ClampToArena(transform.position + new Vector3(ring.x, 0f, ring.y));
            }

            MoveToward(_moveTarget);
        }

        private void MoveToward(Vector3 target)
        {
            target = ClampToArena(target);
            Vector3 planarDelta = target - transform.position;
            planarDelta.y = 0f;

            if (planarDelta.sqrMagnitude < 0.15f)
            {
                _mover.ServerSetExternalInput(Vector3.zero, transform.eulerAngles.y, false, false, false, 0.2f);
                return;
            }

            Vector3 direction = ResolveSteeringDirection(planarDelta.normalized, target);
            float yaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
            bool shouldJump = Time.time >= _nextJumpTime;
            if (shouldJump)
            {
                _nextJumpTime = Time.time + Random.Range(jumpIntervalMin, jumpIntervalMax);
            }

            _mover.ServerSetExternalInput(direction, yaw, shouldJump, false, false, 0.25f);
        }

        private Vector3 ResolveSteeringDirection(Vector3 desiredDirection, Vector3 target)
        {
            TrackStuckState();

            Vector3 origin = transform.position + (Vector3.up * 0.75f);
            bool blocked = Physics.SphereCast(origin, 0.32f, desiredDirection, out RaycastHit hit, 1.25f, ~0, QueryTriggerInteraction.Ignore) && !IsOwnCollider(hit.collider);

            if (!blocked && _stuckSeconds < 0.75f)
            {
                return desiredDirection;
            }

            if (Time.time >= _nextAvoidanceRefreshTime || _avoidanceDirection.sqrMagnitude < 0.01f)
            {
                _nextAvoidanceRefreshTime = Time.time + 0.45f;
                Vector3 left = Vector3.Cross(Vector3.up, desiredDirection).normalized;
                Vector3 right = -left;
                float leftScore = ScoreAvoidanceDirection(left, target);
                float rightScore = ScoreAvoidanceDirection(right, target);
                _avoidanceDirection = leftScore >= rightScore ? left : right;
            }

            Vector3 blended = Vector3.Lerp(desiredDirection, _avoidanceDirection, blocked ? 0.72f : 0.55f);
            blended.y = 0f;
            return blended.sqrMagnitude > 0.001f ? blended.normalized : desiredDirection;
        }

        private float ScoreAvoidanceDirection(Vector3 direction, Vector3 target)
        {
            Vector3 origin = transform.position + (Vector3.up * 0.75f);
            bool blocked = Physics.SphereCast(origin, 0.32f, direction, out RaycastHit hit, 1.7f, ~0, QueryTriggerInteraction.Ignore) && !IsOwnCollider(hit.collider);
            Vector3 targetDelta = target - transform.position;
            targetDelta.y = 0f;
            float targetAlignment = targetDelta.sqrMagnitude > 0.001f ? Vector3.Dot(direction, targetDelta.normalized) : 0f;
            return (blocked ? -4f : 2f) + targetAlignment;
        }

        private void TrackStuckState()
        {
            Vector3 current = transform.position;
            Vector3 previous = _lastPathSamplePosition;
            current.y = 0f;
            previous.y = 0f;
            float moved = Vector3.Distance(current, previous);
            if (moved < 0.035f)
            {
                _stuckSeconds += Time.deltaTime;
            }
            else
            {
                _stuckSeconds = Mathf.Max(0f, _stuckSeconds - (Time.deltaTime * 2f));
                _lastPathSamplePosition = transform.position;
            }
        }

        private bool IsOwnCollider(Collider colliderRef)
        {
            return colliderRef != null && colliderRef.transform.IsChildOf(transform);
        }

        private static Vector3 ClampToArena(Vector3 point)
        {
            point.x = Mathf.Clamp(point.x, -21.5f, 21.5f);
            point.z = Mathf.Clamp(point.z, -14.8f, 14.8f);
            return point;
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

        private void ResetTaskState()
        {
            _currentTask = null;
            _currentAdvancedStep = null;
            _trackedTaskId = ulong.MaxValue;
            _shipCheckpointIndex = 0;
            _shipCheckpointWindow = 0f;
            _shipCheckpointFractions.Clear();
        }

        private void ResolveReferences()
        {
            _round ??= FindFirstObjectByType<NetworkRoundState>();
            _pump ??= FindFirstObjectByType<PumpRepairTask>();
        }
    }
}
