// File: Assets/_Project/Gameplay/Players/LocalPlayerCameraBinder.cs
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerAuthoritativeMover))]
    public sealed class LocalPlayerCameraBinder : NetworkBehaviour
    {
        [Header("Base Camera")]
        [SerializeField] private Transform cameraAnchor;
        [SerializeField] private Vector3 ownerCameraAnchorLocalPosition = new(0f, 0.75f, 0f);
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;
        [SerializeField] private bool lockCursorOnStart = true;

        [Header("Blob Motion Presentation")]
        [SerializeField, Min(0f)] private float velocityLagAmount = 0.03f;
        [SerializeField, Min(0f)] private float lateralSwayAmount = 0.025f;
        [SerializeField, Min(0f)] private float verticalBobAmount = 0.018f;
        [SerializeField, Min(0.1f)] private float effectLerpSpeed = 10f;
        [SerializeField, Min(0f)] private float landingCompressionAmount = 0.045f;
        [SerializeField, Min(0.1f)] private float landingCompressionRecoverSpeed = 8f;
        [SerializeField, Min(0f)] private float speedPositionKick = 0.015f;
        [SerializeField, Min(0f)] private float wallCompressionCameraPush = 0.03f;

        [Header("FOV")]
        [SerializeField, Min(1f)] private float baseFieldOfView = 74f;
        [SerializeField, Min(0f)] private float speedFieldOfViewBoost = 7f;
        [SerializeField, Min(0.1f)] private float fieldOfViewLerpSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float speedForMaxFovBoost = 14f;

        [Header("Gravity / Wall Tilt")]
        [SerializeField, Min(0f)] private float airborneRollAmount = 4f;
        [SerializeField, Min(0f)] private float wallRollAmount = 9f;
        [SerializeField, Min(0f)] private float movementYawLeanAmount = 1.3f;
        [SerializeField, Min(0f)] private float wallLaunchImpulseRoll = 4f;
        [SerializeField, Min(0.1f)] private float wallLaunchImpulseRecoverSpeed = 7.5f;
        [SerializeField, Min(0.1f)] private float rollLerpSpeed = 7f;
        [SerializeField, Min(0f)] private float ragdollCameraChaosRoll = 9f;
        [SerializeField, Min(0f)] private float ragdollCameraChaosPositional = 0.055f;

        private Camera _mainCamera;
        private float _pitch;
        private MeshRenderer[] _ownerRenderers;
        private bool _cameraBound;
        private Vector3 _currentPresentationOffset;
        private float _currentLandingCompression;
        private float _currentRoll;
        private Vector3 _lastVelocity;
        private NetworkPlayerAuthoritativeMover _mover;
        private NetworkPlayerAuthoritativeMover.LocomotionState _lastState;
        private float _wallLaunchRollKick;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _mover = GetComponent<NetworkPlayerAuthoritativeMover>();

            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            BindOwnerCamera();
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                LockCursor(false);
            }

            _cameraBound = false;
            base.OnNetworkDespawn();
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                return;
            }

            if (!_cameraBound)
            {
                BindOwnerCamera();
                if (!_cameraBound)
                {
                    return;
                }
            }

            HandleMouseLook();
            ApplyBlobPresentation();
        }

        private void HandleMouseLook()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                LockCursor(false);
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                LockCursor(true);
            }

            if (mouse == null || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
        }

        private void ApplyBlobPresentation()
        {
            if (cameraAnchor == null)
            {
                return;
            }

            Vector3 worldVelocity = _mover != null ? _mover.CurrentVelocity : Vector3.zero;
            Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);
            Vector3 acceleration = (worldVelocity - _lastVelocity) / Mathf.Max(Time.deltaTime, 0.001f);
            Vector3 localAcceleration = transform.InverseTransformDirection(acceleration);
            _lastVelocity = worldVelocity;

            float speed = new Vector3(worldVelocity.x, 0f, worldVelocity.z).magnitude;
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.1f, speedForMaxFovBoost));

            NetworkPlayerAuthoritativeMover.LocomotionState currentState = _mover != null
                ? _mover.CurrentState
                : NetworkPlayerAuthoritativeMover.LocomotionState.Airborne;

            bool landed = _lastState != NetworkPlayerAuthoritativeMover.LocomotionState.Grounded &&
                          currentState == NetworkPlayerAuthoritativeMover.LocomotionState.Grounded;
            if (landed)
            {
                float landingStrength = Mathf.Clamp01((_mover != null ? _mover.LastLandingImpact : Mathf.Abs(localVelocity.y)) / 12f);
                _currentLandingCompression = Mathf.Max(_currentLandingCompression, landingCompressionAmount * (0.45f + landingStrength));
            }

            if (_lastState != NetworkPlayerAuthoritativeMover.LocomotionState.WallLaunch &&
                currentState == NetworkPlayerAuthoritativeMover.LocomotionState.WallLaunch)
            {
                float signed = Mathf.Sign(localVelocity.x == 0f ? 1f : localVelocity.x);
                _wallLaunchRollKick = wallLaunchImpulseRoll * signed;
            }

            _currentLandingCompression = Mathf.MoveTowards(_currentLandingCompression, 0f, landingCompressionRecoverSpeed * Time.deltaTime);
            _wallLaunchRollKick = Mathf.MoveTowards(_wallLaunchRollKick, 0f, wallLaunchImpulseRecoverSpeed * Time.deltaTime);

            float wallCompression = _mover != null ? _mover.WallCompression : 0f;
            Vector3 targetOffset = new Vector3(
                (-localVelocity.x * lateralSwayAmount) + (-localAcceleration.x * 0.0015f),
                (-Mathf.Max(0f, worldVelocity.y) * verticalBobAmount * 0.15f) - _currentLandingCompression,
                (-localVelocity.z * velocityLagAmount) - (Mathf.Abs(localAcceleration.z) * 0.0008f) - (speed01 * speedPositionKick) + (wallCompression * wallCompressionCameraPush));

            if (currentState == NetworkPlayerAuthoritativeMover.LocomotionState.Ragdoll)
            {
                float noiseT = Time.time * 7.5f;
                targetOffset += new Vector3(
                    (Mathf.PerlinNoise(noiseT, 0f) - 0.5f) * ragdollCameraChaosPositional,
                    (Mathf.PerlinNoise(0f, noiseT) - 0.5f) * ragdollCameraChaosPositional,
                    0f);
            }

            _currentPresentationOffset = Vector3.Lerp(_currentPresentationOffset, targetOffset, effectLerpSpeed * Time.deltaTime);

            float targetRoll = 0f;
            if (currentState is NetworkPlayerAuthoritativeMover.LocomotionState.WallStick or
                NetworkPlayerAuthoritativeMover.LocomotionState.WallSlide or
                NetworkPlayerAuthoritativeMover.LocomotionState.WallLaunch)
            {
                Vector3 supportNormal = _mover != null ? _mover.CurrentSupportNormal : Vector3.up;
                Vector3 localSupport = transform.InverseTransformDirection(supportNormal.normalized);
                targetRoll = Mathf.Clamp(localSupport.x * wallRollAmount, -wallRollAmount, wallRollAmount);
            }
            else if (currentState is NetworkPlayerAuthoritativeMover.LocomotionState.Airborne or
                     NetworkPlayerAuthoritativeMover.LocomotionState.Knockback or
                     NetworkPlayerAuthoritativeMover.LocomotionState.LowGravityFloat)
            {
                targetRoll = Mathf.Clamp(localVelocity.x * 0.45f, -airborneRollAmount, airborneRollAmount);
            }
            else if (currentState == NetworkPlayerAuthoritativeMover.LocomotionState.Ragdoll)
            {
                targetRoll = Mathf.Sin(Time.time * 13.5f) * ragdollCameraChaosRoll;
            }

            float yawLean = Mathf.Clamp(localVelocity.x * movementYawLeanAmount, -movementYawLeanAmount, movementYawLeanAmount);
            targetRoll += _wallLaunchRollKick;

            _currentRoll = Mathf.Lerp(_currentRoll, targetRoll, rollLerpSpeed * Time.deltaTime);

            cameraAnchor.localPosition = ownerCameraAnchorLocalPosition + _currentPresentationOffset;
            cameraAnchor.localRotation = Quaternion.Euler(_pitch, yawLean, _currentRoll);

            if (_mainCamera != null)
            {
                float targetFov = baseFieldOfView + (speedFieldOfViewBoost * speed01);
                if (currentState == NetworkPlayerAuthoritativeMover.LocomotionState.Ragdoll)
                {
                    targetFov += 2f;
                }

                _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, targetFov, fieldOfViewLerpSpeed * Time.deltaTime);
            }

            _lastState = currentState;
        }

        private void BindOwnerCamera()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                return;
            }

            EnsureCameraAnchor();
            cameraAnchor.localPosition = ownerCameraAnchorLocalPosition;
            cameraAnchor.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            Transform cameraTransform = _mainCamera.transform;
            cameraTransform.SetParent(cameraAnchor, false);
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = Quaternion.identity;
            _mainCamera.fieldOfView = baseFieldOfView;

            _ownerRenderers ??= GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in _ownerRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            _cameraBound = true;
            if (lockCursorOnStart)
            {
                LockCursor(true);
            }
        }

        private void EnsureCameraAnchor()
        {
            if (cameraAnchor != null)
            {
                return;
            }

            GameObject anchorObject = new(nameof(LocalPlayerCameraBinder) + "_Anchor");
            anchorObject.transform.SetParent(transform, false);
            cameraAnchor = anchorObject.transform;
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
