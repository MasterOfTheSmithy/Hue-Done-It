// File: Assets/_Project/Gameplay/Players/NetworkPlayerAuthoritativeMover.cs
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkPlayerInputReader))]
    public sealed class NetworkPlayerAuthoritativeMover : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float inputSendRate = 20f;
        [SerializeField] private float remoteLerpSpeed = 20f;

        private readonly NetworkVariable<Vector3> _authoritativePosition =
            new(writePerm: NetworkVariableWritePermission.Server);

        private NetworkPlayerInputReader _inputReader;
        private Vector2 _serverMoveInput;
        private Vector2 _lastSentInput;
        private float _nextInputSendTime;

        private void Awake()
        {
            _inputReader = GetComponent<NetworkPlayerInputReader>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                _authoritativePosition.Value = transform.position;
            }
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner && IsClient)
            {
                SendInputToServerIfNeeded();
            }

            if (!IsServer)
            {
                transform.position = Vector3.Lerp(transform.position, _authoritativePosition.Value, remoteLerpSpeed * Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            if (IsOwner && IsClient && _inputReader != null)
            {
                _serverMoveInput = _inputReader.CurrentMoveInput;
            }

            Vector3 movement = new Vector3(_serverMoveInput.x, 0f, _serverMoveInput.y) * (moveSpeed * Time.fixedDeltaTime);
            transform.position += movement;
            _authoritativePosition.Value = transform.position;
        }

        private void SendInputToServerIfNeeded()
        {
            if (IsServer || _inputReader == null)
            {
                return;
            }

            float interval = inputSendRate <= 0f ? 0.05f : 1f / inputSendRate;
            bool elapsed = Time.unscaledTime >= _nextInputSendTime;
            bool changed = _inputReader.CurrentMoveInput != _lastSentInput;
            if (!elapsed && !changed)
            {
                return;
            }

            _lastSentInput = _inputReader.CurrentMoveInput;
            _nextInputSendTime = Time.unscaledTime + interval;
            SubmitMoveInputServerRpc(_lastSentInput);
        }

        [ServerRpc]
        private void SubmitMoveInputServerRpc(Vector2 moveInput, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning($"Rejected move input from non-owner client {rpcParams.Receive.SenderClientId}.");
                return;
            }

            _serverMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        }
    }
}
