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

        public Vector2 CurrentMoveInput { get; private set; }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                CurrentMoveInput = Vector2.zero;
                return;
            }

            CurrentMoveInput = ReadMoveInput();
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
    }
}
