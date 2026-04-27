// File: Assets/_Project/Gameplay/Environment/NetworkEmergencySealStation.cs
using HueDoneIt.Flood;
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
    public sealed class NetworkEmergencySealStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "emergency-seal";
        [SerializeField] private string displayName = "Emergency Seal Station";
        [SerializeField] private string interactPrompt = "Seal Route";

        [Header("Effect")]
        [SerializeField] private FloodZone targetZone;
        [SerializeField] private NetworkBleachLeakHazard linkedHazard;
        [SerializeField, Min(1f)] private float cooldownSeconds = 42f;
        [SerializeField, Min(0f)] private float hazardSuppressionSeconds = 34f;
        [SerializeField, Min(0f)] private float roundTimeBonusSeconds = 8f;
        [SerializeField] private bool colorPlayersOnly = true;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.2f, 1f, 0.72f, 1f);
        [SerializeField] private Color cooldownColor = new(0.2f, 0.32f, 0.42f, 1f);
        [SerializeField] private Color usedColor = new(0.95f, 0.9f, 0.28f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string StationId => stationId;
        public string DisplayName => displayName;
        public bool IsReady => CooldownRemaining <= 0.001f;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEndServerTime.Value - GetServerTime());
        public FloodZone TargetZone => targetZone;
        public NetworkBleachLeakHazard LinkedHazard => linkedHazard;

        protected override void Awake()
        {
            base.Awake();
            if (statusRenderer == null)
            {
                statusRenderer = GetComponentInChildren<Renderer>();
            }

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

        public void ConfigureRuntime(
            string id,
            string label,
            string prompt,
            FloodZone zone,
            NetworkBleachLeakHazard hazard,
            float cooldown,
            float suppressSeconds,
            float timeBonusSeconds,
            Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            targetZone = zone;
            linkedHazard = hazard;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            hazardSuppressionSeconds = Mathf.Max(0f, suppressSeconds);
            roundTimeBonusSeconds = Mathf.Max(0f, timeBonusSeconds);
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
            return roundState == null || roundState.IsFreeRoam;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            if (context.InteractorObject != null &&
                context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                colorPlayersOnly &&
                roleController.CurrentRole == PlayerRole.Bleach)
            {
                return displayName + ": corrosive input rejected";
            }

            if (!IsReady)
            {
                return $"{displayName}: recharging {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            if (linkedHazard != null && !linkedHazard.IsSuppressed)
            {
                return $"{interactPrompt} / suppress {linkedHazard.DisplayName}";
            }

            return targetZone != null ? $"{interactPrompt} ({targetZone.ZoneId})" : interactPrompt;
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                colorPlayersOnly &&
                roleController.CurrentRole == PlayerRole.Bleach)
            {
                return false;
            }

            if (targetZone != null)
            {
                targetZone.TrySetState(FloodZoneState.SealedSafe);
            }

            if (linkedHazard != null)
            {
                linkedHazard.ServerSuppressFor(hazardSuppressionSeconds, linkedHazard.DisplayName + " purged");
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            roundState?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, roundTimeBonusSeconds);

            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(usedColor);
            return true;
        }

        private void HandleCooldownChanged(float previous, float current)
        {
            ApplyColor(IsReady ? readyColor : cooldownColor);
        }

        private void ApplyColor(Color color)
        {
            if (statusRenderer == null)
            {
                statusRenderer = GetComponentInChildren<Renderer>();
            }

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
