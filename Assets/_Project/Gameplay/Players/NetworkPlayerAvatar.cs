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
        private readonly NetworkVariable<float> _opacity =
            new(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Color32> _bodyColor =
            new(new Color32(255, 128, 128, 255), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _hatIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _outfitIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString32Bytes> _playerLabel =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Renderer[] _renderers;
        private Transform _hatTransform;
        private PlayerFloodZoneTracker _flood;
        private PlayerLifeState _life;

        public float Opacity01 => _opacity.Value;
        public string PlayerLabel => _playerLabel.Value.ToString();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _flood = GetComponent<PlayerFloodZoneTracker>();
            _life = GetComponent<PlayerLifeState>();
            EnsureHatVisual();

            if (IsServer)
            {
                _opacity.Value = 1f;
                _bodyColor.Value = (Color32)BootSessionConfig.SelectedBodyColor;
                _hatIndex.Value = BootSessionConfig.SelectedHat;
                _outfitIndex.Value = BootSessionConfig.SelectedOutfit;
                _playerLabel.Value = new FixedString32Bytes((GetComponent<SimpleCpuOpponentAgent>() != null ? "CPU" : "Player") + $" {OwnerClientId}");
            }

            _opacity.OnValueChanged += HandleOpacityChanged;
            _bodyColor.OnValueChanged += (_, _) => RefreshVisuals();
            _hatIndex.OnValueChanged += (_, _) => RefreshVisuals();
            _outfitIndex.OnValueChanged += (_, _) => RefreshVisuals();
            RefreshVisuals();
        }

        private void Update()
        {
            if (IsServer)
            {
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

            if (!IsServer && _life != null && !_life.IsAlive)
            {
                RefreshVisuals();
            }
        }

        public override void OnNetworkDespawn()
        {
            _opacity.OnValueChanged -= HandleOpacityChanged;
            base.OnNetworkDespawn();
        }

        private void HandleOpacityChanged(float _, float __)
        {
            RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            Color baseColor = _bodyColor.Value;
            float wash = 1f - _opacity.Value;
            Color final = Color.Lerp(baseColor, Color.white, wash * 0.85f);

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Renderer r = _renderers[i];
                    if (r == null || r.material == null)
                    {
                        continue;
                    }

                    if (r.transform == _hatTransform)
                    {
                        r.material.color = Color.Lerp(final, Color.black, (_hatIndex.Value % 6) * 0.08f);
                    }
                    else
                    {
                        float outfitDarken = (_outfitIndex.Value % 6) * 0.06f;
                        r.material.color = Color.Lerp(final, Color.black, outfitDarken);
                    }
                }
            }

            EnsureHatVisual();
            _hatTransform.gameObject.SetActive(_hatIndex.Value > 0);
            _hatTransform.localScale = Vector3.one * (0.25f + (_hatIndex.Value * 0.05f));
        }

        private void EnsureHatVisual()
        {
            if (_hatTransform != null)
            {
                return;
            }

            GameObject hat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hat.name = "HatPlaceholder";
            hat.transform.SetParent(transform, false);
            hat.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            Destroy(hat.GetComponent<Collider>());
            _hatTransform = hat.transform;
        }
    }
}
