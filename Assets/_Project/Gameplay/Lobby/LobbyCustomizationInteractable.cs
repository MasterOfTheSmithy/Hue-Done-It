// File: Assets/_Project/Gameplay/Lobby/LobbyCustomizationInteractable.cs
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.UI.Lobby;
using Unity.Netcode;

namespace HueDoneIt.Gameplay.Lobby
{
    // This in-world interactable opens the player customization HUD for the requesting client.
    public sealed class LobbyCustomizationInteractable : NetworkInteractable
    {
        public override bool CanInteract(in InteractionContext context)
        {
            return true;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            return "Open Customization Hub";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!IsServer)
            {
                return false;
            }

            LobbyHudController.ShowCustomizationPanelClient(context.InteractorClientId);
            return true;
        }
    }
}
