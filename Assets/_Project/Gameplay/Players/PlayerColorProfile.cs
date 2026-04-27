// File: Assets/_Project/Gameplay/Players/PlayerColorProfile.cs
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
            new(0.95f, 0.44f, 0.18f)
        };

        private readonly NetworkVariable<Color32> _playerColor =
            new(new Color32(255, 255, 255, 255), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public Color PlayerColor => _playerColor.Value;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                Color selected = palette != null && palette.Length > 0
                    ? palette[OwnerClientId % (ulong)palette.Length]
                    : Color.white;

                _playerColor.Value = selected;
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

            _playerColor.Value = (Color32)color;
        }
    }
}