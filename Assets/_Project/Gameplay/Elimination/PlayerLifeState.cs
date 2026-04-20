// File: Assets/_Project/Gameplay/Elimination/PlayerLifeState.cs
using System;
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

        public event Action<PlayerLifeStateKind, PlayerLifeStateKind> LifeStateChanged;

        public PlayerLifeStateKind CurrentLifeState => (PlayerLifeStateKind)_lifeState.Value;
        public bool IsAlive => CurrentLifeState == PlayerLifeStateKind.Alive;
        public bool HasSpawnedRemains => _hasSpawnedRemains.Value;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _lifeState.OnValueChanged += HandleLifeStateChanged;

            if (IsServer)
            {
                _lifeState.Value = (byte)PlayerLifeStateKind.Alive;
                _hasSpawnedRemains.Value = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            _lifeState.OnValueChanged -= HandleLifeStateChanged;
            base.OnNetworkDespawn();
        }

        public bool ServerTrySetEliminated()
        {
            if (!IsServer || !IsSpawned || !IsAlive)
            {
                return false;
            }

            _lifeState.Value = (byte)PlayerLifeStateKind.Eliminated;
            Debug.Log($"Player {OwnerClientId} marked eliminated.");
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
