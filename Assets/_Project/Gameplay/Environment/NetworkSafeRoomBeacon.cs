// File: Assets/_Project/Gameplay/Environment/NetworkSafeRoomBeacon.cs
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
    public sealed class NetworkSafeRoomBeacon : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string beaconId = "safe-room-beacon";
        [SerializeField] private string displayName = "Safe Room Beacon";
        [SerializeField] private string interactPrompt = "Open Safe Bubble";

        [Header("Effect")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 58f;
        [SerializeField, Min(1f)] private float activeDurationSeconds = 18f;
        [SerializeField, Min(0.5f)] private float effectRadius = 4.75f;
        [SerializeField, Range(0f, 1f)] private float saturationReductionPerSecond = 0.035f;
        [SerializeField, Min(0f)] private float staminaRestorePerSecond = 4.5f;
        [SerializeField, Min(0f)] private float roundTimeBonusSeconds = 4f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.2f, 1f, 0.62f, 1f);
        [SerializeField] private Color activeColor = new(0.35f, 0.95f, 1f, 1f);
        [SerializeField] private Color cooldownColor = new(0.16f, 0.24f, 0.22f, 1f);
        [SerializeField] private Color jammedColor = new(1f, 0.25f, 0.75f, 1f);

        private readonly NetworkVariable<float> _activeEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;
        private float _nextRoundAnnounceTime;

        public string BeaconId => beaconId;
        public string DisplayName => displayName;
        public bool IsActive => ActiveRemaining > 0.001f;
        public bool IsReady => !IsActive && CooldownRemaining <= 0.001f;
        public float ActiveRemaining => Mathf.Max(0f, _activeEndServerTime.Value - GetServerTime());
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEndServerTime.Value - GetServerTime());
        public float EffectRadius => effectRadius;

        protected override void Awake()
        {
            base.Awake();
            statusRenderer ??= GetComponentInChildren<Renderer>();
            ApplyColor(readyColor);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _activeEndServerTime.OnValueChanged += HandleTimedStateChanged;
            _cooldownEndServerTime.OnValueChanged += HandleTimedStateChanged;
            ApplyCurrentColor();
        }

        public override void OnNetworkDespawn()
        {
            _activeEndServerTime.OnValueChanged -= HandleTimedStateChanged;
            _cooldownEndServerTime.OnValueChanged -= HandleTimedStateChanged;
            base.OnNetworkDespawn();
        }

        private void FixedUpdate()
        {
            ApplyCurrentColor();

            if (!IsServer || !IsActive)
            {
                return;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState != null && !roundState.IsFreeRoam)
            {
                return;
            }

            ApplyProtectionToPlayers(Time.fixedDeltaTime);
        }

        public void ConfigureRuntime(string id, string label, string prompt, float cooldown, float duration, float radius, float saturationReduction, float staminaRestore, float timeBonus, Renderer renderer = null)
        {
            beaconId = string.IsNullOrWhiteSpace(id) ? beaconId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            activeDurationSeconds = Mathf.Max(1f, duration);
            effectRadius = Mathf.Max(0.5f, radius);
            saturationReductionPerSecond = Mathf.Clamp01(saturationReduction);
            staminaRestorePerSecond = Mathf.Max(0f, staminaRestore);
            roundTimeBonusSeconds = Mathf.Max(0f, timeBonus);
            statusRenderer = renderer != null ? renderer : GetComponentInChildren<Renderer>();
            ApplyCurrentColor();
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
            if (context.InteractorObject != null && context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return displayName + ": color refuge controls";
            }

            if (IsActive)
            {
                return $"{displayName}: shelter active {Mathf.CeilToInt(ActiveRemaining)}s";
            }

            if (!IsReady)
            {
                return $"{displayName}: recharging {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return $"{interactPrompt} ({Mathf.RoundToInt(effectRadius)}m refuge)";
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

            float now = GetServerTime();
            _activeEndServerTime.Value = now + activeDurationSeconds;
            _cooldownEndServerTime.Value = now + activeDurationSeconds + cooldownSeconds;

            FindFirstObjectByType<NetworkRoundState>()?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, roundTimeBonusSeconds);
            ApplyColor(activeColor);
            return true;
        }

        public bool ServerJam(float seconds, string reason = "Safe room jammed")
        {
            if (!IsServer)
            {
                return false;
            }

            float now = GetServerTime();
            _activeEndServerTime.Value = Mathf.Min(_activeEndServerTime.Value, now);
            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, now + Mathf.Max(1f, seconds));
            ApplyColor(jammedColor);
            return true;
        }

        private void ApplyProtectionToPlayers(float deltaTime)
        {
            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            float radiusSqr = effectRadius * effectRadius;
            int affected = 0;

            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (avatar == null || !avatar.IsSpawned)
                {
                    continue;
                }

                if ((avatar.transform.position - transform.position).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                if (avatar.TryGetComponent(out PlayerLifeState lifeState) && !lifeState.IsAlive)
                {
                    continue;
                }

                if (avatar.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach)
                {
                    continue;
                }

                bool changed = false;
                if (avatar.TryGetComponent(out PlayerFloodZoneTracker floodTracker))
                {
                    changed |= floodTracker.ServerReduceSaturation(saturationReductionPerSecond * deltaTime, displayName);
                }

                if (avatar.TryGetComponent(out PlayerStaminaState staminaState))
                {
                    changed |= staminaState.ServerRestoreStamina(staminaRestorePerSecond * deltaTime);
                }

                if (changed)
                {
                    affected++;
                }
            }

            if (affected > 0 && Time.time >= _nextRoundAnnounceTime)
            {
                _nextRoundAnnounceTime = Time.time + 4f;
                FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, $"Refuge bubble stabilizing {affected} color player(s).", 0f);
            }
        }

        private void HandleTimedStateChanged(float previous, float current)
        {
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            if (IsActive)
            {
                ApplyColor(activeColor);
            }
            else if (IsReady)
            {
                ApplyColor(readyColor);
            }
            else
            {
                ApplyColor(cooldownColor);
            }
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
