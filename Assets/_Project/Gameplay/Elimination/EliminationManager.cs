// File: Assets/_Project/Gameplay/Elimination/EliminationManager.cs
using HueDoneIt.Evidence;
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

            SpawnEvidenceShard(killerObject, targetObject);

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

        private void SpawnEvidenceShard(NetworkObject killerObject, NetworkObject targetObject)
        {
            if (!IsServer || killerObject == null || targetObject == null)
            {
                return;
            }

            Vector3 awayFromBody = (targetObject.transform.position - killerObject.transform.position);
            if (awayFromBody.sqrMagnitude < 0.01f)
            {
                awayFromBody = UnityEngine.Random.insideUnitSphere;
                awayFromBody.y = 0f;
            }

            Vector3 spawnPosition = targetObject.transform.position + awayFromBody.normalized * 0.85f + Vector3.up * 0.15f;
            GameObject shardObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shardObject.name = $"EvidenceShard_{targetObject.OwnerClientId}_{Time.frameCount}";
            shardObject.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            shardObject.transform.localScale = Vector3.one * 0.42f;

            Collider colliderRef = shardObject.GetComponent<Collider>();
            if (colliderRef != null)
            {
                colliderRef.isTrigger = false;
            }

            NetworkObject networkObject = shardObject.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = shardObject.AddComponent<NetworkObject>();
            }

            NetworkEvidenceShard evidence = shardObject.GetComponent<NetworkEvidenceShard>();
            if (evidence == null)
            {
                evidence = shardObject.AddComponent<NetworkEvidenceShard>();
            }

            string vector = BuildEvidenceDirectionLabel(killerObject.transform.position - targetObject.transform.position);
            string clue = $"Fresh bleach residue was scraped away from the body. Trace direction: {vector}.";
            evidence.ConfigureRuntime(
                $"evidence-{targetObject.OwnerClientId}-{Time.frameCount}",
                "Fresh Bleach Residue",
                clue,
                killerObject.OwnerClientId,
                180f,
                shardObject.GetComponentInChildren<Renderer>());

            networkObject.Spawn(true);
        }

        private static string BuildEvidenceDirectionLabel(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
            {
                return "unclear";
            }

            direction.Normalize();
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
            {
                return direction.x >= 0f ? "east" : "west";
            }

            return direction.z >= 0f ? "north" : "south";
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
