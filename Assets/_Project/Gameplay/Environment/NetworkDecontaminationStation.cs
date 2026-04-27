// File: Assets/_Project/Gameplay/Environment/NetworkDecontaminationStation.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Environment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkDecontaminationStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "decontamination-station";
        [SerializeField] private string displayName = "Decontamination Shower";
        [SerializeField] private string interactPrompt = "Decontaminate";

        [Header("Effect")]
        [SerializeField, Range(0f, 1f)] private float saturationReduction01 = 0.42f;
        [SerializeField, Min(0f)] private float staminaRestoreAmount = 35f;
        [SerializeField, Min(1f)] private float cooldownSeconds = 32f;
        [SerializeField, Min(0f)] private float roundTimeBonusSeconds = 4f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.15f, 0.95f, 1f, 1f);
        [SerializeField] private Color cooldownColor = new(0.18f, 0.26f, 0.34f, 1f);
        [SerializeField] private Color usedColor = new(0.95f, 1f, 0.55f, 1f);

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
            float cooldown,
            float saturationReduction,
            float staminaRestore,
            float timeBonus,
            Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            saturationReduction01 = Mathf.Clamp01(saturationReduction);
            staminaRestoreAmount = Mathf.Max(0f, staminaRestore);
            roundTimeBonusSeconds = Mathf.Max(0f, timeBonus);
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
            if (!IsReady)
            {
                return $"{displayName}: recycling {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            float saturation = 0f;
            float stamina = 1f;
            if (context.InteractorObject != null)
            {
                if (context.InteractorObject.TryGetComponent(out PlayerFloodZoneTracker floodTracker))
                {
                    saturation = floodTracker.Saturation01;
                }

                if (context.InteractorObject.TryGetComponent(out PlayerStaminaState staminaState))
                {
                    stamina = staminaState.Normalized;
                }
            }

            if (saturation <= 0.02f && stamina >= 0.98f)
            {
                return $"{displayName}: clean / stable";
            }

            return $"{interactPrompt}: -{Mathf.RoundToInt(saturationReduction01 * 100f)}% saturation / +{Mathf.RoundToInt(staminaRestoreAmount)} stability";
        }

        public bool ServerForceCooldown(float seconds, string reason = "Contaminated")
        {
            if (!IsServer)
            {
                return false;
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            ApplyColor(cooldownColor);
            return true;
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
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

            if (!changed)
            {
                return false;
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
