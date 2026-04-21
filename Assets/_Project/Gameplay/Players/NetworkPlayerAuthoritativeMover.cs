// File: Assets/_Project/Gameplay/Players/NetworkPlayerAuthoritativeMover.cs
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Round;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkPlayerInputReader))]
    [RequireComponent(typeof(PlayerLifeState))]
    [RequireComponent(typeof(PlayerFloodZoneTracker))]
    [RequireComponent(typeof(NetworkPlayerPaintEmitter))]
    [RequireComponent(typeof(PlayerStaminaState))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class NetworkPlayerAuthoritativeMover : NetworkBehaviour
    {
        public enum LocomotionState : byte
        {
            Grounded = 0,
            Airborne = 1,
            WallSlide = 2,
            WallStick = 3,
            WallLaunch = 4,
            LowGravityFloat = 5,
            Knockback = 6,
            Ragdoll = 7
        }

        [Header("Core Movement")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 6.2f;
        [SerializeField, Min(0.1f)] private float burstMoveSpeed = 8.1f;
        [SerializeField, Min(0.1f)] private float groundAcceleration = 58f;
        [SerializeField, Min(0.1f)] private float airAcceleration = 22f;
        [SerializeField, Min(0.1f)] private float dragDamping = 8f;
        [SerializeField, Min(0.1f)] private float jumpVelocity = 7.1f;
        [SerializeField, Min(0.1f)] private float gravityMagnitude = 24f;
        [SerializeField, Min(0f)] private float lowGravityMultiplier = 0.32f;
        [SerializeField, Min(0f)] private float stickToGroundVelocity = 2f;
        [SerializeField, Min(0.01f)] private float coyoteTimeSeconds = 0.14f;
        [SerializeField, Min(0.01f)] private float jumpBufferSeconds = 0.15f;

        [Header("Wall Play")]
        [SerializeField, Min(0.05f)] private float wallDetectionDistance = 0.65f;
        [SerializeField, Min(0f)] private float wallStickForce = 8f;
        [SerializeField, Min(0f)] private float wallStickDuration = 0.38f;
        [SerializeField] private bool allowUnlimitedLowGravityWallStick = true;
        [SerializeField, Min(0f)] private float wallSlideMaxFallSpeed = 2.3f;
        [SerializeField, Min(0.1f)] private float wallLaunchHorizontalForce = 7.6f;
        [SerializeField, Min(0.1f)] private float wallLaunchVerticalForce = 6.8f;
        [SerializeField, Min(0f)] private float wallLaunchInputInfluence = 1.2f;
        [SerializeField, Min(0f)] private float wallLaunchSeparationBoost = 2.4f;
        [SerializeField, Min(0f)] private float wallGlidePlanarAssist = 0.55f;
        [SerializeField, Min(0f)] private float postWallLaunchControlLockSeconds = 0.14f;
        [SerializeField, Range(0f, 1f)] private float postWallLaunchAirAccelerationMultiplier = 0.35f;
        [SerializeField, Min(0.01f)] private float wallDetachGraceSeconds = 0.2f;
        [SerializeField, Range(0f, 1f)] private float wallDetachNormalDotThreshold = 0.5f;

        [Header("Punch / Knockback")]
        [SerializeField, Min(0.1f)] private float punchCooldownSeconds = 0.75f;
        [SerializeField, Min(0.1f)] private float punchRange = 2.2f;
        [SerializeField, Min(0.1f)] private float punchRadius = 0.8f;
        [SerializeField, Min(0.1f)] private float knockbackImpulse = 10f;
        [SerializeField, Min(0f)] private float knockbackUpwardBoost = 2.2f;
        [SerializeField, Min(0.05f)] private float knockbackLockSeconds = 0.24f;
        [SerializeField, Min(0.1f)] private float knockbackDamping = 14f;
        [SerializeField, Min(0.1f)] private float airbornePunchRagdollImpulseThreshold = 10.5f;
        [SerializeField] private LayerMask punchMask = ~0;

        [Header("Ragdoll (Authoritative Fallback Tumble)")]
        [SerializeField, Min(0.1f)] private float airborneRagdollSpeedThreshold = 14f;
        [SerializeField, Min(0.05f)] private float ragdollMinDurationSeconds = 0.8f;
        [SerializeField, Min(0.05f)] private float ragdollRecoveryLockSeconds = 0.45f;
        [SerializeField, Min(0f)] private float ragdollHorizontalDamping = 1.1f;
        [SerializeField, Min(0f)] private float ragdollGravityMultiplier = 1.2f;

        [Header("Grounding / Collision")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Min(0.01f)] private float groundCheckDistance = 0.22f;
        [SerializeField, Min(0.05f)] private float groundCheckRadius = 0.25f;
        [SerializeField, Range(0f, 89f)] private float maxGroundSlopeAngle = 52f;
        [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;
        [SerializeField, Min(1)] private int depenetrationIterations = 4;
        [SerializeField, Min(1)] private int sweepIterations = 3;

        [Header("Blob Visual Deformation")]
        [SerializeField] private Transform blobVisualRoot;
        [SerializeField, Min(0f)] private float stretchByHorizontalSpeed = 0.05f;
        [SerializeField, Min(0f)] private float stretchByVerticalSpeed = 0.02f;
        [SerializeField, Min(0f)] private float accelerationStretch = 0.004f;
        [SerializeField, Min(0f)] private float landingSquashStrength = 0.06f;
        [SerializeField, Min(0f)] private float wallCompressionStrength = 0.14f;
        [SerializeField, Min(0f)] private float airborneSquashAmount = 0.03f;
        [SerializeField, Min(0.01f)] private float deformationLerpSpeed = 11f;
        [SerializeField] private Vector2 stretchClamp = new(0.85f, 1.35f);
        [SerializeField] private Vector2 squashClamp = new(0.72f, 1.22f);

        [Header("Networking")]
        [SerializeField] private float inputSendRate = 30f;
        [SerializeField] private float remoteLerpSpeed = 16f;
        [SerializeField] private float remoteRotationLerpSpeed = 16f;

        [Header("Debug")]
        [SerializeField] private bool drawDebugGizmos = true;
        [SerializeField] private bool logJumpStateTransitions;

        private readonly NetworkVariable<Vector3> _authoritativePosition =
            new(writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _authoritativeYaw =
            new(writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> _locomotionState =
            new((byte)LocomotionState.Airborne, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _authoritativeVelocity =
            new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _supportNormal =
            new(Vector3.up, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _lastLandingImpact =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _wallCompression01 =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _punchCooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetworkPlayerInputReader _inputReader;
        private PlayerLifeState _lifeState;
        private PlayerFloodZoneTracker _floodTracker;
        private PlayerStaminaState _staminaState;
        private NetworkPlayerPaintEmitter _paintEmitter;
        private CapsuleCollider _capsuleCollider;
        private NetworkRoundState _roundState;

        private Vector3 _serverMoveWorldInput;
        private float _serverVisualYaw;
        private bool _serverJumpRequested;
        private bool _serverPunchRequested;
        private bool _serverBurstHeld;

        private float _verticalVelocity;
        private Vector3 _horizontalVelocity;
        private float _wallStickTimeRemaining;
        private float _wallDetachTimeRemaining;
        private float _knockbackTimeRemaining;
        private float _wallLaunchControlTimeRemaining;
        private float _ragdollTimeRemaining;
        private float _ragdollRecoveryTimeRemaining;
        private float _jumpBufferTimeRemaining;
        private float _coyoteTimeRemaining;
        private bool _wasGroundedLastFrame;
        private Vector3 _lastWallNormal = Vector3.forward;

        private float _nextInputSendTime;
        private Vector3 _lastSentMoveWorldInput;
        private float _lastSentVisualYaw;
        private bool _lastSentBurstHeld;

        private float _nextMovementPaintTime;
        private float _nextWallPaintTime;

        private Vector3 _blobBaseScale = Vector3.one;
        private Vector3 _blobCurrentScale = Vector3.one;
        private Vector3 _lastVisualVelocity;
        private float _blobLandingImpulseVisual;

        public LocomotionState CurrentState => (LocomotionState)_locomotionState.Value;
        public float PunchCooldownRemaining => Mathf.Max(0f, _punchCooldownEndServerTime.Value - GetServerTime());
        public Vector3 CurrentVelocity => _authoritativeVelocity.Value;
        public Vector3 CurrentSupportNormal => _supportNormal.Value;
        public float GroundedCoyoteRemaining => _coyoteTimeRemaining;
        public float JumpBufferRemaining => _jumpBufferTimeRemaining;
        public float LastLandingImpact => _lastLandingImpact.Value;
        public float WallCompression => _wallCompression01.Value;
        public float Opacity01
        {
            get
            {
                if (_floodTracker != null)
                {
                    return 1f - Mathf.Clamp01(_floodTracker.Saturation01);
                }

                if (_lifeState != null)
                {
                    return _lifeState.IsAlive ? 1f : 0f;
                }

                return 1f;
            }
        }

        public float Cohesion01 => _staminaState != null ? _staminaState.Normalized : 1f;

        // Temporary HUD compatibility shim.
        // Replace HUD references with Cohesion01 later.
        public float Stamina01 => _staminaState != null ? _staminaState.Normalized : 1f;

        private void Awake()
        {
            _inputReader = GetComponent<NetworkPlayerInputReader>();
            _lifeState = GetComponent<PlayerLifeState>();
            _floodTracker = GetComponent<PlayerFloodZoneTracker>();
            _staminaState = GetComponent<PlayerStaminaState>();
            _paintEmitter = GetComponent<NetworkPlayerPaintEmitter>();
            _capsuleCollider = GetComponent<CapsuleCollider>();

            if (blobVisualRoot == null)
            {
                blobVisualRoot = transform.childCount > 0 ? transform.GetChild(0) : null;
            }

            if (blobVisualRoot != null)
            {
                _blobBaseScale = blobVisualRoot.localScale;
                _blobCurrentScale = _blobBaseScale;
            }
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
                _locomotionState.Value = (byte)LocomotionState.Airborne;
                _authoritativeVelocity.Value = Vector3.zero;
                _supportNormal.Value = Vector3.up;
                _lastLandingImpact.Value = 0f;
                _wallCompression01.Value = 0f;
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
                if (!IsOwner)
                {
                    Quaternion targetRotation = Quaternion.Euler(0f, _authoritativeYaw.Value, 0f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, remoteRotationLerpSpeed * Time.deltaTime);
                }
            }

            ApplyBlobDeformation(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ResolveRoundState();
            ConsumeLatestInput();
            SimulateServerMotion(Time.fixedDeltaTime);
            TryResolvePunch();
        }

        public void ServerTeleportTo(Vector3 worldPosition, float yawDegrees)
        {
            if (!IsServer)
            {
                return;
            }

            _serverMoveWorldInput = Vector3.zero;
            _serverJumpRequested = false;
            _serverPunchRequested = false;
            _verticalVelocity = 0f;
            _horizontalVelocity = Vector3.zero;
            _knockbackTimeRemaining = 0f;
            _wallDetachTimeRemaining = 0f;
            _wallLaunchControlTimeRemaining = 0f;
            _ragdollTimeRemaining = 0f;
            _ragdollRecoveryTimeRemaining = 0f;
            _jumpBufferTimeRemaining = 0f;
            _coyoteTimeRemaining = 0f;
            _serverVisualYaw = yawDegrees;
            _locomotionState.Value = (byte)LocomotionState.Airborne;

            transform.SetPositionAndRotation(worldPosition, Quaternion.Euler(0f, yawDegrees, 0f));
            ResolveInitialOverlaps();

            _authoritativePosition.Value = transform.position;
            _authoritativeYaw.Value = yawDegrees;
            _authoritativeVelocity.Value = Vector3.zero;
            _supportNormal.Value = Vector3.up;
            _lastLandingImpact.Value = 0f;
            _wallCompression01.Value = 0f;
        }

        public void ServerApplyKnockback(Vector3 impulse)
        {
            if (!IsServer)
            {
                return;
            }

            bool wasAirborne = !_wasGroundedLastFrame;
            _horizontalVelocity = new Vector3(impulse.x, 0f, impulse.z);
            _verticalVelocity = Mathf.Max(impulse.y, 0f);
            _knockbackTimeRemaining = knockbackLockSeconds;
            _wallStickTimeRemaining = 0f;
            _wallLaunchControlTimeRemaining = 0f;
            _wallDetachTimeRemaining = wallDetachGraceSeconds;
            _locomotionState.Value = (byte)LocomotionState.Knockback;

            if (wasAirborne && impulse.magnitude >= airbornePunchRagdollImpulseThreshold)
            {
                EnterRagdoll(ragdollMinDurationSeconds);
            }

            if (_paintEmitter != null)
            {
                EmitPaint(PaintEventKind.Punch, transform.position + (Vector3.up * 0.7f), -impulse.normalized, 0.52f, 0.85f, impulse.magnitude, impulse.normalized, PaintSplatPermanence.Permanent);
            }
        }

        private void ConsumeLatestInput()
        {
            if (_lifeState != null && !_lifeState.IsAlive)
            {
                _serverMoveWorldInput = Vector3.zero;
                _serverJumpRequested = false;
                _serverPunchRequested = false;
                _serverBurstHeld = false;
                return;
            }

            if (IsOwner && IsClient && _inputReader != null)
            {
                _serverMoveWorldInput = _inputReader.CurrentWorldMoveInput;
                _serverVisualYaw = _inputReader.CurrentVisualYaw;
                _serverJumpRequested |= _inputReader.ConsumeJumpPressedThisFrame();
                _serverPunchRequested |= _inputReader.ConsumePunchPressedThisFrame();
                _serverBurstHeld = _inputReader.BurstHeld;
            }

            if (_roundState != null && _roundState.CurrentPhase != RoundPhase.FreeRoam)
            {
                _serverMoveWorldInput = Vector3.zero;
                _serverJumpRequested = false;
                _serverPunchRequested = false;
                _serverBurstHeld = false;
            }

            if (_serverJumpRequested)
            {
                _jumpBufferTimeRemaining = Mathf.Max(_jumpBufferTimeRemaining, jumpBufferSeconds);
            }
        }

        private void SimulateServerMotion(float deltaTime)
        {
            bool lowGravity = IsInLowGravityZone();
            bool grounded = IsGrounded(transform.position, out RaycastHit groundHit);
            bool hasWall = TryFindWall(transform.position, out RaycastHit wallHit);

            _wallDetachTimeRemaining = Mathf.Max(0f, _wallDetachTimeRemaining - deltaTime);
            _jumpBufferTimeRemaining = Mathf.Max(0f, _jumpBufferTimeRemaining - deltaTime);
            _wallLaunchControlTimeRemaining = Mathf.Max(0f, _wallLaunchControlTimeRemaining - deltaTime);
            _ragdollRecoveryTimeRemaining = Mathf.Max(0f, _ragdollRecoveryTimeRemaining - deltaTime);

            if (grounded)
            {
                _coyoteTimeRemaining = coyoteTimeSeconds;
            }
            else
            {
                _coyoteTimeRemaining = Mathf.Max(0f, _coyoteTimeRemaining - deltaTime);
            }

            if (hasWall && _wallDetachTimeRemaining > 0f && Vector3.Dot(wallHit.normal, _lastWallNormal) >= wallDetachNormalDotThreshold)
            {
                hasWall = false;
            }

            Vector3 desiredMoveDir = Vector3.ClampMagnitude(_serverMoveWorldInput, 1f);
            bool burstAllowed = _serverBurstHeld && (_staminaState == null || _staminaState.CanBurst);
            float targetSpeed = burstAllowed ? burstMoveSpeed : moveSpeed;
            Vector3 desiredHorizontalVelocity = desiredMoveDir * targetSpeed;

            if (_ragdollTimeRemaining > 0f)
            {
                SimulateRagdoll(deltaTime, lowGravity, hasWall, wallHit);
            }
            else if (_knockbackTimeRemaining > 0f)
            {
                _knockbackTimeRemaining -= deltaTime;
                _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, Vector3.zero, knockbackDamping * deltaTime);
                _verticalVelocity += -GetGravity(lowGravity) * deltaTime;
                _supportNormal.Value = hasWall ? wallHit.normal : Vector3.up;
                _locomotionState.Value = (byte)LocomotionState.Knockback;
            }
            else
            {
                bool canGroundJump = grounded || _coyoteTimeRemaining > 0f;
                bool consumedJump = false;

                if (canGroundJump && _jumpBufferTimeRemaining > 0f && _ragdollRecoveryTimeRemaining <= 0f)
                {
                    _verticalVelocity = jumpVelocity;
                    _jumpBufferTimeRemaining = 0f;
                    _coyoteTimeRemaining = 0f;
                    grounded = false;
                    consumedJump = true;
                    _locomotionState.Value = (byte)LocomotionState.Airborne;
                    EmitPaint(PaintEventKind.Move, transform.position + (Vector3.down * 0.45f), Vector3.up, 0.28f, 0.6f, jumpVelocity, transform.forward);
                }

                if (grounded)
                {
                    _wallStickTimeRemaining = wallStickDuration;
                    _supportNormal.Value = groundHit.normal;
                    _locomotionState.Value = _ragdollRecoveryTimeRemaining > 0f
                        ? (byte)LocomotionState.Knockback
                        : (byte)LocomotionState.Grounded;

                    Vector3 planarDesired = Vector3.ProjectOnPlane(desiredHorizontalVelocity, groundHit.normal);
                    _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, planarDesired, groundAcceleration * deltaTime);

                    if (planarDesired.sqrMagnitude < 0.0001f)
                    {
                        _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, Vector3.zero, dragDamping * deltaTime);
                    }

                    if (_verticalVelocity < 0f)
                    {
                        _verticalVelocity = -stickToGroundVelocity;
                    }
                }
                else
                {
                    float effectiveAirAcceleration = _wallLaunchControlTimeRemaining > 0f
                        ? airAcceleration * postWallLaunchAirAccelerationMultiplier
                        : airAcceleration;

                    _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, desiredHorizontalVelocity, effectiveAirAcceleration * deltaTime);

                    if (hasWall)
                    {
                        _lastWallNormal = wallHit.normal;
                        _supportNormal.Value = wallHit.normal;
                        bool movingIntoWall = Vector3.Dot(desiredMoveDir, -wallHit.normal) > 0.15f;

                        if (_jumpBufferTimeRemaining > 0f && _ragdollRecoveryTimeRemaining <= 0f)
                        {
                            Vector3 lateralMomentum = Vector3.ProjectOnPlane(_horizontalVelocity, Vector3.up);
                            Vector3 away = wallHit.normal * (wallLaunchHorizontalForce + wallLaunchSeparationBoost);
                            Vector3 alongWallInput = Vector3.ProjectOnPlane(desiredHorizontalVelocity, wallHit.normal) * wallLaunchInputInfluence;
                            _horizontalVelocity = lateralMomentum + away + alongWallInput;
                            _verticalVelocity = Mathf.Max(_verticalVelocity, wallLaunchVerticalForce);
                            _wallStickTimeRemaining = 0f;
                            _wallDetachTimeRemaining = wallDetachGraceSeconds;
                            _wallLaunchControlTimeRemaining = postWallLaunchControlLockSeconds;
                            _jumpBufferTimeRemaining = 0f;
                            _locomotionState.Value = (byte)LocomotionState.WallLaunch;
                            consumedJump = true;
                            EmitPaint(PaintEventKind.WallLaunch, wallHit.point, wallHit.normal, 0.45f, 0.95f, _horizontalVelocity.magnitude, _horizontalVelocity.normalized);
                        }
                        else if (movingIntoWall)
                        {
                            bool allowStick = lowGravity && allowUnlimitedLowGravityWallStick
                                ? true
                                : _wallStickTimeRemaining > 0f;
                            if (allowStick && _staminaState != null && !_staminaState.ServerTryConsumeWallStick(deltaTime))
                            {
                                allowStick = false;
                            }

                            if (allowStick)
                            {
                                _locomotionState.Value = _ragdollRecoveryTimeRemaining > 0f
                                    ? (byte)LocomotionState.Knockback
                                    : (byte)LocomotionState.WallStick;

                                _verticalVelocity = Mathf.Max(_verticalVelocity - (wallStickForce * deltaTime), -0.4f);
                                Vector3 projected = Vector3.ProjectOnPlane(_horizontalVelocity, wallHit.normal) * 0.94f;
                                Vector3 wallCarry = Vector3.ProjectOnPlane(desiredHorizontalVelocity, wallHit.normal) * wallGlidePlanarAssist;
                                _horizontalVelocity = projected + wallCarry;

                                if (!lowGravity)
                                {
                                    _wallStickTimeRemaining -= deltaTime;
                                }

                                if (Time.time >= _nextWallPaintTime)
                                {
                                    _nextWallPaintTime = Time.time + (lowGravity ? 0.18f : 0.25f);
                                    EmitPaint(PaintEventKind.WallStick, wallHit.point, wallHit.normal, 0.22f, lowGravity ? 0.9f : 0.55f, _horizontalVelocity.magnitude, _horizontalVelocity.normalized);
                                }
                            }
                            else
                            {
                                _locomotionState.Value = (byte)LocomotionState.WallSlide;
                                _verticalVelocity = Mathf.Max(_verticalVelocity, -wallSlideMaxFallSpeed);
                            }
                        }
                        else
                        {
                            _locomotionState.Value = lowGravity
                                ? (byte)LocomotionState.LowGravityFloat
                                : (byte)LocomotionState.Airborne;
                        }
                    }
                    else
                    {
                        _supportNormal.Value = Vector3.up;
                        _locomotionState.Value = lowGravity
                            ? (byte)LocomotionState.LowGravityFloat
                            : (byte)LocomotionState.Airborne;
                    }

                    if (!consumedJump)
                    {
                        _verticalVelocity += -GetGravity(lowGravity) * deltaTime;
                    }
                }
            }

            if (!grounded && _ragdollTimeRemaining <= 0f && _ragdollRecoveryTimeRemaining <= 0f)
            {
                float airborneSpeed = new Vector3(_horizontalVelocity.x, 0f, _horizontalVelocity.z).magnitude;
                if (airborneSpeed >= airborneRagdollSpeedThreshold)
                {
                    EnterRagdoll(ragdollMinDurationSeconds);
                }
            }

            _serverJumpRequested = false;

            Vector3 displacement = (_horizontalVelocity * deltaTime) + (Vector3.up * (_verticalVelocity * deltaTime));
            Vector3 previousPosition = transform.position;
            Vector3 nextPosition = MoveCharacter(transform.position, displacement, out bool hitGroundBySweep, out Vector3 groundNormalFromSweep);

            transform.SetPositionAndRotation(nextPosition, Quaternion.Euler(0f, _serverVisualYaw, 0f));

            RaycastHit refreshGroundHit = default;
            bool groundedAfterMove = hitGroundBySweep;

            if (!groundedAfterMove)
            {
                groundedAfterMove = IsGrounded(nextPosition, out refreshGroundHit);
            }

            if (_staminaState != null)
            {
                if (burstAllowed && desiredMoveDir.sqrMagnitude > 0.05f)
                {
                    _staminaState.ServerTryConsumeBurst(deltaTime);
                }
                else
                {
                    bool idle = desiredMoveDir.sqrMagnitude <= 0.01f && groundedAfterMove;
                    _staminaState.ServerRegenerate(deltaTime, groundedAfterMove, idle);
                }
            }

            Vector3 groundNormal = hitGroundBySweep
                ? groundNormalFromSweep
                : (groundedAfterMove ? refreshGroundHit.normal : Vector3.up);

            if (groundedAfterMove)
            {
                _supportNormal.Value = groundNormal;
            }

            float landingImpact = Mathf.Abs(_verticalVelocity);
            if (!_wasGroundedLastFrame && groundedAfterMove && _verticalVelocity <= 0f)
            {
                _lastLandingImpact.Value = landingImpact;
                _verticalVelocity = -stickToGroundVelocity;
                if (landingImpact > 5f)
                {
                    EmitPaint(PaintEventKind.Land, nextPosition + (Vector3.down * 0.45f), groundNormal, 0.35f + (landingImpact * 0.02f), landingImpact * 0.1f, landingImpact, Vector3.down);
                }

                if (CurrentState == LocomotionState.Ragdoll && landingImpact > 4.5f)
                {
                    EmitPaint(PaintEventKind.RagdollImpact, nextPosition + (Vector3.down * 0.45f), groundNormal, 0.55f, 1.1f, landingImpact * 1.2f, _authoritativeVelocity.Value.normalized, PaintSplatPermanence.Permanent);
                }
            }
            else
            {
                _lastLandingImpact.Value = Mathf.MoveTowards(_lastLandingImpact.Value, 0f, deltaTime * 12f);
            }

            if (groundedAfterMove)
            {
                _coyoteTimeRemaining = coyoteTimeSeconds;
            }

            _wallCompression01.Value = hasWall
                ? Mathf.Clamp01(Vector3.Dot(_authoritativeVelocity.Value, -wallHit.normal) / Mathf.Max(0.1f, moveSpeed + 3f))
                : Mathf.MoveTowards(_wallCompression01.Value, 0f, deltaTime * 5f);

            if ((_locomotionState.Value == (byte)LocomotionState.Grounded || _locomotionState.Value == (byte)LocomotionState.LowGravityFloat) &&
                (_serverMoveWorldInput.sqrMagnitude > 0.2f) && Time.time >= _nextMovementPaintTime)
            {
                _nextMovementPaintTime = Time.time + (lowGravity ? 0.12f : 0.2f);
                Vector3 direction = (nextPosition - previousPosition).sqrMagnitude > 0.001f
                    ? (nextPosition - previousPosition).normalized
                    : transform.forward;
                EmitPaint(PaintEventKind.Move, nextPosition + (Vector3.down * 0.48f), Vector3.up, lowGravity ? 0.2f : 0.14f, lowGravity ? 0.9f : 0.45f, _horizontalVelocity.magnitude, direction);
                EmitPaint(PaintEventKind.Move, nextPosition + (direction * 0.25f) + (Vector3.down * 0.48f), Vector3.up, 0.11f, 0.4f, _horizontalVelocity.magnitude * 0.8f, direction);
            }

            _wasGroundedLastFrame = groundedAfterMove;
            _authoritativePosition.Value = transform.position;
            _authoritativeYaw.Value = _serverVisualYaw;
            _authoritativeVelocity.Value = _horizontalVelocity + (Vector3.up * _verticalVelocity);

            if (logJumpStateTransitions && (_jumpBufferTimeRemaining > 0f || _coyoteTimeRemaining > 0f))
            {
                Debug.Log($"[{name}] grounded={groundedAfterMove} coyote={_coyoteTimeRemaining:F3} jumpBuffer={_jumpBufferTimeRemaining:F3} state={(LocomotionState)_locomotionState.Value} vel={_authoritativeVelocity.Value}");
            }
        }

        private void SimulateRagdoll(float deltaTime, bool lowGravity, bool hasWall, RaycastHit wallHit)
        {
            _ragdollTimeRemaining = Mathf.Max(0f, _ragdollTimeRemaining - deltaTime);
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, Vector3.zero, ragdollHorizontalDamping * deltaTime);
            _verticalVelocity += -(GetGravity(lowGravity) * ragdollGravityMultiplier) * deltaTime;
            _locomotionState.Value = (byte)LocomotionState.Ragdoll;
            _supportNormal.Value = hasWall ? wallHit.normal : Vector3.up;

            if (_ragdollTimeRemaining <= 0f)
            {
                _ragdollRecoveryTimeRemaining = ragdollRecoveryLockSeconds;
                _locomotionState.Value = (byte)LocomotionState.Knockback;
            }
        }

        private void ApplyBlobDeformation(float deltaTime)
        {
            if (blobVisualRoot == null)
            {
                return;
            }

            Vector3 velocity = _authoritativeVelocity.Value;
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            float horizontalSpeed = horizontalVelocity.magnitude;
            float verticalSpeedAbs = Mathf.Abs(velocity.y);

            Vector3 acceleration = (velocity - _lastVisualVelocity) / Mathf.Max(0.0001f, deltaTime);
            _lastVisualVelocity = velocity;

            _blobLandingImpulseVisual = Mathf.Max(_blobLandingImpulseVisual, _lastLandingImpact.Value * landingSquashStrength * 0.03f);
            _blobLandingImpulseVisual = Mathf.MoveTowards(_blobLandingImpulseVisual, 0f, deltaTime * 2.8f);

            Vector3 localMove = transform.InverseTransformDirection(horizontalVelocity.normalized);
            if (horizontalSpeed < 0.01f)
            {
                localMove = Vector3.forward;
            }

            float forwardStretch = 1f + (horizontalSpeed * stretchByHorizontalSpeed) + (verticalSpeedAbs * stretchByVerticalSpeed);
            forwardStretch += Vector3.Dot(acceleration, velocity.normalized) * accelerationStretch;
            forwardStretch = Mathf.Clamp(forwardStretch, stretchClamp.x, stretchClamp.y);

            float squatBase = 1f / Mathf.Sqrt(Mathf.Max(0.01f, forwardStretch));
            float airborneSquash = CurrentState is LocomotionState.Airborne or LocomotionState.LowGravityFloat or LocomotionState.WallLaunch
                ? airborneSquashAmount
                : 0f;
            float wallCompress = _wallCompression01.Value * wallCompressionStrength;
            float landingSquash = _blobLandingImpulseVisual;
            float sideScale = Mathf.Clamp(squatBase + landingSquash + airborneSquash + wallCompress, squashClamp.x, squashClamp.y);
            float upScale = Mathf.Clamp((squatBase - airborneSquash) + landingSquash + (wallCompress * 0.6f), squashClamp.x, squashClamp.y);

            Vector3 targetLocalScale = _blobBaseScale;
            targetLocalScale.x *= Mathf.Lerp(sideScale, sideScale - (wallCompress * 0.2f), Mathf.Clamp01(Mathf.Abs(localMove.x)));
            targetLocalScale.y *= upScale;
            targetLocalScale.z *= forwardStretch;

            _blobCurrentScale = Vector3.Lerp(_blobCurrentScale, targetLocalScale, deformationLerpSpeed * deltaTime);
            blobVisualRoot.localScale = _blobCurrentScale;
        }

        private void TryResolvePunch()
        {
            if (!_serverPunchRequested)
            {
                return;
            }

            _serverPunchRequested = false;
            if (GetServerTime() < _punchCooldownEndServerTime.Value)
            {
                return;
            }

            if (TryFindPunchTarget(out NetworkPlayerAuthoritativeMover targetMover, out Vector3 hitPoint))
            {
                Vector3 direction = (targetMover.transform.position - transform.position).normalized;
                Vector3 impulse = (direction * knockbackImpulse) + (Vector3.up * knockbackUpwardBoost);
                targetMover.ServerApplyKnockback(impulse);
                _punchCooldownEndServerTime.Value = GetServerTime() + punchCooldownSeconds;
                EmitPaint(PaintEventKind.Punch, hitPoint, -direction, 0.48f, 1f, impulse.magnitude, direction, PaintSplatPermanence.Permanent);
            }
            else
            {
                _punchCooldownEndServerTime.Value = GetServerTime() + (punchCooldownSeconds * 0.5f);
            }
        }

        private bool TryFindPunchTarget(out NetworkPlayerAuthoritativeMover targetMover, out Vector3 hitPoint)
        {
            targetMover = null;
            hitPoint = transform.position + (transform.forward * punchRange);

            Vector3 origin = transform.position + (Vector3.up * 0.9f);
            Vector3 center = origin + (transform.forward * (punchRange * 0.65f));
            Collider[] overlaps = Physics.OverlapSphere(center, punchRadius, punchMask, QueryTriggerInteraction.Ignore);
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider candidate = overlaps[i];
                if (candidate == null || candidate.transform.IsChildOf(transform))
                {
                    continue;
                }

                NetworkPlayerAuthoritativeMover mover = candidate.GetComponentInParent<NetworkPlayerAuthoritativeMover>();
                if (mover == null || mover == this)
                {
                    continue;
                }

                PlayerLifeState lifeState = mover.GetComponent<PlayerLifeState>();
                if (lifeState != null && !lifeState.IsAlive)
                {
                    continue;
                }

                Vector3 targetPoint = mover.transform.position + (Vector3.up * 0.8f);
                Vector3 toTarget = targetPoint - origin;
                float distance = toTarget.magnitude;
                if (distance > punchRange)
                {
                    continue;
                }

                if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit losHit, distance + 0.1f, punchMask, QueryTriggerInteraction.Ignore) &&
                    !losHit.transform.IsChildOf(mover.transform))
                {
                    continue;
                }

                float distanceSqr = toTarget.sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    targetMover = mover;
                    hitPoint = Vector3.Lerp(origin, targetPoint, 0.8f);
                }
            }

            return targetMover != null;
        }

        private bool TryFindWall(Vector3 position, out RaycastHit wallHit)
        {
            GetCapsuleWorldPoints(position, out Vector3 point1, out Vector3 point2, out float radius);

            Vector3[] directions =
            {
                Vector3.ProjectOnPlane(_horizontalVelocity, Vector3.up).normalized,
                Vector3.ProjectOnPlane(_serverMoveWorldInput, Vector3.up).normalized,
                Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized,
                Vector3.ProjectOnPlane(-transform.right, Vector3.up).normalized,
                Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized
            };

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 direction = directions[i];
                if (direction.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                if (Physics.CapsuleCast(point1, point2, radius * 0.95f, direction, out wallHit, wallDetectionDistance, groundMask, QueryTriggerInteraction.Ignore))
                {
                    if (wallHit.normal.y < Mathf.Cos(maxGroundSlopeAngle * Mathf.Deg2Rad))
                    {
                        return true;
                    }
                }
            }

            wallHit = default;
            return false;
        }

        private float GetGravity(bool lowGravity)
        {
            return gravityMagnitude * (lowGravity ? lowGravityMultiplier : 1f);
        }

        private bool IsInLowGravityZone()
        {
            if (_floodTracker == null)
            {
                return false;
            }

            FloodZoneState state = _floodTracker.CurrentZoneState;
            return state is FloodZoneState.Flooding or FloodZoneState.Submerged;
        }

        private void EmitPaint(
            PaintEventKind kind,
            Vector3 position,
            Vector3 normal,
            float radius,
            float intensity,
            float forceMagnitude = -1f,
            Vector3? velocityDirection = null,
            PaintSplatPermanence permanence = PaintSplatPermanence.Temporary)
        {
            if (_paintEmitter == null)
            {
                return;
            }

            Vector3 direction = velocityDirection ?? (_authoritativeVelocity.Value.sqrMagnitude > 0.001f ? _authoritativeVelocity.Value.normalized : transform.forward);
            float resolvedForce = forceMagnitude >= 0f ? forceMagnitude : _authoritativeVelocity.Value.magnitude;
            _paintEmitter.ServerEmitPaint(
                kind,
                position,
                normal,
                radius,
                intensity,
                resolvedForce,
                direction,
                ResolveSplatType(kind),
                permanence,
                -1);
        }

        private static PaintSplatType ResolveSplatType(PaintEventKind kind)
        {
            return kind switch
            {
                PaintEventKind.Move => PaintSplatType.Footstep,
                PaintEventKind.Land => PaintSplatType.Landing,
                PaintEventKind.WallStick => PaintSplatType.WallImpact,
                PaintEventKind.WallLaunch => PaintSplatType.WallImpact,
                PaintEventKind.Punch => PaintSplatType.Punch,
                PaintEventKind.RagdollImpact => PaintSplatType.RagdollImpact,
                PaintEventKind.TaskInteract => PaintSplatType.TaskInteract,
                PaintEventKind.ThrownObjectImpact => PaintSplatType.ThrownObject,
                _ => PaintSplatType.Generic
            };
        }

        private void EnterRagdoll(float duration)
        {
            _ragdollTimeRemaining = Mathf.Max(_ragdollTimeRemaining, Mathf.Max(ragdollMinDurationSeconds, duration));
            _ragdollRecoveryTimeRemaining = 0f;
            _wallStickTimeRemaining = 0f;
            _wallLaunchControlTimeRemaining = 0f;
            _wallDetachTimeRemaining = wallDetachGraceSeconds;
            _locomotionState.Value = (byte)LocomotionState.Ragdoll;
        }

        private Vector3 MoveCharacter(Vector3 startPosition, Vector3 displacement, out bool hitGround, out Vector3 groundNormal)
        {
            hitGround = false;
            groundNormal = Vector3.up;
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
                if (IsGroundNormal(normal))
                {
                    hitGround = true;
                    groundNormal = normal;
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

        private bool IsGrounded(Vector3 position, out RaycastHit groundHit)
        {
            GetCapsuleWorldPoints(position, out Vector3 point1, out Vector3 point2, out float radius);
            float probeRadius = Mathf.Min(radius * 0.95f, Mathf.Max(0.05f, groundCheckRadius));
            Vector3 bottom = point1.y <= point2.y ? point1 : point2;
            Vector3 origin = bottom + (Vector3.up * 0.05f);
            float probeDistance = Mathf.Max(0.05f, groundCheckDistance + 0.05f);

            if (Physics.SphereCast(origin, probeRadius, Vector3.down, out groundHit, probeDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                return IsGroundNormal(groundHit.normal);
            }

            return false;
        }

        private bool IsGroundNormal(Vector3 normal)
        {
            return normal.y >= Mathf.Cos(maxGroundSlopeAngle * Mathf.Deg2Rad);
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
            bool jumpPressed = _inputReader.JumpPressedThisFrame;
            bool punchPressed = _inputReader.PunchPressedThisFrame;
            bool burstHeld = _inputReader.BurstHeld;

            bool shouldSend = (move - _lastSentMoveWorldInput).sqrMagnitude > 0.0001f ||
                              Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentVisualYaw)) > 0.5f ||
                              jumpPressed || punchPressed || burstHeld != _lastSentBurstHeld;

            if (!shouldSend)
            {
                return;
            }

            _lastSentMoveWorldInput = move;
            _lastSentVisualYaw = yaw;
            _lastSentBurstHeld = burstHeld;
            SubmitInputServerRpc(move, yaw, jumpPressed, punchPressed, burstHeld);
        }

        [ServerRpc]
        private void SubmitInputServerRpc(Vector3 moveWorldInput, float visualYaw, bool jumpPressed, bool punchPressed, bool burstHeld, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            _serverMoveWorldInput = Vector3.ClampMagnitude(moveWorldInput, 1f);
            _serverVisualYaw = visualYaw;
            _serverJumpRequested |= jumpPressed;
            _serverPunchRequested |= punchPressed;
            _serverBurstHeld = burstHeld;
        } 

        private float GetServerTime()
        {
            return NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.unscaledTime;
        }

        private void ResolveRoundState()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            if (_capsuleCollider != null)
            {
                GetCapsuleWorldPoints(transform.position, out Vector3 point1, out Vector3 point2, out float radius);
                Vector3 bottom = point1.y <= point2.y ? point1 : point2;
                Vector3 origin = bottom + (Vector3.up * 0.05f);
                Gizmos.DrawWireSphere(origin + (Vector3.down * groundCheckDistance), Mathf.Min(radius * 0.95f, Mathf.Max(0.05f, groundCheckRadius)));
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position + (Vector3.up * 0.8f), _supportNormal.Value.normalized * 0.9f);

            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(transform.position + (Vector3.up * 1.0f), _authoritativeVelocity.Value.normalized * 1.2f);
        }
    }
}
