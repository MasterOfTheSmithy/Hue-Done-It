// File: Assets/_Project/Gameplay/Interaction/PlayerInteractionDetector.cs
using System;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerInteractionDetector : NetworkBehaviour
    {
        [SerializeField] private float interactionRange = 2.5f;
        [SerializeField] private LayerMask detectionMask = ~0;
        [SerializeField] private int overlapBufferSize = 16;

        private Collider[] _overlapResults;
        private NetworkInteractable _currentInteractable;

        public event Action<string, bool> PromptChanged;

        public NetworkInteractable CurrentInteractable => _currentInteractable;

        private void Awake()
        {
            _overlapResults = new Collider[Mathf.Max(1, overlapBufferSize)];
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                SetCurrentInteractable(null);
                return;
            }

            SetCurrentInteractable(FindBestInteractable());
        }

        private NetworkInteractable FindBestInteractable()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, interactionRange, _overlapResults, detectionMask, QueryTriggerInteraction.Collide);

            float bestDistanceSqr = float.MaxValue;
            NetworkInteractable best = null;
            InteractionContext context = new(NetworkObject, OwnerClientId, IsServer);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapResults[i];
                if (hit == null)
                {
                    continue;
                }

                NetworkInteractable interactable = hit.GetComponentInParent<NetworkInteractable>();
                if (interactable == null || !interactable.IsSpawned || !interactable.CanInteract(context))
                {
                    continue;
                }

                Vector3 closest = hit.ClosestPoint(transform.position);
                float distanceSqr = (closest - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = interactable;
                }
            }

            return best;
        }

        private void SetCurrentInteractable(NetworkInteractable interactable)
        {
            if (_currentInteractable == interactable)
            {
                if (_currentInteractable != null)
                {
                    InteractionContext context = new(NetworkObject, OwnerClientId, IsServer);
                    string prompt = _currentInteractable.GetPromptText(context);
                    PromptChanged?.Invoke(prompt, !string.IsNullOrWhiteSpace(prompt));
                }

                return;
            }

            _currentInteractable = interactable;
            if (_currentInteractable == null)
            {
                PromptChanged?.Invoke(string.Empty, false);
                return;
            }

            InteractionContext newContext = new(NetworkObject, OwnerClientId, IsServer);
            string newPrompt = _currentInteractable.GetPromptText(newContext);
            PromptChanged?.Invoke(newPrompt, !string.IsNullOrWhiteSpace(newPrompt));
        }
    }
}
