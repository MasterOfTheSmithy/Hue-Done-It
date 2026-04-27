// File: Assets/_Project/Gameplay/Interaction/PlayerInteractionDetector.cs
using System;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Tasks;
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
        [SerializeField] private LayerMask lineOfSightMask = ~0;
        [SerializeField] private int overlapBufferSize = 16;
        [SerializeField] private int lineOfSightHitBufferSize = 12;
        [SerializeField, Min(0.01f)] private float screenCenterProbeRadius = 0.18f;

        private Collider[] _overlapResults;
        private RaycastHit[] _lineOfSightHits;
        private NetworkInteractable _currentInteractable;
        private PlayerLifeState _lifeState;

        public event Action<string, bool> PromptChanged;

        public NetworkInteractable CurrentInteractable => _currentInteractable;

        private void Awake()
        {
            _overlapResults = new Collider[Mathf.Max(1, overlapBufferSize)];
            _lineOfSightHits = new RaycastHit[Mathf.Max(1, lineOfSightHitBufferSize)];
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

            Vector3 fallbackForward = GetFallbackForward();
            Vector3 rayOrigin = GetRayOrigin();
            int hitCount = Physics.OverlapSphereNonAlloc(rayOrigin, interactionRange, _overlapResults, detectionMask, QueryTriggerInteraction.Collide);

            float bestScore = float.MinValue;
            NetworkInteractable best = null;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapResults[i];
                if (hit == null)
                {
                    continue;
                }

                NetworkInteractable interactable = hit.GetComponentInParent<NetworkInteractable>();
                if (!IsLocallyViable(interactable, context, rayOrigin))
                {
                    continue;
                }

                Vector3 interactionPoint = interactable.GetInteractionPoint(rayOrigin);
                Vector3 toTarget = interactionPoint - rayOrigin;
                float distance = toTarget.magnitude;
                if (distance > GetPromptRange(interactable) || distance <= 0.001f)
                {
                    continue;
                }

                Vector3 direction = toTarget / distance;
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
            return IsLocallyViable(interactable, context, ray.origin);
        }

        private bool IsLocallyViable(NetworkInteractable interactable, in InteractionContext context, Vector3 rayOrigin)
        {
            if (interactable == null || !interactable.IsSpawned || !interactable.CanInteract(context))
            {
                return false;
            }

            Vector3 targetPoint = interactable.GetInteractionPoint(rayOrigin);
            Vector3 toTarget = targetPoint - rayOrigin;
            float distance = toTarget.magnitude;
            if (distance > GetPromptRange(interactable) || distance <= 0.001f)
            {
                return false;
            }

            return !HasBlockingLineOfSight(rayOrigin, targetPoint, interactable);
        }

        private float GetPromptRange(NetworkInteractable interactable)
        {
            if (interactable == null)
            {
                return interactionRange;
            }

            // Task prompts must not appear just because the player's broad scan sphere found the station.
            // They only appear inside the task's own use radius.
            if (interactable is NetworkRepairTask)
            {
                return Mathf.Max(0.1f, interactable.MaxUseDistance);
            }

            return Mathf.Max(0.1f, Mathf.Min(interactionRange, interactable.MaxUseDistance));
        }

        private bool HasBlockingLineOfSight(Vector3 rayOrigin, Vector3 targetPoint, NetworkInteractable interactable)
        {
            Vector3 toTarget = targetPoint - rayOrigin;
            float distance = toTarget.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                toTarget / distance,
                _lineOfSightHits,
                distance + 0.05f,
                lineOfSightMask,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.MaxValue;
            Transform nearestTransform = null;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _lineOfSightHits[i];
                if (hit.collider == null || hit.transform == null)
                {
                    continue;
                }

                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestTransform = hit.transform;
                }
            }

            if (nearestTransform == null)
            {
                return false;
            }

            return !nearestTransform.IsChildOf(interactable.transform);
        }

        private Vector3 GetRayOrigin()
        {
            Camera cameraRef = Camera.main;
            if (cameraRef != null)
            {
                return cameraRef.transform.position;
            }

            return transform.position + (Vector3.up * 0.8f);
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
            _currentInteractable = interactable;
            if (_currentInteractable == null)
            {
                PromptChanged?.Invoke(string.Empty, false);
                return;
            }

            InteractionContext context = new(NetworkObject, OwnerClientId, false);
            string prompt = _currentInteractable.GetPromptText(context);
            bool hasPrompt = !string.IsNullOrWhiteSpace(prompt);
            PromptChanged?.Invoke(hasPrompt ? $"Press E to {prompt}" : string.Empty, hasPrompt);
        }
    }
}
