// File: Assets/_Project/Gameplay/Elimination/PlayerLifeState.cs
using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Elimination
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerLifeState : NetworkBehaviour, IPlayerLifeStateReader
    {
        private readonly NetworkVariable<byte> _lifeState =
            new((byte)PlayerLifeStateKind.Alive, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _hasSpawnedRemains =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _lastStateReason =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action<PlayerLifeStateKind, PlayerLifeStateKind> LifeStateChanged;

        public PlayerLifeStateKind CurrentLifeState => (PlayerLifeStateKind)_lifeState.Value;
        public bool IsAlive => CurrentLifeState == PlayerLifeStateKind.Alive;
        public bool HasSpawnedRemains => _hasSpawnedRemains.Value;
        public string LastStateReason => _lastStateReason.Value.ToString();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _lifeState.OnValueChanged += HandleLifeStateChanged;

            if (IsServer)
            {
                ServerResetForRound();
            }
        }

        public override void OnNetworkDespawn()
        {
            _lifeState.OnValueChanged -= HandleLifeStateChanged;
            base.OnNetworkDespawn();
        }

        public void ServerResetForRound()
        {
            if (!IsServer)
            {
                return;
            }

            _lifeState.Value = (byte)PlayerLifeStateKind.Alive;
            _hasSpawnedRemains.Value = false;
            _lastStateReason.Value = default;
        }

        public bool ServerTrySetEliminated(string reason = "Eliminated")
        {
            return ServerTrySetLifeState(PlayerLifeStateKind.Eliminated, reason);
        }

        public bool ServerTrySetDiffused(string reason = "Diffused by flood")
        {
            return ServerTrySetLifeState(PlayerLifeStateKind.DiffusedByFlood, reason);
        }

        public bool ServerTrySetBackfired(string reason = "Backfired")
        {
            return ServerTrySetLifeState(PlayerLifeStateKind.Backfired, reason);
        }

        public bool ServerTrySetLifeState(PlayerLifeStateKind nextState, string reason)
        {
            if (!IsServer || !IsSpawned || !IsAlive)
            {
                return false;
            }

            _lifeState.Value = (byte)nextState;
            _lastStateReason.Value = string.IsNullOrWhiteSpace(reason)
                ? new FixedString64Bytes(nextState.ToString())
                : new FixedString64Bytes(reason);

            Debug.Log($"Player {OwnerClientId} marked {nextState}. Reason={_lastStateReason.Value}");
            return true;
        }

        public bool ServerTryMarkRemainsSpawned()
        {
            if (!IsServer || _hasSpawnedRemains.Value)
            {
                return false;
            }

            _hasSpawnedRemains.Value = true;
            return true;
        }

        private void HandleLifeStateChanged(byte previousValue, byte currentValue)
        {
            LifeStateChanged?.Invoke((PlayerLifeStateKind)previousValue, (PlayerLifeStateKind)currentValue);
        }
    }
}
