// File: Assets/_Project/Flood/FloodSequenceController.cs
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HueDoneIt.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Flood
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FloodSequenceController : NetworkBehaviour
    {
        public enum RoundPressureStage : byte
        {
            Early = 0,
            Mid = 1,
            Late = 2
        }

        [System.Serializable]
        public struct StateDuration
        {
            public FloodZoneState state;
            [Min(0.1f)] public float durationSeconds;
        }

        [System.Serializable]
        public sealed class ZoneSequence
        {
            public FloodZone zone;
            [Min(0f)] public float initialDelaySeconds;
            public bool loop;
            public List<StateDuration> states = new();
        }

        [Header("Baseline Room Loops")]
        [SerializeField] private List<ZoneSequence> sequences = new();

        [Header("Pressure Timing")]
        [SerializeField, Min(0.1f)] private float earlySpeedMultiplier = 0.8f;
        [SerializeField, Min(0.1f)] private float midSpeedMultiplier = 1f;
        [SerializeField, Min(0.1f)] private float lateSpeedMultiplier = 1.35f;
        [SerializeField, Min(2f)] private float earlyPulseCadenceSeconds = 28f;
        [SerializeField, Min(2f)] private float midPulseCadenceSeconds = 18f;
        [SerializeField, Min(2f)] private float latePulseCadenceSeconds = 11f;
        [SerializeField, Min(0.5f)] private float pulseTelegraphSeconds = 4f;
        [SerializeField, Min(0.5f)] private float pulseFloodDurationSeconds = 5.5f;
        [SerializeField, Min(0.5f)] private float pulseSubmergeDurationSeconds = 3.5f;
        [SerializeField, Min(0f)] private float reportAftershockDelaySeconds = 2f;

        [Header("Lock Escalation")]
        [SerializeField, Min(0.1f)] private float lockedFloodingDelaySeconds = 4f;
        [SerializeField, Min(0.1f)] private float lockedSubmergeDelaySeconds = 6f;

        private readonly NetworkVariable<byte> _pressureStage =
            new((byte)RoundPressureStage.Early, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _nextPulseServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _activePulseEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _pulseZoneId =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly List<Coroutine> _running = new();
        private Coroutine _lockedEscalationCoroutine;
        private Coroutine _pulseRoutine;
        private bool _completionApplied;
        private bool _lockEscalationStarted;
        private bool _flowActive;
        private int _nextPulseZoneIndex;

        public RoundPressureStage CurrentPressureStage => (RoundPressureStage)_pressureStage.Value;
        public bool IsPulseTelegraphActive => _nextPulseServerTime.Value > GetServerTime() && !string.IsNullOrEmpty(_pulseZoneId.Value.ToString());
        public bool IsPulseActive => _activePulseEndServerTime.Value > GetServerTime();
        public string PulseZoneId => _pulseZoneId.Value.ToString();
        public float SecondsUntilPulse => Mathf.Max(0f, _nextPulseServerTime.Value - GetServerTime());
        public float PulseSecondsRemaining => Mathf.Max(0f, _activePulseEndServerTime.Value - GetServerTime());

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                ServerResetForRound();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                StopSequences();
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsServer || !_flowActive)
            {
                return;
            }

            PumpRepairTask pumpRepairTask = FindFirstObjectByType<PumpRepairTask>();
            if (pumpRepairTask == null)
            {
                return;
            }

            if (pumpRepairTask.IsCompleted && !_completionApplied)
            {
                _completionApplied = true;
                StopSequences();
                SetAllZones(FloodZoneState.SealedSafe);
                return;
            }

            if (pumpRepairTask.IsLocked && !_lockEscalationStarted)
            {
                _lockEscalationStarted = true;
                StopSequences();
                _lockedEscalationCoroutine = StartCoroutine(RunLockedEscalation());
            }
        }

        public void ServerResetForRound()
        {
            if (!IsServer)
            {
                return;
            }

            StopSequences();
            EnsureDefaultSequencesIfNeeded();
            ResetZonesToInitialState();
            _completionApplied = false;
            _lockEscalationStarted = false;
            _flowActive = false;
            _nextPulseZoneIndex = 0;
            _pressureStage.Value = (byte)RoundPressureStage.Early;
            _nextPulseServerTime.Value = 0f;
            _activePulseEndServerTime.Value = 0f;
            _pulseZoneId.Value = default;
        }

        public void ServerSetFlowActive(bool isActive)
        {
            if (!IsServer || _flowActive == isActive)
            {
                return;
            }

            _flowActive = isActive;
            if (_flowActive)
            {
                StartSequences();
                StartPulseRoutine();
                return;
            }

            StopSequences();
        }

        public void ServerSetPressureStage(RoundPressureStage stage)
        {
            if (!IsServer)
            {
                return;
            }

            _pressureStage.Value = (byte)stage;
            if (_flowActive)
            {
                StartPulseRoutine();
            }
        }

        public void ServerTriggerReportAftershock()
        {
            if (!IsServer || !_flowActive)
            {
                return;
            }

            StartPulseRoutine(reportAftershockDelaySeconds);
        }

        public string BuildRoundPressureHint()
        {
            if (IsPulseActive)
            {
                return $"Pulse active in {PulseZoneId} ({Mathf.CeilToInt(PulseSecondsRemaining)}s)";
            }

            if (IsPulseTelegraphActive)
            {
                return $"Surge incoming: {PulseZoneId} in {Mathf.CeilToInt(SecondsUntilPulse)}s";
            }

            return CurrentPressureStage switch
            {
                RoundPressureStage.Early => "Flood probing routes",
                RoundPressureStage.Mid => "Flood closing fast paths",
                _ => "Flood end pressure rising"
            };
        }

        private void EnsureDefaultSequencesIfNeeded()
        {
            if (sequences != null && sequences.Count > 0)
            {
                return;
            }

            FloodZone mainZone = FindZoneByName("FloodZone_Main") ?? FindZoneByName("FloodZone_Bilge");
            FloodZone lowZone = FindZoneByName("FloodZone_LowArea") ?? FindZoneByName("FloodZone_Bridge");

            if (mainZone == null && lowZone == null)
            {
                return;
            }

            sequences = new List<ZoneSequence>();
            if (mainZone != null)
            {
                ApplyZoneInitialState(mainZone, FloodZoneState.Dry);
                sequences.Add(new ZoneSequence
                {
                    zone = mainZone,
                    initialDelaySeconds = 18f,
                    loop = true,
                    states = new List<StateDuration>
                    {
                        // Start the round with a real dry grace period so players never spawn inside active water.
                        new() { state = FloodZoneState.Dry, durationSeconds = 12f },
                        new() { state = FloodZoneState.Wet, durationSeconds = 16f },
                        new() { state = FloodZoneState.Flooding, durationSeconds = 12f },
                        new() { state = FloodZoneState.Wet, durationSeconds = 14f }
                    }
                });
            }

            if (lowZone != null)
            {
                ApplyZoneInitialState(lowZone, FloodZoneState.Dry);
                sequences.Add(new ZoneSequence
                {
                    zone = lowZone,
                    initialDelaySeconds = 26f,
                    loop = true,
                    states = new List<StateDuration>
                    {
                        // Bilge/low-area flooding should be a pressure event, not the spawn state.
                        new() { state = FloodZoneState.Dry, durationSeconds = 10f },
                        new() { state = FloodZoneState.Wet, durationSeconds = 10f },
                        new() { state = FloodZoneState.Flooding, durationSeconds = 10f },
                        new() { state = FloodZoneState.Wet, durationSeconds = 12f }
                    }
                });
            }
        }

        private static FloodZone FindZoneByName(string name)
        {
            FloodZone[] zones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            foreach (FloodZone zone in zones)
            {
                if (zone != null && zone.gameObject.name == name)
                {
                    return zone;
                }
            }

            return null;
        }

        private static void ApplyZoneInitialState(FloodZone zone, FloodZoneState state)
        {
            FieldInfo field = typeof(FloodZone).GetField("initialState", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(zone, state);
        }

        private void StartSequences()
        {
            StopSequences();
            foreach (ZoneSequence sequence in sequences)
            {
                if (sequence?.zone == null || sequence.states == null || sequence.states.Count == 0)
                {
                    continue;
                }

                _running.Add(StartCoroutine(RunSequence(sequence)));
            }
        }

        private void StartPulseRoutine(float initialDelayOverride = -1f)
        {
            if (_pulseRoutine != null)
            {
                StopCoroutine(_pulseRoutine);
            }

            _pulseRoutine = StartCoroutine(RunPulseCycle(initialDelayOverride));
        }

        private void StopSequences()
        {
            foreach (Coroutine coroutineRef in _running)
            {
                if (coroutineRef != null)
                {
                    StopCoroutine(coroutineRef);
                }
            }

            _running.Clear();

            if (_lockedEscalationCoroutine != null)
            {
                StopCoroutine(_lockedEscalationCoroutine);
                _lockedEscalationCoroutine = null;
            }

            if (_pulseRoutine != null)
            {
                StopCoroutine(_pulseRoutine);
                _pulseRoutine = null;
            }

            _nextPulseServerTime.Value = 0f;
            _activePulseEndServerTime.Value = 0f;
            _pulseZoneId.Value = default;
        }

        private IEnumerator RunSequence(ZoneSequence sequence)
        {
            if (sequence.initialDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(sequence.initialDelaySeconds);
            }

            do
            {
                foreach (StateDuration step in sequence.states)
                {
                    sequence.zone.TrySetState(step.state);
                    float duration = Mathf.Max(0.1f, step.durationSeconds / Mathf.Max(0.1f, GetStageSpeedMultiplier()));
                    yield return new WaitForSeconds(duration);
                }
            }
            while (sequence.loop && _flowActive);
        }

        private IEnumerator RunPulseCycle(float initialDelayOverride)
        {
            float initialDelay = initialDelayOverride >= 0f ? initialDelayOverride : GetPulseCadenceSeconds();
            if (initialDelay > 0f)
            {
                _nextPulseServerTime.Value = GetServerTime() + initialDelay;
                _activePulseEndServerTime.Value = 0f;
                yield return new WaitForSeconds(initialDelay);
            }

            while (_flowActive)
            {
                FloodZone pulseZone = ResolveNextPulseZone();
                if (pulseZone == null)
                {
                    yield return new WaitForSeconds(2f);
                    continue;
                }

                _pulseZoneId.Value = new FixedString64Bytes(string.IsNullOrWhiteSpace(pulseZone.ZoneId) ? pulseZone.gameObject.name : pulseZone.ZoneId);
                _nextPulseServerTime.Value = GetServerTime() + pulseTelegraphSeconds;
                _activePulseEndServerTime.Value = 0f;

                yield return new WaitForSeconds(pulseTelegraphSeconds);
                FloodZoneState previous = pulseZone.CurrentState;

                pulseZone.TrySetState(FloodZoneState.Flooding);
                _activePulseEndServerTime.Value = GetServerTime() + pulseFloodDurationSeconds + pulseSubmergeDurationSeconds;
                yield return new WaitForSeconds(pulseFloodDurationSeconds);

                pulseZone.TrySetState(FloodZoneState.Submerged);
                yield return new WaitForSeconds(pulseSubmergeDurationSeconds);

                if (_flowActive)
                {
                    pulseZone.TrySetState(previous is FloodZoneState.Submerged ? FloodZoneState.Flooding : previous);
                }

                _activePulseEndServerTime.Value = 0f;
                float cadence = GetPulseCadenceSeconds();
                _nextPulseServerTime.Value = GetServerTime() + cadence;
                yield return new WaitForSeconds(cadence);
            }
        }

        private IEnumerator RunLockedEscalation()
        {
            yield return new WaitForSeconds(lockedFloodingDelaySeconds);
            SetAllZones(FloodZoneState.Flooding);
            yield return new WaitForSeconds(lockedSubmergeDelaySeconds);
            SetAllZones(FloodZoneState.Submerged);
            _lockedEscalationCoroutine = null;
        }

        private float GetStageSpeedMultiplier()
        {
            return CurrentPressureStage switch
            {
                RoundPressureStage.Early => earlySpeedMultiplier,
                RoundPressureStage.Mid => midSpeedMultiplier,
                _ => lateSpeedMultiplier
            };
        }

        private float GetPulseCadenceSeconds()
        {
            return CurrentPressureStage switch
            {
                RoundPressureStage.Early => earlyPulseCadenceSeconds,
                RoundPressureStage.Mid => midPulseCadenceSeconds,
                _ => latePulseCadenceSeconds
            };
        }

        private FloodZone ResolveNextPulseZone()
        {
            HashSet<FloodZone> zones = GatherAllRelevantZones();
            if (zones.Count == 0)
            {
                return null;
            }

            List<FloodZone> ordered = new(zones);
            ordered.Sort((a, b) => string.CompareOrdinal(a.ZoneId, b.ZoneId));
            FloodZone zone = ordered[_nextPulseZoneIndex % ordered.Count];
            _nextPulseZoneIndex = (_nextPulseZoneIndex + 1) % ordered.Count;
            return zone;
        }

        private void ResetZonesToInitialState()
        {
            HashSet<FloodZone> uniqueZones = GatherAllRelevantZones();
            foreach (FloodZone zone in uniqueZones)
            {
                zone.ServerResetToInitialState();
            }
        }

        private void SetAllZones(FloodZoneState state)
        {
            HashSet<FloodZone> uniqueZones = GatherAllRelevantZones();
            foreach (FloodZone zone in uniqueZones)
            {
                zone.TrySetState(state);
            }
        }

        private HashSet<FloodZone> GatherAllRelevantZones()
        {
            HashSet<FloodZone> uniqueZones = new();
            foreach (ZoneSequence sequence in sequences)
            {
                if (sequence?.zone != null)
                {
                    uniqueZones.Add(sequence.zone);
                }
            }

            FloodZone[] allZones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            foreach (FloodZone zone in allZones)
            {
                if (zone != null)
                {
                    uniqueZones.Add(zone);
                }
            }

            return uniqueZones;
        }

        private float GetServerTime()
        {
            return NetworkManager == null ? 0f : (float)NetworkManager.ServerTime.Time;
        }
    }
}
