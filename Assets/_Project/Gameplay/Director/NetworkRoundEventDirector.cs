// File: Assets/_Project/Gameplay/Director/NetworkRoundEventDirector.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Director
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRoundEventDirector : NetworkBehaviour
    {
        [Header("Scheduling")]
        [SerializeField, Min(5f)] private float firstEventDelaySeconds = 38f;
        [SerializeField, Min(5f)] private float earlyIntervalSeconds = 78f;
        [SerializeField, Min(5f)] private float midIntervalSeconds = 58f;
        [SerializeField, Min(5f)] private float lateIntervalSeconds = 42f;
        [SerializeField, Min(0f)] private float intervalJitterSeconds = 10f;

        [Header("Pressure")]
        [SerializeField, Min(0f)] private float environmentalTimePenaltySeconds = 6f;
        [SerializeField, Min(0f)] private float reliefTimeBonusSeconds = 5f;

        private readonly NetworkVariable<float> _nextEventServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _eventCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> _currentEventLabel =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetworkRoundState _roundState;

        public int EventCount => _eventCount.Value;
        public float SecondsUntilNextEvent => Mathf.Max(0f, _nextEventServerTime.Value - GetServerTime());
        public string CurrentEventLabel => _currentEventLabel.Value.ToString();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                ResetSchedule();
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            ResolveRoundState();
            if (_roundState == null)
            {
                return;
            }

            if (!_roundState.IsFreeRoam)
            {
                if (_nextEventServerTime.Value <= 0f)
                {
                    ResetSchedule();
                }

                return;
            }

            if (_nextEventServerTime.Value <= 0f)
            {
                ScheduleNextEvent(firstEventDelaySeconds);
                return;
            }

            if (GetServerTime() < _nextEventServerTime.Value)
            {
                return;
            }

            TriggerDirectorEvent();
            ScheduleNextEvent(GetCurrentIntervalSeconds());
        }

        public void ServerResetForRound()
        {
            if (!IsServer)
            {
                return;
            }

            _eventCount.Value = 0;
            _currentEventLabel.Value = default;
            ResetSchedule();
        }

        private void TriggerDirectorEvent()
        {
            if (_roundState == null || !_roundState.IsFreeRoam)
            {
                return;
            }

            int choice = Mathf.Abs((int)((GetServerTime() * 1000f) + _eventCount.Value * 37f)) % 5;
            bool success = choice switch
            {
                0 => TriggerFloodBulkheadRattle(),
                1 => TriggerBleachReflux(),
                2 => TriggerEmergencyDrainWindow(),
                3 => TriggerFloodgateJam(),
                _ => TriggerTaskNoiseBurst()
            };

            if (!success)
            {
                success = TriggerFloodBulkheadRattle() || TriggerBleachReflux() || TriggerEmergencyDrainWindow() || TriggerFloodgateJam() || TriggerTaskNoiseBurst();
            }

            if (success)
            {
                _eventCount.Value = Mathf.Max(0, _eventCount.Value + 1);
            }
        }

        private bool TriggerFloodBulkheadRattle()
        {
            FloodSequenceController floodController = FindFirstObjectByType<FloodSequenceController>();
            floodController?.ServerTriggerReportAftershock();

            FloodZone zone = ResolveMostRelevantFloodZone(false);
            if (zone != null && zone.CurrentState != FloodZoneState.SealedSafe)
            {
                zone.TrySetState(zone.CurrentState == FloodZoneState.Dry ? FloodZoneState.Wet : FloodZoneState.Flooding);
            }

            SetEvent("Bulkhead rattle");
            _roundState.ServerAnnounceEnvironmentalEvent("Bulkhead rattle", "A pressure seam buckled. Flood route changed.", environmentalTimePenaltySeconds);
            return true;
        }

        private bool TriggerBleachReflux()
        {
            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            NetworkBleachLeakHazard selected = null;
            for (int i = 0; i < hazards.Length; i++)
            {
                NetworkBleachLeakHazard hazard = hazards[i];
                if (hazard == null)
                {
                    continue;
                }

                if (selected == null || hazard.IsSuppressed)
                {
                    selected = hazard;
                }
            }

            if (selected == null)
            {
                return false;
            }

            selected.ServerReactivate("Director reflux reopened " + selected.DisplayName);
            SetEvent("Bleach reflux");
            _roundState.ServerAnnounceEnvironmentalEvent("Bleach reflux", selected.DisplayName + " reactivated. Use a seal station or decontamination route.", environmentalTimePenaltySeconds);
            return true;
        }

        private bool TriggerEmergencyDrainWindow()
        {
            FloodZone zone = ResolveMostRelevantFloodZone(true);
            if (zone != null)
            {
                zone.TrySetState(FloodZoneState.SealedSafe);
            }

            NetworkBleachLeakHazard hazard = ResolveActiveHazard();
            hazard?.ServerSuppressFor(18f, "Emergency scrubber dampened " + hazard.DisplayName);

            SetEvent("Drain window");
            _roundState.ServerApplyCrewStabilization(ulong.MaxValue, "Automatic drain window", reliefTimeBonusSeconds);
            _roundState.ServerAnnounceEnvironmentalEvent("Drain window", "Ship scrubbers opened a short safe route. Push objectives now.", 0f);
            return zone != null || hazard != null;
        }

        private bool TriggerFloodgateJam()
        {
            NetworkFloodgateStation[] stations = FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None);
            NetworkFloodgateStation selected = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkFloodgateStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float score = station.IsReady ? 20f : -station.CooldownRemaining;
                if (score > bestScore)
                {
                    bestScore = score;
                    selected = station;
                }
            }

            if (selected == null)
            {
                return false;
            }

            selected.ServerJam(14f, "Pressure inversion jammed " + selected.DisplayName);
            SetEvent("Floodgate inversion");
            _roundState.ServerAnnounceEnvironmentalEvent("Floodgate inversion", selected.DisplayName + " jammed. Route pressure shifted.", environmentalTimePenaltySeconds * 0.5f);
            return true;
        }

        private bool TriggerTaskNoiseBurst()
        {
            TaskObjectiveBase[] tasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            TaskObjectiveBase target = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < tasks.Length; i++)
            {
                TaskObjectiveBase task = tasks[i];
                if (task == null || task.IsCompleted || task.IsLocked)
                {
                    continue;
                }

                float score = task.CurrentState == RepairTaskState.InProgress ? -10f : i;
                if (score < bestScore)
                {
                    bestScore = score;
                    target = task;
                }
            }

            if (target == null)
            {
                return false;
            }

            if (target.CurrentState == RepairTaskState.InProgress)
            {
                target.ServerReleaseActiveOperator("Environmental noise interrupted calibration.");
            }

            SetEvent("Signal noise");
            _roundState.ServerAnnounceEnvironmentalEvent("Signal noise", target.DisplayName + " needs fresh confirmation.", 0f);
            return true;
        }

        private FloodZone ResolveMostRelevantFloodZone(bool preferDangerous)
        {
            FloodZone[] zones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            FloodZone best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < zones.Length; i++)
            {
                FloodZone zone = zones[i];
                if (zone == null)
                {
                    continue;
                }

                int score = zone.CurrentState switch
                {
                    FloodZoneState.Submerged => preferDangerous ? 100 : 10,
                    FloodZoneState.Flooding => preferDangerous ? 80 : 30,
                    FloodZoneState.Wet => preferDangerous ? 40 : 60,
                    FloodZoneState.Dry => preferDangerous ? 10 : 90,
                    FloodZoneState.SealedSafe => preferDangerous ? 60 : -50,
                    _ => 0
                };

                if (score > bestScore)
                {
                    bestScore = score;
                    best = zone;
                }
            }

            return best;
        }

        private NetworkBleachLeakHazard ResolveActiveHazard()
        {
            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null && !hazards[i].IsSuppressed)
                {
                    return hazards[i];
                }
            }

            return null;
        }

        private void SetEvent(string label)
        {
            _currentEventLabel.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString64(string.IsNullOrWhiteSpace(label) ? "Director event" : label);
        }

        private void ResolveRoundState()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }
        }

        private void ResetSchedule()
        {
            ScheduleNextEvent(firstEventDelaySeconds);
        }

        private void ScheduleNextEvent(float baseDelay)
        {
            float jitter = intervalJitterSeconds <= 0f ? 0f : Random.Range(-intervalJitterSeconds, intervalJitterSeconds);
            _nextEventServerTime.Value = GetServerTime() + Mathf.Max(5f, baseDelay + jitter);
        }

        private float GetCurrentIntervalSeconds()
        {
            ResolveRoundState();
            if (_roundState == null)
            {
                return earlyIntervalSeconds;
            }

            return _roundState.CurrentPressureStage switch
            {
                NetworkRoundState.PressureStage.Late => lateIntervalSeconds,
                NetworkRoundState.PressureStage.Mid => midIntervalSeconds,
                _ => earlyIntervalSeconds
            };
        }

        private float GetServerTime()
        {
            return NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.unscaledTime;
        }
    }
}
