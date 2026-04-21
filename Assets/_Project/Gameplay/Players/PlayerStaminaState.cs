// File: Assets/_Project/Gameplay/Players/PlayerStaminaState.cs
using System;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerStaminaState : NetworkBehaviour
    {
        [Header("Stamina")]
        [SerializeField, Min(10f)] private float maxStamina = 100f;
        [SerializeField, Min(0f)] private float burstDrainPerSecond = 20f;
        [SerializeField, Min(0f)] private float wallStickDrainPerSecond = 16f;
        [SerializeField, Min(0f)] private float regenPerSecondGrounded = 18f;
        [SerializeField, Min(0f)] private float regenPerSecondIdle = 24f;
        [SerializeField, Min(0f)] private float regenDelayAfterUse = 0.7f;
        [SerializeField, Min(0f)] private float minBurstStamina = 8f;

        private readonly NetworkVariable<float> _stamina =
            new(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float _regenBlockedUntil;

        public event Action<float, float> StaminaChanged;

        public float CurrentStamina => _stamina.Value;
        public float MaxStamina => maxStamina;
        public float Normalized => maxStamina <= 0.001f ? 0f : Mathf.Clamp01(_stamina.Value / maxStamina);
        public bool CanBurst => _stamina.Value >= minBurstStamina;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _stamina.OnValueChanged += HandleStaminaChanged;
            if (IsServer)
            {
                _stamina.Value = maxStamina;
                _regenBlockedUntil = 0f;
            }
        }

        public override void OnNetworkDespawn()
        {
            _stamina.OnValueChanged -= HandleStaminaChanged;
            base.OnNetworkDespawn();
        }

        public void ServerResetForRound()
        {
            if (!IsServer)
            {
                return;
            }

            _stamina.Value = maxStamina;
            _regenBlockedUntil = 0f;
        }

        public bool ServerTryConsumeBurst(float deltaTime)
        {
            return ServerTryConsume(burstDrainPerSecond * Mathf.Max(0f, deltaTime));
        }

        public bool ServerTryConsumeWallStick(float deltaTime)
        {
            return ServerTryConsume(wallStickDrainPerSecond * Mathf.Max(0f, deltaTime));
        }

        public void ServerRegenerate(float deltaTime, bool grounded, bool idle)
        {
            if (!IsServer || deltaTime <= 0f || Time.time < _regenBlockedUntil)
            {
                return;
            }

            float regenRate = idle ? regenPerSecondIdle : (grounded ? regenPerSecondGrounded : 0f);
            if (regenRate <= 0f)
            {
                return;
            }

            _stamina.Value = Mathf.Clamp(_stamina.Value + (regenRate * deltaTime), 0f, maxStamina);
        }

        private bool ServerTryConsume(float amount)
        {
            if (!IsServer || amount <= 0f || _stamina.Value <= 0f)
            {
                return false;
            }

            _stamina.Value = Mathf.Clamp(_stamina.Value - amount, 0f, maxStamina);
            _regenBlockedUntil = Time.time + regenDelayAfterUse;
            return true;
        }

        private void HandleStaminaChanged(float previousValue, float currentValue)
        {
            StaminaChanged?.Invoke(previousValue, currentValue);
        }
    }
}
