// File: Assets/_Project/Gameplay/Players/NetworkPlayerAvatar.cs
using HueDoneIt.Core.Bootstrap;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Players
{
    // This class is the network avatar integration point for player-level presentation state.
    // It does not own movement authority. Movement remains in NetworkPlayerAuthoritativeMover.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkPlayerInputReader))]
    [RequireComponent(typeof(NetworkPlayerAuthoritativeMover))]
    [RequireComponent(typeof(PlayerColorProfile))]
    [RequireComponent(typeof(NetworkPlayerPaintEmitter))]
    [RequireComponent(typeof(PlayerInteractionDetector))]
    [RequireComponent(typeof(PlayerInteractionController))]
    [RequireComponent(typeof(PlayerFloodZoneTracker))]
    [RequireComponent(typeof(PlayerRepairTaskParticipant))]
    [RequireComponent(typeof(PlayerLifeState))]
    [RequireComponent(typeof(PlayerKillInputController))]
    public sealed class NetworkPlayerAvatar : NetworkBehaviour
    {
        // Opacity is replicated from server so HUD and remote presentation stay consistent.
        private readonly NetworkVariable<float> _opacity =
            new NetworkVariable<float>(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Placeholder customization data replicated for all peers.
        private readonly NetworkVariable<Color32> _bodyColor =
            new NetworkVariable<Color32>(new Color32(255, 128, 128, 255), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _hatIndex =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _outfitIndex =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString32Bytes> _playerLabel =
            new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Renderer[] _renderers;
        private Transform _hatTransform;
        private Transform _bodyPlaceholderTransform;
        private PlayerFloodZoneTracker _flood;
        private PlayerLifeState _life;

        public float Opacity01 => _opacity.Value;
        public string PlayerLabel => _playerLabel.Value.ToString();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _flood = GetComponent<PlayerFloodZoneTracker>();
            _life = GetComponent<PlayerLifeState>();

            // Ensure a visible avatar body exists even if the source prefab mesh is removed.
            // LocalPlayerCameraBinder decides whether owner body is visible based on active scene.
            EnsureBodyVisual();
            EnsureHatVisual();
            CacheRenderers();

            if (IsServer)
            {
                // Session-level placeholder customization values are read from Boot settings.
                _opacity.Value = 1f;
                _bodyColor.Value = (Color32)BootSessionConfig.SelectedBodyColor;
                _hatIndex.Value = BootSessionConfig.SelectedHat;
                _outfitIndex.Value = BootSessionConfig.SelectedOutfit;

                bool isCpu = GetComponent<SimpleCpuOpponentAgent>() != null;
                _playerLabel.Value = new FixedString32Bytes((isCpu ? "CPU" : "Player") + $" {OwnerClientId}");
            }

            _opacity.OnValueChanged += HandleOpacityChanged;
            _bodyColor.OnValueChanged += HandleVisualValueChanged;
            _hatIndex.OnValueChanged += HandleVisualValueChanged;
            _outfitIndex.OnValueChanged += HandleVisualValueChanged;

            RefreshVisuals();
        }

        public override void OnNetworkDespawn()
        {
            _opacity.OnValueChanged -= HandleOpacityChanged;
            _bodyColor.OnValueChanged -= HandleVisualValueChanged;
            _hatIndex.OnValueChanged -= HandleVisualValueChanged;
            _outfitIndex.OnValueChanged -= HandleVisualValueChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (IsServer)
            {
                // Opacity is driven by flood saturation and elimination state.
                float targetOpacity = 1f;
                if (_life != null && !_life.IsAlive)
                {
                    targetOpacity = 0.1f;
                }
                else if (_flood != null)
                {
                    targetOpacity = Mathf.Clamp01(1f - _flood.Saturation01);
                }

                _opacity.Value = Mathf.MoveTowards(_opacity.Value, targetOpacity, Time.deltaTime * 0.2f);
            }

            // Clients update their render materials whenever replicated state changes.
            if (!IsServer)
            {
                RefreshVisuals();
            }
        }

        // This method is called by LobbyHudController on the server after a player confirms customization.
        // It updates the replicated visual state so all peers see the same placeholder body, hat, and outfit choices.
        public void ServerApplyCustomization(Color32 bodyColor, int hatIndex, int outfitIndex)
        {
            if (!IsServer)
            {
                return;
            }

            _bodyColor.Value = bodyColor;
            _hatIndex.Value = Mathf.Max(0, hatIndex);
            _outfitIndex.Value = Mathf.Max(0, outfitIndex);

            RefreshVisuals();
        }

        private void HandleOpacityChanged(float previousValue, float currentValue)
        {
            RefreshVisuals();
        }

        private void HandleVisualValueChanged<T>(T previousValue, T currentValue) where T : unmanaged
        {
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            // Opacity low values wash color toward white to communicate diffused state.
            Color baseColor = _bodyColor.Value;
            float wash01 = 1f - _opacity.Value;
            Color opacityColor = Color.Lerp(baseColor, Color.white, wash01 * 0.85f);

            EnsureBodyVisual();
            EnsureHatVisual();
            CacheRenderers();

            if (_renderers != null)
            {
                for (int index = 0; index < _renderers.Length; index++)
                {
                    Renderer renderer = _renderers[index];
                    if (renderer == null || renderer.material == null)
                    {
                        continue;
                    }

                    if (_hatTransform != null && renderer.transform == _hatTransform)
                    {
                        float hatDarken = (_hatIndex.Value % 6) * 0.08f;
                        renderer.material.color = Color.Lerp(opacityColor, Color.black, hatDarken);
                    }
                    else
                    {
                        float outfitDarken = (_outfitIndex.Value % 6) * 0.06f;
                        renderer.material.color = Color.Lerp(opacityColor, Color.black, outfitDarken);
                    }
                }
            }

            if (_hatTransform != null)
            {
                _hatTransform.gameObject.SetActive(_hatIndex.Value > 0);
                _hatTransform.localScale = Vector3.one * (0.25f + (_hatIndex.Value * 0.05f));
            }
        }

        private void CacheRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void EnsureBodyVisual()
        {
            // If a visible authored renderer already exists, keep authored presentation.
            Renderer[] currentRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < currentRenderers.Length; i++)
            {
                Renderer renderer = currentRenderers[i];
                if (renderer != null && renderer.transform != _hatTransform)
                {
                    return;
                }
            }

            if (_bodyPlaceholderTransform != null)
            {
                return;
            }

            // Placeholder body is a capsule so Lobby always has a visible avatar with no art dependency.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "BodyPlaceholder";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 0f, 0f);
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = new Vector3(0.9f, 1f, 0.9f);

            Collider bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null)
            {
                Destroy(bodyCollider);
            }

            _bodyPlaceholderTransform = body.transform;
        }

        private void EnsureHatVisual()
        {
            if (_hatTransform != null)
            {
                return;
            }

            // Placeholder hat mesh so customization has visible output without new assets.
            GameObject hat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hat.name = "HatPlaceholder";
            hat.transform.SetParent(transform, false);
            hat.transform.localPosition = new Vector3(0f, 1.1f, 0f);

            Collider hatCollider = hat.GetComponent<Collider>();
            if (hatCollider != null)
            {
                Destroy(hatCollider);
            }

            _hatTransform = hat.transform;
        }
    }
}