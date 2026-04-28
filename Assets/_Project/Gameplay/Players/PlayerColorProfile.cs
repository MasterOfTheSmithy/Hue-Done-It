// File: Assets/_Project/Gameplay/Players/PlayerColorProfile.cs
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerColorProfile : NetworkBehaviour
    {
        [SerializeField]
        private Color[] palette =
        {
            new(0.98f, 0.28f, 0.42f),
            new(0.26f, 0.62f, 0.98f),
            new(0.29f, 0.90f, 0.52f),
            new(0.97f, 0.76f, 0.21f),
            new(0.78f, 0.34f, 0.94f),
            new(0.95f, 0.44f, 0.18f),
            new(0.18f, 0.92f, 0.84f),
            new(0.96f, 0.33f, 0.70f),
            new(0.65f, 0.94f, 0.26f),
            new(1.00f, 0.55f, 0.35f)
        };

        [SerializeField]
        private Color[] cpuPalette =
        {
            new(0.54f, 0.78f, 1.00f),
            new(1.00f, 0.66f, 0.86f),
            new(0.86f, 1.00f, 0.60f),
            new(1.00f, 0.87f, 0.55f),
            new(0.76f, 0.68f, 1.00f),
            new(0.66f, 1.00f, 0.92f),
            new(1.00f, 0.79f, 0.62f),
            new(0.92f, 0.92f, 0.92f)
        };

        private readonly NetworkVariable<Color32> _playerColor =
            new(new Color32(255, 255, 255, 255), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public Color PlayerColor => _playerColor.Value;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                _playerColor.Value = ResolveUniqueColor(OwnerClientId);
            }
        }

        // This method is called by LobbyHudController on the server when a player confirms a new color choice.
        // The selected color is replicated so paint emission and any color-driven presentation stay in sync.
        public void ServerSetPlayerColor(Color color)
        {
            if (!IsServer)
            {
                return;
            }

            // Gameplay readability requires distinct player colors. The visual model is now the outfit;
            // player color is the identity/paint channel and should remain unique for friend-test sessions.
            _playerColor.Value = ResolveUniqueColor(OwnerClientId);
        }

        private Color32 ResolveUniqueColor(ulong clientId)
        {
            bool isCpu = GetComponent<SimpleCpuOpponentAgent>() != null;
            return isCpu
                ? ResolveCpuColor()
                : ResolveHumanPlayerColor(clientId);
        }

        private Color32 ResolveHumanPlayerColor(ulong clientId)
        {
            List<ulong> playerIds = new List<ulong>();
            PlayerColorProfile[] profiles = FindObjectsByType<PlayerColorProfile>(FindObjectsSortMode.None);
            for (int i = 0; i < profiles.Length; i++)
            {
                PlayerColorProfile profile = profiles[i];
                if (profile == null || profile.GetComponent<SimpleCpuOpponentAgent>() != null)
                {
                    continue;
                }

                if (!playerIds.Contains(profile.OwnerClientId))
                {
                    playerIds.Add(profile.OwnerClientId);
                }
            }

            if (!playerIds.Contains(clientId))
            {
                playerIds.Add(clientId);
            }

            playerIds.Sort();
            int index = Mathf.Max(0, playerIds.IndexOf(clientId));

            if (palette != null && index < palette.Length)
            {
                return (Color32)palette[index];
            }

            float hue = Mathf.Repeat((index * 0.113f) + 0.06f, 1f);
            return (Color32)Color.HSVToRGB(hue, 0.78f, 1f);
        }

        private Color32 ResolveCpuColor()
        {
            int cpuIndex = ResolveCpuIndex();
            if (cpuPalette != null && cpuPalette.Length > 0)
            {
                Color candidate = cpuPalette[Mathf.Abs(cpuIndex % cpuPalette.Length)];
                if (!IsTooCloseToHumanColor(candidate))
                {
                    return (Color32)candidate;
                }
            }

            float hue = Mathf.Repeat((cpuIndex * 0.137f) + 0.58f, 1f);
            return (Color32)Color.HSVToRGB(hue, 0.48f, 1f);
        }

        private int ResolveCpuIndex()
        {
            List<ulong> cpuIds = new List<ulong>();
            PlayerColorProfile[] profiles = FindObjectsByType<PlayerColorProfile>(FindObjectsSortMode.None);
            for (int i = 0; i < profiles.Length; i++)
            {
                PlayerColorProfile profile = profiles[i];
                if (profile == null || profile.GetComponent<SimpleCpuOpponentAgent>() == null)
                {
                    continue;
                }

                ulong stable = profile.NetworkObject != null && profile.NetworkObject.IsSpawned
                    ? profile.NetworkObject.NetworkObjectId
                    : (ulong)Mathf.Abs(profile.GetInstanceID());
                if (!cpuIds.Contains(stable))
                {
                    cpuIds.Add(stable);
                }
            }

            ulong ownStable = NetworkObject != null && NetworkObject.IsSpawned
                ? NetworkObject.NetworkObjectId
                : (ulong)Mathf.Abs(GetInstanceID());
            if (!cpuIds.Contains(ownStable))
            {
                cpuIds.Add(ownStable);
            }

            cpuIds.Sort();
            return Mathf.Max(0, cpuIds.IndexOf(ownStable));
        }

        private bool IsTooCloseToHumanColor(Color candidate)
        {
            PlayerColorProfile[] profiles = FindObjectsByType<PlayerColorProfile>(FindObjectsSortMode.None);
            for (int i = 0; i < profiles.Length; i++)
            {
                PlayerColorProfile profile = profiles[i];
                if (profile == null || profile == this || profile.GetComponent<SimpleCpuOpponentAgent>() != null)
                {
                    continue;
                }

                Color humanColor = profile.PlayerColor;
                float distance = Mathf.Abs(candidate.r - humanColor.r) +
                                 Mathf.Abs(candidate.g - humanColor.g) +
                                 Mathf.Abs(candidate.b - humanColor.b);
                if (distance < 0.55f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
