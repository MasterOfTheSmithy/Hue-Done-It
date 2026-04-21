// File: Assets/_Project/Flood/FloodZone.cs
using System;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Flood
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FloodZone : NetworkBehaviour
    {
        [SerializeField] private string zoneId = "Zone";
        [SerializeField] private FloodZoneState initialState = FloodZoneState.Dry;

        private readonly NetworkVariable<byte> _state =
            new((byte)FloodZoneState.Dry, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action<FloodZoneState, FloodZoneState> StateChanged;

        public string ZoneId => zoneId;
        public FloodZoneState CurrentState => (FloodZoneState)_state.Value;
        public bool IsSafe => CurrentState is FloodZoneState.Dry or FloodZoneState.Wet or FloodZoneState.SealedSafe;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _state.OnValueChanged += HandleStateChanged;
            if (IsServer)
            {
                _state.Value = (byte)initialState;
            }
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
            base.OnNetworkDespawn();
        }

        public bool TrySetState(FloodZoneState nextState)
        {
            if (!IsServer || CurrentState == nextState)
            {
                return false;
            }

            _state.Value = (byte)nextState;
            return true;
        }

        private void HandleStateChanged(byte previous, byte current)
        {
            FloodZoneState previousState = (FloodZoneState)previous;
            FloodZoneState currentState = (FloodZoneState)current;
            StateChanged?.Invoke(previousState, currentState);
            if (IsServer)
            {
                UnityEngine.Debug.Log($"Flood zone '{zoneId}' transitioned {previousState} -> {currentState}.");
            }
        }
    }
}
