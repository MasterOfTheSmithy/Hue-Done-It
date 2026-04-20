// File: Assets/_Project/Gameplay/Round/NetworkRoundState.cs
using System;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Round
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRoundState : NetworkBehaviour
    {
        private readonly NetworkVariable<byte> _phase =
            new((byte)RoundPhase.FreeRoam, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _reportingClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _reportedVictimClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action<RoundPhase, RoundPhase> PhaseChanged;

        public RoundPhase CurrentPhase => (RoundPhase)_phase.Value;
        public ulong ReportingClientId => _reportingClientId.Value;
        public ulong ReportedVictimClientId => _reportedVictimClientId.Value;
        public bool IsFreeRoam => CurrentPhase == RoundPhase.FreeRoam;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _phase.OnValueChanged += HandlePhaseChanged;

            if (IsServer)
            {
                _phase.Value = (byte)RoundPhase.FreeRoam;
                _reportingClientId.Value = ulong.MaxValue;
                _reportedVictimClientId.Value = ulong.MaxValue;
            }
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            base.OnNetworkDespawn();
        }

        public bool ServerTrySetReported(ulong reportingClientId, ulong reportedVictimClientId)
        {
            if (!IsServer || CurrentPhase == RoundPhase.Reported)
            {
                return false;
            }

            _reportingClientId.Value = reportingClientId;
            _reportedVictimClientId.Value = reportedVictimClientId;
            _phase.Value = (byte)RoundPhase.Reported;
            Debug.Log($"Round phase transitioned to Reported. Reporter={reportingClientId}, Victim={reportedVictimClientId}");
            return true;
        }

        private void HandlePhaseChanged(byte previousValue, byte currentValue)
        {
            PhaseChanged?.Invoke((RoundPhase)previousValue, (RoundPhase)currentValue);
        }
    }
}
