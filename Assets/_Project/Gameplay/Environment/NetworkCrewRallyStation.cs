// File: Assets/_Project/Gameplay/Environment/NetworkCrewRallyStation.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Environment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkCrewRallyStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "crew-rally";
        [SerializeField] private string displayName = "Crew Rally Beacon";
        [SerializeField] private string rallyPrompt = "Start Crew Rally";
        [SerializeField] private string panicPrompt = "Fake Rally Panic";

        [Header("Effect")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 72f;
        [SerializeField, Min(1f)] private float radius = 7f;
        [SerializeField, Min(0f)] private float staminaRestore = 22f;
        [SerializeField, Range(0f, 1f)] private float saturationReduction = 0.18f;
        [SerializeField, Min(0f)] private float timeBonusSeconds = 5f;
        [SerializeField, Min(0f)] private float fakeRallyTimePenaltySeconds = 6f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.22f, 1f, 0.58f, 1f);
        [SerializeField] private Color activeColor = new(0.95f, 1f, 0.25f, 1f);
        [SerializeField] private Color cooldownColor = new(0.17f, 0.23f, 0.18f, 1f);
        [SerializeField] private Color jammedColor = new(1f, 0.22f, 0.62f, 1f);

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

        public void ConfigureRuntime(string id, string label, string rallyText, string panicText, float cooldown, float effectRadius, float staminaAmount, float saturationAmount, float bonusSeconds, float penaltySeconds, Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            rallyPrompt = string.IsNullOrWhiteSpace(rallyText) ? rallyPrompt : rallyText;
            panicPrompt = string.IsNullOrWhiteSpace(panicText) ? panicPrompt : panicText;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            radius = Mathf.Max(1f, effectRadius);
            staminaRestore = Mathf.Max(0f, staminaAmount);
            saturationReduction = Mathf.Clamp01(saturationAmount);
            timeBonusSeconds = Mathf.Max(0f, bonusSeconds);
            fakeRallyTimePenaltySeconds = Mathf.Max(0f, penaltySeconds);
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
                return $"{displayName}: rally cooldown {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            bool isBleach = context.InteractorObject != null &&
                            context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                            roleController.CurrentRole == PlayerRole.Bleach;
            return isBleach ? panicPrompt : $"{rallyPrompt} ({Mathf.RoundToInt(radius)}m)";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            bool isBleach = context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                            roleController.CurrentRole == PlayerRole.Bleach;

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (isBleach)
            {
                roundState?.ServerApplySabotagePressure(context.InteractorClientId, displayName + " fake rally", fakeRallyTimePenaltySeconds);
                _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
                ApplyColor(jammedColor);
                return true;
            }

            int helped = ServerApplyRallyEffects();
            roundState?.ServerApplyCrewStabilization(context.InteractorClientId, $"{displayName} helped {helped} player(s)", timeBonusSeconds);
            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(activeColor);
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
            ApplyColor(jammedColor);
            return true;
        }

        private int ServerApplyRallyEffects()
        {
            int helped = 0;
            PlayerKillInputController[] controllers = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            float radiusSqr = radius * radius;

            for (int i = 0; i < controllers.Length; i++)
            {
                PlayerKillInputController controller = controllers[i];
                if (controller == null || controller.CurrentRole == PlayerRole.Bleach)
                {
                    continue;
                }

                if (!controller.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
                {
                    continue;
                }

                if ((controller.transform.position - transform.position).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                bool changed = false;
                if (controller.TryGetComponent(out PlayerStaminaState staminaState))
                {
                    changed |= staminaState.ServerRestoreStamina(staminaRestore);
                }

                if (controller.TryGetComponent(out PlayerFloodZoneTracker floodTracker))
                {
                    changed |= floodTracker.ServerReduceSaturation(saturationReduction, displayName);
                }

                if (changed)
                {
                    helped++;
                }
            }

            return helped;
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
