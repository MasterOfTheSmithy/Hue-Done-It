// File: Assets/_Project/Gameplay/Interaction/InteractionContext.cs
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Interaction
{
    public readonly struct InteractionContext
    {
        public InteractionContext(NetworkObject interactorObject, ulong interactorClientId, bool isServer)
        {
            InteractorObject = interactorObject;
            InteractorClientId = interactorClientId;
            IsServer = isServer;
            InteractorTransform = interactorObject != null ? interactorObject.transform : null;
        }

        public NetworkObject InteractorObject { get; }
        public ulong InteractorClientId { get; }
        public bool IsServer { get; }
        public Transform InteractorTransform { get; }
    }
}
