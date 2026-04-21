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

        [Header("Water Level Curves")]
        [SerializeField, Range(0f, 1f)] private float dryLevel = 0f;
        [SerializeField, Range(0f, 1f)] private float wetLevel = 0.25f;
        [SerializeField, Range(0f, 1f)] private float floodingLevel = 0.68f;
        [SerializeField, Range(0f, 1f)] private float submergedLevel = 1f;
        [SerializeField, Range(0f, 1f)] private float sealedSafeLevel = 0.08f;
        [SerializeField, Min(0.05f)] private float levelLerpSpeed = 0.35f;
        [SerializeField, Min(0f)] private float oscillationAmplitude = 0.08f;
        [SerializeField, Min(0.1f)] private float oscillationFrequency = 0.28f;

        private readonly NetworkVariable<byte> _state =
            new((byte)FloodZoneState.Dry, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _waterLevel =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _flowVelocity =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action<FloodZoneState, FloodZoneState> StateChanged;

        public string ZoneId => zoneId;
        public FloodZoneState InitialState => initialState;
        public FloodZoneState CurrentState => (FloodZoneState)_state.Value;
        public float WaterLevel01 => _waterLevel.Value;
        public float FlowVelocity => _flowVelocity.Value;
        public bool IsSafe => CurrentState is FloodZoneState.Dry or FloodZoneState.Wet or FloodZoneState.SealedSafe;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _state.OnValueChanged += HandleStateChanged;
            if (IsServer)
            {
                _state.Value = (byte)initialState;
                _waterLevel.Value = GetBaseLevel(initialState);
                _flowVelocity.Value = 0f;
            }
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            float target = GetBaseLevel(CurrentState);
            if (CurrentState is FloodZoneState.Flooding or FloodZoneState.Submerged)
            {
                target = Mathf.Clamp01(target + (Mathf.Sin(Time.time * oscillationFrequency * Mathf.PI * 2f) * oscillationAmplitude));
            }

            float previous = _waterLevel.Value;
            _waterLevel.Value = Mathf.MoveTowards(previous, target, levelLerpSpeed * Time.deltaTime);
            _flowVelocity.Value = Mathf.Abs(_waterLevel.Value - previous) / Mathf.Max(Time.deltaTime, 0.001f);
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

        public void ServerResetToInitialState()
        {
            if (!IsServer)
            {
                return;
            }

            _state.Value = (byte)initialState;
            _waterLevel.Value = GetBaseLevel(initialState);
            _flowVelocity.Value = 0f;
        }

        private float GetBaseLevel(FloodZoneState state)
        {
            return state switch
            {
                FloodZoneState.Dry => dryLevel,
                FloodZoneState.Wet => wetLevel,
                FloodZoneState.Flooding => floodingLevel,
                FloodZoneState.Submerged => submergedLevel,
                FloodZoneState.SealedSafe => sealedSafeLevel,
                _ => dryLevel
            };
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
