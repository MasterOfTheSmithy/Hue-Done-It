// File: Assets/_Project/Gameplay/Interaction/PlayerInteractionController.cs
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

        private PlayerInteractionDetector _detector;

        private void Awake()
        {
            _detector = GetComponent<PlayerInteractionDetector>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
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

            float distance = Vector3.Distance(client.PlayerObject.transform.position, interactable.transform.position);
            if (distance > interactable.MaxUseDistance)
            {
                Debug.LogWarning($"Interaction rejected: client {senderClientId} out of range.");
                return;
            }

            InteractionContext context = new(client.PlayerObject, senderClientId, true);
            if (!interactable.CanInteract(context))
            {
                return;
            }

            interactable.TryInteract(context);
        }
    }
}
