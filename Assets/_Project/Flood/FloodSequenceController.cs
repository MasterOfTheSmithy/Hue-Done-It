// File: Assets/_Project/Flood/FloodSequenceController.cs
using System.Collections;
using System.Collections.Generic;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Flood
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FloodSequenceController : NetworkBehaviour
    {
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

        [SerializeField] private List<ZoneSequence> sequences = new();
        [SerializeField, Min(0.1f)] private float lockedFloodingDelaySeconds = 4f;
        [SerializeField, Min(0.1f)] private float lockedSubmergeDelaySeconds = 6f;

        private readonly List<Coroutine> _running = new();
        private Coroutine _lockedEscalationCoroutine;
        private bool _completionApplied;
        private bool _lockEscalationStarted;

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
            if (!IsServer)
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
            ResetZonesToInitialState();
            _completionApplied = false;
            _lockEscalationStarted = false;
            StartSequences();
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
                    yield return new WaitForSeconds(Mathf.Max(0.1f, step.durationSeconds));
                }
            }
            while (sequence.loop);
        }

        private IEnumerator RunLockedEscalation()
        {
            yield return new WaitForSeconds(lockedFloodingDelaySeconds);
            SetAllZones(FloodZoneState.Flooding);
            yield return new WaitForSeconds(lockedSubmergeDelaySeconds);
            SetAllZones(FloodZoneState.Submerged);
            _lockedEscalationCoroutine = null;
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

            if (uniqueZones.Count > 0)
            {
                return uniqueZones;
            }

            FloodZone[] sceneZones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            foreach (FloodZone zone in sceneZones)
            {
                if (zone != null)
                {
                    uniqueZones.Add(zone);
                }
            }

            return uniqueZones;
        }
    }
}
