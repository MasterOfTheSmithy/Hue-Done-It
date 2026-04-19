// File: Assets/_Project/Gameplay/Interaction/SampleConsoleInteractable.cs
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Interaction
{
    public sealed class SampleConsoleInteractable : NetworkInteractable
    {
        [SerializeField] private string availablePrompt = "Press [E] Use Console";
        [SerializeField] private string unavailablePrompt = "Console Already Used";

        private readonly NetworkVariable<bool> _isAvailable =
            new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public override bool CanInteract(in InteractionContext context)
        {
            return _isAvailable.Value;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            return _isAvailable.Value ? availablePrompt : unavailablePrompt;
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!IsServer || !_isAvailable.Value)
            {
                return false;
            }

            _isAvailable.Value = false;
            Debug.Log($"SampleConsoleInteractable used by client {context.InteractorClientId}.");
            return true;
        }
    }
}
