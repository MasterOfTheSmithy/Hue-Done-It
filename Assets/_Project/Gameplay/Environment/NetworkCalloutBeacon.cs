// File: Assets/_Project/Gameplay/Environment/NetworkCalloutBeacon.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Environment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkCalloutBeacon : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string beaconId = "callout-beacon";
        [SerializeField] private string displayName = "Callout Beacon";
        [SerializeField] private string calloutPrompt = "Broadcast Callout";
        [SerializeField] private string fakePrompt = "Broadcast Fake Callout";

        [Header("Rules")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 40f;
        [SerializeField, Min(0f)] private float fakePenaltySeconds = 4f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.95f, 0.92f, 0.22f, 1f);
        [SerializeField] private Color usedColor = new(1f, 0.55f, 0.18f, 1f);
        [SerializeField] private Color cooldownColor = new(0.28f, 0.24f, 0.12f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string BeaconId => beaconId;
        public string DisplayName => displayName;
        public bool IsReady => CooldownRemaining <= 0.001f;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEndServerTime.Value - GetServerTime());

        protected override void Awake()
        {
            base.Awake();
            statusRenderer ??= GetComponentInChildren<Renderer>();
            ApplyColor(readyColor);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _cooldownEndServerTime.OnValueChanged += HandleCooldownChanged;
            ApplyColor(IsReady ? readyColor : cooldownColor);
        }

        public override void OnNetworkDespawn()
        {
            _cooldownEndServerTime.OnValueChanged -= HandleCooldownChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            ApplyColor(IsReady ? readyColor : cooldownColor);
        }

        public void ConfigureRuntime(string id, string label, string calloutText, string fakeText, float cooldown, float penalty, Renderer renderer = null)
        {
            beaconId = string.IsNullOrWhiteSpace(id) ? beaconId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            calloutPrompt = string.IsNullOrWhiteSpace(calloutText) ? calloutPrompt : calloutText;
            fakePrompt = string.IsNullOrWhiteSpace(fakeText) ? fakePrompt : fakeText;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            fakePenaltySeconds = Mathf.Max(0f, penalty);
            statusRenderer = renderer != null ? renderer : GetComponentInChildren<Renderer>();
            ApplyColor(IsReady ? readyColor : cooldownColor);
        }

        public override bool CanInteract(in InteractionContext context)
        {
            if (context.InteractorObject == null)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerLifeState lifeState) && !lifeState.IsAlive)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            return (roundState == null || roundState.IsFreeRoam) && IsReady;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            if (!IsReady)
            {
                return $"{displayName}: cooling {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            bool isBleach = context.InteractorObject != null &&
                            context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                            roleController.CurrentRole == PlayerRole.Bleach;
            return isBleach ? fakePrompt : calloutPrompt;
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            bool isBleach = context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach;
            string location = BuildLocationLabel(context.InteractorObject.transform.position);
            if (isBleach)
            {
                roundState?.ServerApplySabotagePressure(context.InteractorClientId, $"Fake callout from {displayName}", fakePenaltySeconds);
                roundState?.ServerAnnounceEnvironmentalEvent(displayName, $"False crew callout pinged near {location}. Verify with scanners/vitals before voting.", 0f);
            }
            else
            {
                roundState?.ServerAnnounceEnvironmentalEvent(displayName, $"Player {context.InteractorClientId} called attention near {location}. Nearby players should verify evidence or routes.", 0f);
            }

            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(usedColor);
            return true;
        }

        public bool ServerForceCooldown(float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, reason, 0f);
            ApplyColor(cooldownColor);
            return true;
        }

        private static string BuildLocationLabel(Vector3 position)
        {
            if (position.z > 6f)
            {
                return position.x < -6f ? "Engine/Northwest" : (position.x > 6f ? "Navigation/Northeast" : "Fore Hall");
            }

            if (position.z < -6f)
            {
                return position.x < -6f ? "Cargo/Southwest" : (position.x > 6f ? "Lab/Southeast" : "Aft Hall");
            }

            return position.x < -6f ? "West Connector" : (position.x > 6f ? "East Connector" : "Center Hall");
        }

        private void HandleCooldownChanged(float previous, float current)
        {
            ApplyColor(IsReady ? readyColor : cooldownColor);
        }

        private void ApplyColor(Color color)
        {
            statusRenderer ??= GetComponentInChildren<Renderer>();
            if (statusRenderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            statusRenderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            statusRenderer.SetPropertyBlock(_block);
        }

        private float GetServerTime()
        {
            return NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.unscaledTime;
        }
    }
}
