// File: Assets/_Project/Gameplay/Players/LocalPlayerCameraBinder.cs
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    public sealed class LocalPlayerCameraBinder : NetworkBehaviour
    {
        [SerializeField] private Transform cameraAnchor;
        [SerializeField] private Vector3 ownerCameraAnchorLocalPosition = new(0f, 0.75f, 0f);
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;
        [SerializeField] private bool lockCursorOnStart = true;

        private Camera _mainCamera;
        private float _pitch;
        private MeshRenderer[] _ownerRenderers;
        private bool _cameraBound;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

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
            cameraAnchor.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
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
