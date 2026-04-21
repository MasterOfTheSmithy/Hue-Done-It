// File: Assets/_Project/Gameplay/Players/NetworkPlayerInputReader.cs
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayerInputReader : NetworkBehaviour
    {
        [SerializeField, Min(0.01f)] private float jumpPressBufferSeconds = 0.15f;
        [SerializeField, Min(0.01f)] private float punchPressBufferSeconds = 0.12f;

        private bool _loggedMissingKeyboard;
        private float _jumpPressBufferRemaining;
        private float _punchPressBufferRemaining;

        public Vector2 CurrentMoveInput { get; private set; }
        public Vector3 CurrentWorldMoveInput { get; private set; }

        // Non-consuming state for client send path.
        public bool JumpPressedThisFrame => _jumpPressBufferRemaining > 0f;
        public bool PunchPressedThisFrame => _punchPressBufferRemaining > 0f;

        public bool BurstHeld { get; private set; }
        public float CurrentVisualYaw => transform.eulerAngles.y;

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                CurrentMoveInput = Vector2.zero;
                CurrentWorldMoveInput = Vector3.zero;
                BurstHeld = false;
                _jumpPressBufferRemaining = 0f;
                _punchPressBufferRemaining = 0f;
                return;
            }

            CurrentMoveInput = ReadMoveInput();
            CurrentWorldMoveInput = ResolveWorldMove(CurrentMoveInput);
            ReadJumpPressed();
            ReadPunchPressed();
            BurstHeld = ReadBurstHeld();

            if (_jumpPressBufferRemaining > 0f)
            {
                _jumpPressBufferRemaining = Mathf.Max(0f, _jumpPressBufferRemaining - Time.deltaTime);
            }

            if (_punchPressBufferRemaining > 0f)
            {
                _punchPressBufferRemaining = Mathf.Max(0f, _punchPressBufferRemaining - Time.deltaTime);
            }
        }

        public bool ConsumeJumpPressedThisFrame()
        {
            if (_jumpPressBufferRemaining <= 0f)
            {
                return false;
            }

            _jumpPressBufferRemaining = 0f;
            return true;
        }

        public bool ConsumePunchPressedThisFrame()
        {
            if (_punchPressBufferRemaining <= 0f)
            {
                return false;
            }

            _punchPressBufferRemaining = 0f;
            return true;
        }

        private Vector2 ReadMoveInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                if (!_loggedMissingKeyboard)
                {
                    Debug.LogWarning("Keyboard input is unavailable for NetworkPlayerInputReader.");
                    _loggedMissingKeyboard = true;
                }

                return Vector2.zero;
            }

            _loggedMissingKeyboard = false;

            float x = 0f;
            float y = 0f;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;

            Vector2 input = new Vector2(x, y);
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            return input;
        }

        private void ReadJumpPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                _jumpPressBufferRemaining = jumpPressBufferSeconds;
            }
        }

        private void ReadPunchPressed()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                _punchPressBufferRemaining = punchPressBufferSeconds;
            }
        }

        private static bool ReadBurstHeld()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }

        private Vector3 ResolveWorldMove(Vector2 input)
        {
            if (input == Vector2.zero)
            {
                return Vector3.zero;
            }

            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            right.y = 0f;
            forward.y = 0f;
            right.Normalize();
            forward.Normalize();

            Vector3 world = (right * input.x) + (forward * input.y);
            if (world.sqrMagnitude > 1f)
            {
                world.Normalize();
            }

            return world;
        }
    }
}