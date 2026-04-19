// File: Assets/_Project/Gameplay/Interaction/NetworkInteractable.cs
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public abstract class NetworkInteractable : NetworkBehaviour, IInteractable
    {
        [SerializeField] private float maxUseDistance = 2.5f;

        public float MaxUseDistance => maxUseDistance;

        public abstract bool CanInteract(in InteractionContext context);
        public abstract string GetPromptText(in InteractionContext context);
        public abstract bool TryInteract(in InteractionContext context);
    }
}
