// File: Assets/_Project/Gameplay/Players/NetworkPlayerAuthoritativeMover.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Round;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkPlayerInputReader))]
    [RequireComponent(typeof(PlayerLifeState))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class NetworkPlayerAuthoritativeMover : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 5.8f;
        [SerializeField, Min(0.1f)] private float groundAcceleration = 42f;
        [SerializeField, Min(0.1f)] private float airAcceleration = 18f;
        [SerializeField, Min(0.1f)] private float groundFriction = 16f;
        [SerializeField, Min(0.1f)] private float jumpVelocity = 6.1f;
        [SerializeField] private float gravity = -22f;
        [SerializeField] private float inputSendRate = 30f;
        [SerializeField] private float remoteLerpSpeed = 16f;
        [SerializeField] private float remoteRotationLerpSpeed = 16f;
        [SerializeField, Range(0f, 1f)] private float airControlMaxSpeedFactor = 0.92f;

        [Header("Grounding")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Min(0.01f)] private float groundCheckDistance = 0.2f;
        [SerializeField, Min(0.05f)] private float groundCheckRadius = 0.24f;
        [SerializeField, Min(0f)] private float stickToGroundVelocity = 2f;

        [Header("Collision")]
        [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;
        [SerializeField, Min(1)] private int depenetrationIterations = 4;
        [SerializeField, Min(1)] private int sweepIterations = 3;

        private readonly NetworkVariable<Vector3> _authoritativePosition =
            new(writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _authoritativeYaw =
            new(writePerm: NetworkVariableWritePermission.Server);

        private NetworkPlayerInputReader _inputReader;
        private PlayerLifeState _lifeState;
        private CapsuleCollider _capsuleCollider;
        private NetworkRoundState _roundState;

        private Vector3 _serverMoveWorldInput;
        private float _serverVisualYaw;
        private bool _serverJumpRequested;
        private float _verticalVelocity;
        private Vector3 _horizontalVelocity;

        private float _nextInputSendTime;
        private Vector3 _lastSentMoveWorldInput;
        private float _lastSentVisualYaw;

        private void Awake()
        {
            _inputReader = GetComponent<NetworkPlayerInputReader>();
            _lifeState = GetComponent<PlayerLifeState>();
            _capsuleCollider = GetComponent<CapsuleCollider>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResolveRoundState();
            if (IsServer)
            {
                ResolveInitialOverlaps();
                _authoritativePosition.Value = transform.position;
                _authoritativeYaw.Value = transform.eulerAngles.y;
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

            if (IsServer)
            {
                return;
            }

            transform.position = Vector3.Lerp(transform.position, _authoritativePosition.Value, remoteLerpSpeed * Time.deltaTime);
            if (!IsOwner)
            {
                Quaternion targetRotation = Quaternion.Euler(0f, _authoritativeYaw.Value, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, remoteRotationLerpSpeed * Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ResolveRoundState();

            if (_lifeState != null && !_lifeState.IsAlive)
            {
                _serverMoveWorldInput = Vector3.zero;
                _serverJumpRequested = false;
            }
            else if (IsOwner && IsClient && _inputReader != null)
            {
                _serverMoveWorldInput = _inputReader.CurrentWorldMoveInput;
                _serverVisualYaw = _inputReader.CurrentVisualYaw;
                _serverJumpRequested |= _inputReader.ConsumeJumpPressedThisFrame();
            }

            if (_roundState != null && _roundState.CurrentPhase != RoundPhase.FreeRoam)
            {
                _serverMoveWorldInput = Vector3.zero;
                _serverJumpRequested = false;
            }

            SimulateServerMotion(Time.fixedDeltaTime);
        }

        public void ServerTeleportTo(Vector3 worldPosition, float yawDegrees)
        {
            if (!IsServer)
            {
                return;
            }

            _serverMoveWorldInput = Vector3.zero;
            _serverJumpRequested = false;
            _verticalVelocity = 0f;
            _horizontalVelocity = Vector3.zero;
            _serverVisualYaw = yawDegrees;

            transform.SetPositionAndRotation(worldPosition, Quaternion.Euler(0f, yawDegrees, 0f));
            ResolveInitialOverlaps();

            _authoritativePosition.Value = transform.position;
            _authoritativeYaw.Value = yawDegrees;
        }

        private void SimulateServerMotion(float deltaTime)
        {
            bool grounded = IsGrounded(transform.position);

            Vector3 desiredHorizontalVelocity = Vector3.ClampMagnitude(_serverMoveWorldInput, 1f) * moveSpeed;
            float acceleration = grounded ? groundAcceleration : airAcceleration;
            if (grounded)
            {
                _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, desiredHorizontalVelocity, acceleration * deltaTime);

                if (desiredHorizontalVelocity.sqrMagnitude < 0.0001f)
                {
                    _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, Vector3.zero, groundFriction * deltaTime);
                }

                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = -stickToGroundVelocity;
                }

                if (_serverJumpRequested)
                {
                    _verticalVelocity = jumpVelocity;
                    grounded = false;
                }
            }
            else
            {
                Vector3 cappedDesiredVelocity = desiredHorizontalVelocity * airControlMaxSpeedFactor;
                _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, cappedDesiredVelocity, acceleration * deltaTime);
            }

            _serverJumpRequested = false;
            _verticalVelocity += gravity * deltaTime;

            Vector3 displacement = (_horizontalVelocity * deltaTime) + (Vector3.up * (_verticalVelocity * deltaTime));
            Vector3 nextPosition = MoveCharacter(transform.position, displacement, out bool hitGround);

            transform.SetPositionAndRotation(nextPosition, Quaternion.Euler(0f, _serverVisualYaw, 0f));

            if (hitGround && _verticalVelocity < 0f)
            {
                _verticalVelocity = -stickToGroundVelocity;
            }

            _authoritativePosition.Value = transform.position;
            _authoritativeYaw.Value = _serverVisualYaw;
        }

        private Vector3 MoveCharacter(Vector3 startPosition, Vector3 displacement, out bool hitGround)
        {
            hitGround = false;
            Vector3 currentPosition = ResolvePenetration(startPosition);
            Vector3 remaining = displacement;

            for (int iteration = 0; iteration < sweepIterations; iteration++)
            {
                float distance = remaining.magnitude;
                if (distance <= 0.0001f)
                {
                    break;
                }

                Vector3 direction = remaining / distance;
                GetCapsuleWorldPoints(currentPosition, out Vector3 point1, out Vector3 point2, out float radius);

                if (!Physics.CapsuleCast(point1, point2, radius, direction, out RaycastHit hit, distance + collisionSkin, groundMask, QueryTriggerInteraction.Ignore))
                {
                    currentPosition += remaining;
                    remaining = Vector3.zero;
                    break;
                }

                float allowedDistance = Mathf.Max(0f, hit.distance - collisionSkin);
                currentPosition += direction * allowedDistance;

                Vector3 normal = hit.normal;
                if (normal.y > 0.5f)
                {
                    hitGround = true;
                }

                remaining = Vector3.ProjectOnPlane(remaining - (direction * allowedDistance), normal);

                if (direction.y < 0f && normal.y > 0.001f)
                {
                    _horizontalVelocity = Vector3.ProjectOnPlane(_horizontalVelocity, normal);
                }

                currentPosition = ResolvePenetration(currentPosition);
            }

            return ResolvePenetration(currentPosition);
        }

        private bool IsGrounded(Vector3 position)
        {
            GetCapsuleWorldPoints(position, out Vector3 point1, out _, out float radius);
            float probeRadius = Mathf.Min(radius * 0.95f, Mathf.Max(0.05f, groundCheckRadius));
            Vector3 origin = point1 + (Vector3.up * 0.05f);
            float probeDistance = Mathf.Max(0.05f, groundCheckDistance + 0.05f);
            return Physics.SphereCast(origin, probeRadius, Vector3.down, out _, probeDistance, groundMask, QueryTriggerInteraction.Ignore);
        }

        private void GetCapsuleWorldPoints(Vector3 position, out Vector3 point1, out Vector3 point2, out float radius)
        {
            if (_capsuleCollider == null)
            {
                radius = 0.5f;
                point1 = position + (Vector3.up * 0.5f);
                point2 = position + (Vector3.up * 1.5f);
                return;
            }

            Transform capsuleTransform = _capsuleCollider.transform;
            Vector3 scale = capsuleTransform.lossyScale;
            Vector3 center = position + (capsuleTransform.rotation * Vector3.Scale(_capsuleCollider.center, scale));

            int direction = _capsuleCollider.direction;
            Vector3 axis = direction == 0 ? capsuleTransform.right : direction == 2 ? capsuleTransform.forward : capsuleTransform.up;

            float axisScale = direction == 0 ? Mathf.Abs(scale.x) : direction == 2 ? Mathf.Abs(scale.z) : Mathf.Abs(scale.y);
            float radiusScaleA = Mathf.Abs(scale[(direction + 1) % 3]);
            float radiusScaleB = Mathf.Abs(scale[(direction + 2) % 3]);

            radius = _capsuleCollider.radius * Mathf.Max(radiusScaleA, radiusScaleB);
            float scaledHeight = Mathf.Max(_capsuleCollider.height * axisScale, radius * 2f);
            float segmentHalf = Mathf.Max(0f, (scaledHeight * 0.5f) - radius);

            point1 = center + (axis * segmentHalf);
            point2 = center - (axis * segmentHalf);
        }

        private Vector3 ResolvePenetration(Vector3 position)
        {
            if (_capsuleCollider == null)
            {
                return position;
            }

            Vector3 resolvedPosition = position;
            for (int iteration = 0; iteration < depenetrationIterations; iteration++)
            {
                GetCapsuleWorldPoints(resolvedPosition, out Vector3 point1, out Vector3 point2, out float radius);
                Collider[] overlaps = Physics.OverlapCapsule(point1, point2, radius, groundMask, QueryTriggerInteraction.Ignore);

                bool moved = false;
                for (int i = 0; i < overlaps.Length; i++)
                {
                    Collider other = overlaps[i];
                    if (other == null || other == _capsuleCollider || other.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    if (Physics.ComputePenetration(
                            _capsuleCollider, resolvedPosition, transform.rotation,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 separationDirection, out float separationDistance))
                    {
                        resolvedPosition += separationDirection * (separationDistance + collisionSkin);
                        moved = true;
                    }
                }

                if (!moved)
                {
                    break;
                }
            }

            return resolvedPosition;
        }

        private void ResolveInitialOverlaps()
        {
            transform.position = ResolvePenetration(transform.position);
        }

        private void SendInputToServerIfNeeded()
        {
            if (Time.unscaledTime < _nextInputSendTime)
            {
                return;
            }

            _nextInputSendTime = Time.unscaledTime + (1f / Mathf.Max(1f, inputSendRate));

            if (_inputReader == null)
            {
                return;
            }

            Vector3 move = _inputReader.CurrentWorldMoveInput;
            float yaw = _inputReader.CurrentVisualYaw;
            bool jumpPressed = _inputReader.ConsumeJumpPressedThisFrame();

            bool shouldSend = (move - _lastSentMoveWorldInput).sqrMagnitude > 0.0001f ||
                              Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentVisualYaw)) > 0.5f ||
                              jumpPressed;

            if (!shouldSend)
            {
                return;
            }

            _lastSentMoveWorldInput = move;
            _lastSentVisualYaw = yaw;
            SubmitInputServerRpc(move, yaw, jumpPressed);
        }

        [ServerRpc]
        private void SubmitInputServerRpc(Vector3 moveWorldInput, float visualYaw, bool jumpPressed, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            _serverMoveWorldInput = Vector3.ClampMagnitude(moveWorldInput, 1f);
            _serverVisualYaw = visualYaw;
            _serverJumpRequested |= jumpPressed;
        }

        private void ResolveRoundState()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }
        }
    }
}
