// File: Assets/_Project/Gameplay/Interaction/NetworkInteractable.cs
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public abstract class NetworkInteractable : NetworkBehaviour, IInteractable
    {
        [SerializeField, Min(0.1f)] private float maxUseDistance = 2.5f;

        private Collider[] _interactionColliders;

        public float MaxUseDistance => maxUseDistance;

        protected virtual void Awake()
        {
            CacheInteractionColliders();
        }

        protected virtual void OnValidate()
        {
            maxUseDistance = Mathf.Max(0.1f, maxUseDistance);
            CacheInteractionColliders();
        }

        public abstract bool CanInteract(in InteractionContext context);
        public abstract string GetPromptText(in InteractionContext context);
        public abstract bool TryInteract(in InteractionContext context);

        public Vector3 GetInteractionPoint(Vector3 fromPosition)
        {
            CacheInteractionColliders();

            Vector3 fallbackPoint = transform.position + (Vector3.up * 0.9f);
            if (_interactionColliders == null || _interactionColliders.Length == 0)
            {
                return fallbackPoint;
            }

            float bestDistanceSquared = float.MaxValue;
            Vector3 bestPoint = fallbackPoint;
            bool foundValidCollider = false;

            for (int i = 0; i < _interactionColliders.Length; i++)
            {
                Collider colliderRef = _interactionColliders[i];
                if (colliderRef == null || !colliderRef.enabled || colliderRef.isTrigger)
                {
                    continue;
                }

                Vector3 candidate = colliderRef.ClosestPoint(fromPosition);
                float distanceSquared = (candidate - fromPosition).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestPoint = candidate;
                    foundValidCollider = true;
                }
            }

            return foundValidCollider ? bestPoint : fallbackPoint;
        }

        private void CacheInteractionColliders()
        {
            _interactionColliders = GetComponentsInChildren<Collider>(true);
        }
    }
}
