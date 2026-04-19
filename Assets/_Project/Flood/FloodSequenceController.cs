// File: Assets/_Project/Flood/FloodSequenceController.cs
using System.Collections;
using System.Collections.Generic;
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

        private readonly List<Coroutine> _running = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                StartSequences();
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
    }
}
