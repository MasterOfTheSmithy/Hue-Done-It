// File: Assets/_Project/Gameplay/Players/NetworkPlayerAvatar.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkPlayerInputReader))]
    [RequireComponent(typeof(NetworkPlayerAuthoritativeMover))]
    [RequireComponent(typeof(PlayerInteractionDetector))]
    [RequireComponent(typeof(PlayerInteractionController))]
    [RequireComponent(typeof(PlayerFloodZoneTracker))]
    [RequireComponent(typeof(PlayerRepairTaskParticipant))]
    [RequireComponent(typeof(PlayerLifeState))]
    [RequireComponent(typeof(PlayerKillInputController))]
    public sealed class NetworkPlayerAvatar : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Debug.Log($"NetworkPlayerAvatar spawned. OwnerClientId={OwnerClientId}, IsServer={IsServer}");
        }

        public override void OnNetworkDespawn()
        {
            Debug.Log($"NetworkPlayerAvatar despawned. OwnerClientId={OwnerClientId}");
            base.OnNetworkDespawn();
        }
    }
}
