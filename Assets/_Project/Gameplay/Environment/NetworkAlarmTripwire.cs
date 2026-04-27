// File: Assets/_Project/Gameplay/Environment/NetworkAlarmTripwire.cs
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
    public sealed class NetworkAlarmTripwire : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string tripwireId = "alarm-tripwire";
        [SerializeField] private string displayName = "Paint Alarm Tripwire";
        [SerializeField] private string armPrompt = "Arm Tripwire";
        [SerializeField] private string jamPrompt = "Short Tripwire";

        [Header("Detection")]
        [SerializeField, Min(0.5f)] private float detectionRadius = 4.25f;
        [SerializeField, Min(1f)] private float armedDurationSeconds = 70f;
        [SerializeField, Min(1f)] private float cooldownSeconds = 36f;
        [SerializeField, Min(0f)] private float jamTimePenaltySeconds = 4f;
        [SerializeField, Min(0f)] private float armTimeBonusSeconds = 1.5f;
        [SerializeField, Min(0.05f)] private float scanIntervalSeconds = 0.25f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.2f, 0.95f, 0.55f, 1f);
        [SerializeField] private Color armedColor = new(1f, 0.92f, 0.22f, 1f);
        [SerializeField] private Color triggeredColor = new(1f, 0.18f, 0.18f, 1f);
        [SerializeField] private Color cooldownColor = new(0.22f, 0.22f, 0.28f, 1f);

        private readonly NetworkVariable<float> _armedEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _lastTriggeredByClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;
        private float _nextScanTime;

        public string TripwireId => tripwireId;
        public string DisplayName => displayName;
        public bool IsArmed => ArmedRemaining > 0.001f;
        public bool IsReady => !IsArmed && CooldownRemaining <= 0.001f;
        public float ArmedRemaining => Mathf.Max(0f, _armedEndServerTime.Value - GetServerTime());
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEndServerTime.Value - GetServerTime());
        public ulong LastTriggeredByClientId => _lastTriggeredByClientId.Value;

        protected override void Awake()
        {
            base.Awake();
            statusRenderer ??= GetComponentInChildren<Renderer>();
            ApplyCurrentColor();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _armedEndServerTime.OnValueChanged += HandleTimedStateChanged;
            _cooldownEndServerTime.OnValueChanged += HandleTimedStateChanged;
            ApplyCurrentColor();
        }

        public override void OnNetworkDespawn()
        {
            _armedEndServerTime.OnValueChanged -= HandleTimedStateChanged;
            _cooldownEndServerTime.OnValueChanged -= HandleTimedStateChanged;
            base.OnNetworkDespawn();
        }

        private void FixedUpdate()
        {
            ApplyCurrentColor();

            if (!IsServer || !IsArmed || Time.time < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.time + scanIntervalSeconds;
            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState != null && !roundState.IsFreeRoam)
            {
                return;
            }

            ScanForBleachIntrusion();
        }

        public void ConfigureRuntime(string id, string label, string armText, string jamText, float radius, float armedDuration, float cooldown, float timeBonus, float jamPenalty, Renderer renderer = null)
        {
            tripwireId = string.IsNullOrWhiteSpace(id) ? tripwireId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            armPrompt = string.IsNullOrWhiteSpace(armText) ? armPrompt : armText;
            jamPrompt = string.IsNullOrWhiteSpace(jamText) ? jamPrompt : jamText;
            detectionRadius = Mathf.Max(0.5f, radius);
            armedDurationSeconds = Mathf.Max(1f, armedDuration);
            cooldownSeconds = Mathf.Max(1f, cooldown);
            armTimeBonusSeconds = Mathf.Max(0f, timeBonus);
            jamTimePenaltySeconds = Mathf.Max(0f, jamPenalty);
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
            bool isBleach = context.InteractorObject != null &&
                            context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                            roleController.CurrentRole == PlayerRole.Bleach;

            if (isBleach)
            {
                return IsArmed || IsReady ? $"{jamPrompt} ({displayName})" : $"{displayName}: discharged {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            if (IsArmed)
            {
                return $"{displayName}: armed {Mathf.CeilToInt(ArmedRemaining)}s";
            }

            if (!IsReady)
            {
                return $"{displayName}: rearming {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return $"{armPrompt} ({Mathf.RoundToInt(detectionRadius)}m alarm)";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null)
            {
                return false;
            }

            bool isBleach = context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach;
            if (isBleach)
            {
                return ServerJam(context.InteractorClientId, cooldownSeconds * 0.75f, displayName + " shorted");
            }

            if (!IsReady)
            {
                return false;
            }

            float now = GetServerTime();
            _armedEndServerTime.Value = now + armedDurationSeconds;
            _cooldownEndServerTime.Value = now + armedDurationSeconds + cooldownSeconds;
            _lastTriggeredByClientId.Value = ulong.MaxValue;
            FindFirstObjectByType<NetworkRoundState>()?.ServerApplyCrewStabilization(context.InteractorClientId, displayName + " armed", armTimeBonusSeconds);
            ApplyColor(armedColor);
            return true;
        }

        public bool ServerJam(ulong instigatorClientId, float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            float now = GetServerTime();
            _armedEndServerTime.Value = 0f;
            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, now + Mathf.Max(1f, seconds));
            FindFirstObjectByType<NetworkRoundState>()?.ServerApplySabotagePressure(instigatorClientId, reason, jamTimePenaltySeconds);
            ApplyColor(cooldownColor);
            return true;
        }

        public bool ServerForceCooldown(float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            _armedEndServerTime.Value = 0f;
            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, reason, 0f);
            ApplyColor(cooldownColor);
            return true;
        }

        private void ScanForBleachIntrusion()
        {
            PlayerKillInputController[] controllers = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            float radiusSqr = detectionRadius * detectionRadius;
            for (int i = 0; i < controllers.Length; i++)
            {
                PlayerKillInputController controller = controllers[i];
                if (controller == null || controller.CurrentRole != PlayerRole.Bleach)
                {
                    continue;
                }

                if (!controller.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
                {
                    continue;
                }

                float distanceSqr = (controller.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr > radiusSqr)
                {
                    continue;
                }

                TriggerAlarm(controller);
                return;
            }
        }

        private void TriggerAlarm(PlayerKillInputController controller)
        {
            float now = GetServerTime();
            ulong clientId = controller.NetworkObject != null ? controller.NetworkObject.OwnerClientId : ulong.MaxValue;
            _lastTriggeredByClientId.Value = clientId;
            _armedEndServerTime.Value = 0f;
            _cooldownEndServerTime.Value = now + cooldownSeconds;

            string direction = BuildDirectionLabel(controller.transform.position - transform.position);
            FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, $"Alarm detected bleach movement {direction} of {displayName}.", 0f);

            if (controller.TryGetComponent(out NetworkPlayerPaintEmitter paintEmitter))
            {
                paintEmitter.ServerEmitPaint(PaintEventKind.RagdollImpact, controller.transform.position + Vector3.up * 0.1f, Vector3.up, 0.32f, 0.9f);
            }

            ApplyColor(triggeredColor);
        }

        private string BuildDirectionLabel(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
            {
                return "inside";
            }

            direction.Normalize();
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
            {
                return direction.x >= 0f ? "east" : "west";
            }

            return direction.z >= 0f ? "north" : "south";
        }

        private void HandleTimedStateChanged(float previous, float current)
        {
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            if (IsArmed)
            {
                ApplyColor(armedColor);
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
