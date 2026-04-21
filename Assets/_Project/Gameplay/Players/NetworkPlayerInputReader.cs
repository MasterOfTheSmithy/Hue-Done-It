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
        private bool _loggedMissingKeyboard;
        private bool _jumpConsumed;
        private bool _punchConsumed;

        public Vector2 CurrentMoveInput { get; private set; }
        public Vector3 CurrentWorldMoveInput { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool PunchPressedThisFrame { get; private set; }
        public bool BurstHeld { get; private set; }
        public float CurrentVisualYaw => transform.eulerAngles.y;

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                CurrentMoveInput = Vector2.zero;
                CurrentWorldMoveInput = Vector3.zero;
                JumpPressedThisFrame = false;
                PunchPressedThisFrame = false;
                BurstHeld = false;
                return;
            }

            CurrentMoveInput = ReadMoveInput();
            CurrentWorldMoveInput = ResolveWorldMove(CurrentMoveInput);
            JumpPressedThisFrame = ReadJumpPressed();
            PunchPressedThisFrame = ReadPunchPressed();
            BurstHeld = ReadBurstHeld();
        }

        public bool ConsumeJumpPressedThisFrame()
        {
            if (_jumpConsumed || !JumpPressedThisFrame)
            {
                return false;
            }

            _jumpConsumed = true;
            return true;
        }

        public bool ConsumePunchPressedThisFrame()
        {
            if (_punchConsumed || !PunchPressedThisFrame)
            {
                return false;
            }

            _punchConsumed = true;
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

            Vector2 input = new(x, y);
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            return input;
        }

        private bool ReadJumpPressed()
        {
            Keyboard keyboard = Keyboard.current;
            bool pressed = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
            _jumpConsumed = false;
            return pressed;
        }

        private bool ReadPunchPressed()
        {
            Mouse mouse = Mouse.current;
            bool pressed = mouse != null && mouse.rightButton.wasPressedThisFrame;
            _punchConsumed = false;
            return pressed;
        }

        private static bool ReadBurstHeld()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.leftShiftKey.isPressed;
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
