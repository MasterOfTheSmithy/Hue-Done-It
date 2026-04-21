// File: Assets/_Project/Gameplay/Interaction/PlayerInteractionController.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Gameplay.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerInteractionDetector))]
    public sealed class PlayerInteractionController : NetworkBehaviour
    {
        [SerializeField] private Key interactKey = Key.E;
        [SerializeField] private LayerMask interactionLineOfSightMask = ~0;

        private PlayerInteractionDetector _detector;
        private PlayerLifeState _lifeState;

        private void Awake()
        {
            _detector = GetComponent<PlayerInteractionDetector>();
            _lifeState = GetComponent<PlayerLifeState>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                return;
            }

            if (_lifeState != null && !_lifeState.IsAlive)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[interactKey].wasPressedThisFrame)
            {
                return;
            }

            NetworkInteractable interactable = _detector.CurrentInteractable;
            if (interactable == null)
            {
                return;
            }

            RequestInteractServerRpc(interactable.NetworkObjectId);
        }

        [ServerRpc]
        private void RequestInteractServerRpc(ulong interactableNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) || client.PlayerObject == null)
            {
                Debug.LogWarning($"Interaction rejected: invalid sender {senderClientId}.");
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(interactableNetworkObjectId, out NetworkObject objectRef))
            {
                Debug.LogWarning($"Interaction rejected: missing object {interactableNetworkObjectId}.");
                return;
            }

            if (!objectRef.TryGetComponent(out NetworkInteractable interactable))
            {
                Debug.LogWarning($"Interaction rejected: object {interactableNetworkObjectId} is not interactable.");
                return;
            }

            if (client.PlayerObject.TryGetComponent(out PlayerLifeState interactorLifeState) && !interactorLifeState.IsAlive)
            {
                Debug.LogWarning($"Interaction rejected: eliminated client {senderClientId}.");
                return;
            }

            Vector3 origin = client.PlayerObject.transform.position + (Vector3.up * 0.9f);
            Vector3 target = interactable.transform.position + (Vector3.up * 0.6f);
            Vector3 toTarget = target - origin;
            float distance = toTarget.magnitude;
            if (distance > interactable.MaxUseDistance)
            {
                Debug.LogWarning($"Interaction rejected: client {senderClientId} out of range.");
                return;
            }

            if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, distance + 0.05f, interactionLineOfSightMask, QueryTriggerInteraction.Ignore) &&
                !hit.transform.IsChildOf(interactable.transform))
            {
                Debug.LogWarning($"Interaction rejected: client {senderClientId} line of sight blocked.");
                return;
            }

            InteractionContext context = new(client.PlayerObject, senderClientId, true);
            if (!interactable.CanInteract(context))
            {
                return;
            }

            bool success = interactable.TryInteract(context);
            if (!success)
            {
                return;
            }

            if (client.PlayerObject.TryGetComponent(out NetworkPlayerPaintEmitter paintEmitter))
            {
                Vector3 interactionNormal = (origin - target).sqrMagnitude > 0.0001f
                    ? (origin - target).normalized
                    : Vector3.up;
                paintEmitter.ServerEmitPaint(
                    PaintEventKind.TaskInteract,
                    target,
                    interactionNormal,
                    0.24f,
                    0.65f,
                    6.25f,
                    toTarget.normalized,
                    PaintSplatType.TaskInteract,
                    PaintSplatPermanence.Permanent,
                    -1);
            }

            if (interactable is NetworkRepairTask repairTask &&
                client.PlayerObject.TryGetComponent(out PlayerRepairTaskParticipant participant))
            {
                participant.ServerRegisterTaskStart(repairTask, senderClientId);
            }
        }
    }
}
