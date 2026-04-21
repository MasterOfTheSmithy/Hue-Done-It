// File: Assets/_Project/Gameplay/Interaction/PlayerInteractionDetector.cs
using System;
using HueDoneIt.Gameplay.Elimination;
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
        [SerializeField, Min(0.01f)] private float screenCenterProbeRadius = 0.18f;

        private Collider[] _overlapResults;
        private NetworkInteractable _currentInteractable;
        private PlayerLifeState _lifeState;

        public event Action<string, bool> PromptChanged;

        public NetworkInteractable CurrentInteractable => _currentInteractable;

        private void Awake()
        {
            _overlapResults = new Collider[Mathf.Max(1, overlapBufferSize)];
            _lifeState = GetComponent<PlayerLifeState>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                SetCurrentInteractable(null);
                return;
            }

            if (_lifeState != null && !_lifeState.IsAlive)
            {
                SetCurrentInteractable(null);
                return;
            }

            SetCurrentInteractable(FindBestInteractable());
        }

        private NetworkInteractable FindBestInteractable()
        {
            InteractionContext context = new(NetworkObject, OwnerClientId, false);
            if (TryFindCenterScreenInteractable(context, out NetworkInteractable focused))
            {
                return focused;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(transform.position, interactionRange, _overlapResults, detectionMask, QueryTriggerInteraction.Collide);
            float bestScore = float.MinValue;
            NetworkInteractable best = null;
            Vector3 fallbackForward = GetFallbackForward();

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapResults[i];
                if (hit == null)
                {
                    continue;
                }

                NetworkInteractable interactable = hit.GetComponentInParent<NetworkInteractable>();
                if (!IsLocallyViable(interactable, context))
                {
                    continue;
                }

                Vector3 closest = hit.ClosestPoint(transform.position);
                Vector3 toTarget = closest - transform.position;
                float distance = toTarget.magnitude;
                if (distance > Mathf.Max(interactionRange, interactable.MaxUseDistance))
                {
                    continue;
                }

                Vector3 direction = distance > 0.001f ? toTarget / distance : fallbackForward;
                float facing = Vector3.Dot(fallbackForward, direction);
                float score = (facing * 4f) - distance;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = interactable;
                }
            }

            return best;
        }

        private bool TryFindCenterScreenInteractable(in InteractionContext context, out NetworkInteractable interactable)
        {
            interactable = null;
            Camera cameraRef = Camera.main;
            if (cameraRef == null)
            {
                return false;
            }

            Ray ray = cameraRef.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.SphereCast(ray, screenCenterProbeRadius, out RaycastHit hit, interactionRange, detectionMask, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            interactable = hit.collider != null ? hit.collider.GetComponentInParent<NetworkInteractable>() : null;
            return IsLocallyViable(interactable, context);
        }

        private bool IsLocallyViable(NetworkInteractable interactable, in InteractionContext context)
        {
            return interactable != null && interactable.IsSpawned && interactable.CanInteract(context);
        }

        private Vector3 GetFallbackForward()
        {
            Camera cameraRef = Camera.main;
            if (cameraRef != null)
            {
                Vector3 cameraForward = cameraRef.transform.forward;
                cameraForward.y = 0f;
                if (cameraForward.sqrMagnitude > 0.001f)
                {
                    return cameraForward.normalized;
                }
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        private void SetCurrentInteractable(NetworkInteractable interactable)
        {
            if (_currentInteractable == interactable)
            {
                if (_currentInteractable != null)
                {
                    InteractionContext context = new(NetworkObject, OwnerClientId, false);
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

            InteractionContext newContext = new(NetworkObject, OwnerClientId, false);
            string newPrompt = _currentInteractable.GetPromptText(newContext);
            PromptChanged?.Invoke(newPrompt, !string.IsNullOrWhiteSpace(newPrompt));
        }
    }
}
