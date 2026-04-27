// File: Assets/_Project/Gameplay/Environment/NetworkVitalsStation.cs
using HueDoneIt.Flood.Integration;
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
    public sealed class NetworkVitalsStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "vitals-station";
        [SerializeField] private string displayName = "Vitals Monitor";
        [SerializeField] private string interactPrompt = "Check Vitals";

        [Header("Rules")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 34f;
        [SerializeField, Min(0f)] private float roundTimeBonusSeconds = 1.5f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.24f, 1f, 0.95f, 1f);
        [SerializeField] private Color cooldownColor = new(0.12f, 0.23f, 0.26f, 1f);
        [SerializeField] private Color jammedColor = new(1f, 0.20f, 0.35f, 1f);

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

        public void ConfigureRuntime(string id, string label, string prompt, float cooldown, float timeBonus, Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
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
            if (context.InteractorObject != null &&
                context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                roleController.CurrentRole == PlayerRole.Bleach)
            {
                return displayName + ": color-only vitals feed";
            }

            if (!IsReady)
            {
                return $"{displayName}: refreshing {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return $"{interactPrompt} ({displayName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return false;
            }

            string result = BuildVitalsSummary();
            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            roundState?.ServerAnnounceEnvironmentalEvent(displayName, result, 0f);
            roundState?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, roundTimeBonusSeconds);

            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(cooldownColor);
            return true;
        }

        public bool ServerJam(float seconds, string reason = "Vitals station jammed")
        {
            if (!IsServer)
            {
                return false;
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            ApplyColor(jammedColor);
            return true;
        }

        private string BuildVitalsSummary()
        {
            PlayerLifeState[] lifeStates = FindObjectsByType<PlayerLifeState>(FindObjectsSortMode.None);
            int alive = 0;
            int eliminated = 0;
            int diffused = 0;
            int criticalSaturation = 0;

            for (int i = 0; i < lifeStates.Length; i++)
            {
                PlayerLifeState life = lifeStates[i];
                if (life == null)
                {
                    continue;
                }

                if (life.IsAlive)
                {
                    alive++;
                }
                else if (life.CurrentLifeState == PlayerLifeStateKind.DiffusedByFlood)
                {
                    diffused++;
                }
                else
                {
                    eliminated++;
                }

                if (life.TryGetComponent(out PlayerFloodZoneTracker floodTracker) && floodTracker.IsCritical)
                {
                    criticalSaturation++;
                }
            }

            PlayerRemains[] remains = FindObjectsByType<PlayerRemains>(FindObjectsSortMode.None);
            int unreportedBodies = 0;
            for (int i = 0; i < remains.Length; i++)
            {
                if (remains[i] != null && !remains[i].IsReported)
                {
                    unreportedBodies++;
                }
            }

            return $"Vitals: {alive} alive, {eliminated} eliminated, {diffused} diffused, {unreportedBodies} unreported body marker(s), {criticalSaturation} critical saturation warning(s).";
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
