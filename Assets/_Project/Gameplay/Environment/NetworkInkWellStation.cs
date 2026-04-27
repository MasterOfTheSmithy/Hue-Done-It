// File: Assets/_Project/Gameplay/Environment/NetworkInkWellStation.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Environment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkInkWellStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "ink-well";
        [SerializeField] private string displayName = "Ink Well";
        [SerializeField] private string restorePrompt = "Reconstitute";
        [SerializeField] private string contaminatePrompt = "Contaminate Well";

        [Header("Recovery")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 32f;
        [SerializeField, Range(0f, 1f)] private float saturationReduction01 = 0.28f;
        [SerializeField, Min(0f)] private float staminaRestoreAmount = 28f;
        [SerializeField, Min(0f)] private float roundTimeBonusSeconds = 2.5f;
        [SerializeField, Min(0f)] private float contaminateTimePenaltySeconds = 4f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.95f, 0.25f, 1f, 1f);
        [SerializeField] private Color usedColor = new(0.25f, 1f, 0.75f, 1f);
        [SerializeField] private Color cooldownColor = new(0.22f, 0.18f, 0.28f, 1f);
        [SerializeField] private Color contaminatedColor = new(0.95f, 0.95f, 1f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string StationId => stationId;
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

        public void ConfigureRuntime(string id, string label, string restoreText, string contaminateText, float cooldown, float saturationReduction, float staminaRestore, float timeBonus, float timePenalty, Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            restorePrompt = string.IsNullOrWhiteSpace(restoreText) ? restorePrompt : restoreText;
            contaminatePrompt = string.IsNullOrWhiteSpace(contaminateText) ? contaminatePrompt : contaminateText;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            saturationReduction01 = Mathf.Clamp01(saturationReduction);
            staminaRestoreAmount = Mathf.Max(0f, staminaRestore);
            roundTimeBonusSeconds = Mathf.Max(0f, timeBonus);
            contaminateTimePenaltySeconds = Mathf.Max(0f, timePenalty);
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
            bool isBleach = context.InteractorObject != null &&
                            context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                            roleController.CurrentRole == PlayerRole.Bleach;

            if (!IsReady)
            {
                return $"{displayName}: settling {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return isBleach ? $"{contaminatePrompt} ({displayName})" : $"{restorePrompt} ({displayName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return ServerContaminate(context.InteractorClientId, cooldownSeconds * 0.9f, displayName + " contaminated");
            }

            bool changed = false;
            if (context.InteractorObject.TryGetComponent(out PlayerFloodZoneTracker floodTracker))
            {
                changed |= floodTracker.ServerReduceSaturation(saturationReduction01, displayName);
            }

            if (context.InteractorObject.TryGetComponent(out PlayerStaminaState staminaState))
            {
                changed |= staminaState.ServerRestoreStamina(staminaRestoreAmount);
            }

            if (context.InteractorObject.TryGetComponent(out NetworkPlayerPaintEmitter paintEmitter))
            {
                paintEmitter.ServerEmitPaint(PaintEventKind.TaskInteract, transform.position + Vector3.up * 0.25f, Vector3.up, 0.42f, 0.85f);
            }

            if (changed)
            {
                FindFirstObjectByType<NetworkRoundState>()?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, roundTimeBonusSeconds);
            }

            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(usedColor);
            return true;
        }

        public bool ServerContaminate(ulong instigatorClientId, float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            FindFirstObjectByType<NetworkRoundState>()?.ServerApplySabotagePressure(instigatorClientId, reason, contaminateTimePenaltySeconds);
            ApplyColor(contaminatedColor);
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
