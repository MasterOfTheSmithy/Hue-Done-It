// File: Assets/_Project/Gameplay/Environment/NetworkEmergencyMeetingConsole.cs
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
    public sealed class NetworkEmergencyMeetingConsole : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string consoleId = "emergency-meeting";
        [SerializeField] private string displayName = "Emergency Meeting Console";
        [SerializeField] private string interactPrompt = "Call Emergency Meeting";

        [Header("Rules")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 150f;
        [SerializeField, Min(0f)] private float roundTimePenaltySeconds = 6f;
        [SerializeField] private bool colorPlayersOnly = true;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(1f, 0.25f, 0.25f, 1f);
        [SerializeField] private Color cooldownColor = new(0.35f, 0.16f, 0.16f, 1f);
        [SerializeField] private Color calledColor = new(1f, 0.85f, 0.25f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string ConsoleId => consoleId;
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

        public void ConfigureRuntime(string id, string label, string prompt, float cooldown, float timePenalty, Renderer renderer = null)
        {
            consoleId = string.IsNullOrWhiteSpace(id) ? consoleId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            roundTimePenaltySeconds = Mathf.Max(0f, timePenalty);
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
                return $"{displayName}: locked {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            if (context.InteractorObject != null && context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && colorPlayersOnly && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return displayName + ": too public for bleach";
            }

            return interactPrompt;
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && colorPlayersOnly && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState == null || !roundState.ServerTryCallEmergencyVote(context.InteractorClientId, displayName, roundTimePenaltySeconds))
            {
                return false;
            }

            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(calledColor);
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
