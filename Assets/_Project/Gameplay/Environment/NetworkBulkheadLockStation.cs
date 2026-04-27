// File: Assets/_Project/Gameplay/Environment/NetworkBulkheadLockStation.cs
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
    public sealed class NetworkBulkheadLockStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "bulkhead-lock";
        [SerializeField] private string displayName = "Bulkhead Lock Station";
        [SerializeField] private string lockPrompt = "Seal Bulkhead";
        [SerializeField] private string jamPrompt = "Jam Bulkhead";

        [Header("Linked Route")]
        [SerializeField] private FloodZone linkedZone;
        [SerializeField, Min(1f)] private float cooldownSeconds = 58f;
        [SerializeField, Min(1f)] private float jamSeconds = 22f;
        [SerializeField, Min(0f)] private float timeBonusSeconds = 4f;
        [SerializeField, Min(0f)] private float jamTimePenaltySeconds = 7f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.22f, 0.82f, 1f, 1f);
        [SerializeField] private Color sealedColor = new(0.22f, 1f, 0.5f, 1f);
        [SerializeField] private Color cooldownColor = new(0.14f, 0.18f, 0.25f, 1f);
        [SerializeField] private Color jammedColor = new(1f, 0.25f, 0.45f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string StationId => stationId;
        public string DisplayName => displayName;
        public FloodZone LinkedZone => linkedZone;
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
            ApplyCurrentColor();
        }

        public override void OnNetworkDespawn()
        {
            _cooldownEndServerTime.OnValueChanged -= HandleCooldownChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            ApplyCurrentColor();
        }

        public void ConfigureRuntime(string id, string label, string lockText, string jamText, FloodZone zone, float cooldown, float jamDuration, float bonusSeconds, float penaltySeconds, Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            lockPrompt = string.IsNullOrWhiteSpace(lockText) ? lockPrompt : lockText;
            jamPrompt = string.IsNullOrWhiteSpace(jamText) ? jamPrompt : jamText;
            linkedZone = zone;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            jamSeconds = Mathf.Max(1f, jamDuration);
            timeBonusSeconds = Mathf.Max(0f, bonusSeconds);
            jamTimePenaltySeconds = Mathf.Max(0f, penaltySeconds);
            statusRenderer = renderer != null ? renderer : GetComponentInChildren<Renderer>();
            ApplyCurrentColor();
        }

        public override bool CanInteract(in InteractionContext context)
        {
            if (context.InteractorObject == null || linkedZone == null)
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
            if (linkedZone == null)
            {
                return displayName + ": no route linked";
            }

            if (!IsReady)
            {
                return $"{displayName}: cycling {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            bool isBleach = context.InteractorObject != null &&
                            context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                            roleController.CurrentRole == PlayerRole.Bleach;
            return isBleach ? $"{jamPrompt} ({displayName})" : $"{lockPrompt}: {linkedZone.ZoneId}";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || linkedZone == null || !IsReady)
            {
                return false;
            }

            bool isBleach = context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach;
            if (isBleach)
            {
                return ServerJam(context.InteractorClientId, jamSeconds, displayName + " jammed");
            }

            linkedZone.TrySetState(FloodZoneState.SealedSafe);
            FindFirstObjectByType<NetworkRoundState>()?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, timeBonusSeconds);
            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(sealedColor);
            return true;
        }

        public bool ServerJam(ulong instigatorClientId, float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            if (linkedZone != null && linkedZone.CurrentState == FloodZoneState.SealedSafe)
            {
                linkedZone.TrySetState(FloodZoneState.Wet);
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            FindFirstObjectByType<NetworkRoundState>()?.ServerApplySabotagePressure(instigatorClientId, reason, jamTimePenaltySeconds);
            ApplyColor(jammedColor);
            return true;
        }

        public bool ServerForceCooldown(float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            if (linkedZone != null && linkedZone.CurrentState == FloodZoneState.SealedSafe)
            {
                linkedZone.TrySetState(FloodZoneState.Wet);
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, reason, 0f);
            ApplyColor(jammedColor);
            return true;
        }

        private void HandleCooldownChanged(float previous, float current)
        {
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            if (linkedZone != null && linkedZone.CurrentState == FloodZoneState.SealedSafe)
            {
                ApplyColor(sealedColor);
                return;
            }

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
