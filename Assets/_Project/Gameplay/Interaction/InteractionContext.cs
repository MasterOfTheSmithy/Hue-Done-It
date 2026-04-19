// File: Assets/_Project/Gameplay/Interaction/InteractionContext.cs
using Unity.Netcode;

namespace HueDoneIt.Gameplay.Interaction
{
    public readonly struct InteractionContext
    {
        public InteractionContext(NetworkObject interactorObject, ulong interactorClientId, bool isServer)
        {
            InteractorObject = interactorObject;
            InteractorClientId = interactorClientId;
            IsServer = isServer;
        }

        public NetworkObject InteractorObject { get; }
        public ulong InteractorClientId { get; }
        public bool IsServer { get; }
    }
}
