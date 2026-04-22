// File: Assets/_Project/Gameplay/Lobby/LobbyMatchControlInteractable.cs
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.UI.Lobby;
using Unity.Netcode;

namespace HueDoneIt.Gameplay.Lobby
{
    // This in-world interactable opens the lobby match control HUD for the requesting client.
    public sealed class LobbyMatchControlInteractable : NetworkInteractable
    {
        public override bool CanInteract(in InteractionContext context)
        {
            return true;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            return "Open Lobby Match Controls";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!IsServer)
            {
                return false;
            }

            LobbyHudController.ShowMatchPanelClient(context.InteractorClientId);
            return true;
        }
    }
}
