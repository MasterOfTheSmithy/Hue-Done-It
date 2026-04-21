// File: Assets/_Project/Gameplay/Elimination/EliminationManager.cs
using HueDoneIt.Gameplay.Round;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Elimination
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class EliminationManager : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float maxEliminationRange = 2.5f;
        [SerializeField] private PlayerRemains remainsPrefab;

        private NetworkRoundState _roundState;

        private void Awake()
        {
            ResolveRoundState();
        }

        public bool TryHandleEliminationRequest(NetworkObject killerObject, ulong targetNetworkObjectId)
        {
            if (!IsServer || killerObject == null || !killerObject.IsSpawned)
            {
                return false;
            }

            ResolveRoundState();
            if (_roundState == null || !_roundState.IsFreeRoam)
            {
                return false;
            }

            if (!killerObject.TryGetComponent(out PlayerLifeState killerLifeState) || !killerLifeState.IsAlive)
            {
                return false;
            }

            if (!killerObject.TryGetComponent(out PlayerKillInputController killerController) || !killerController.IsTestKillerEnabled)
            {
                Debug.LogWarning($"Elimination rejected: player {killerObject.OwnerClientId} is not marked as test killer.");
                return false;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject))
            {
                return false;
            }

            if (targetObject == killerObject)
            {
                return false;
            }

            if (!targetObject.TryGetComponent(out PlayerLifeState targetLifeState) || !targetLifeState.IsAlive)
            {
                return false;
            }

            float distance = Vector3.Distance(killerObject.transform.position, targetObject.transform.position);
            if (distance > maxEliminationRange)
            {
                Debug.LogWarning($"Elimination rejected: target out of range ({distance:F2}m).");
                return false;
            }

            if (!targetLifeState.ServerTrySetEliminated())
            {
                return false;
            }

            if (targetLifeState.ServerTryMarkRemainsSpawned())
            {
                SpawnRemains(targetObject, targetLifeState);
            }

            Debug.Log($"Elimination accepted. Killer={killerObject.OwnerClientId}, Target={targetObject.OwnerClientId}");
            return true;
        }

        private void SpawnRemains(NetworkObject eliminatedPlayerObject, PlayerLifeState eliminatedPlayerLifeState)
        {
            if (remainsPrefab == null)
            {
                Debug.LogError("EliminationManager is missing remains prefab reference.");
                return;
            }

            Vector3 spawnPosition = eliminatedPlayerObject.transform.position;
            Quaternion spawnRotation = eliminatedPlayerObject.transform.rotation;
            PlayerRemains remainsInstance = Instantiate(remainsPrefab, spawnPosition, spawnRotation);

            if (!remainsInstance.TryGetComponent(out NetworkObject remainsNetworkObject))
            {
                Debug.LogError("Remains prefab does not contain a NetworkObject.");
                Destroy(remainsInstance.gameObject);
                return;
            }

            remainsNetworkObject.Spawn(true);
            remainsInstance.ServerInitialize(eliminatedPlayerObject.OwnerClientId, eliminatedPlayerObject.NetworkObjectId, eliminatedPlayerObject.name);
        }

        private void ResolveRoundState()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }
        }
    }
}
