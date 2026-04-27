// File: Assets/_Project/Gameplay/Environment/NetworkSecurityCameraStation.cs
using HueDoneIt.Evidence;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Gameplay.Sabotage;
using HueDoneIt.Roles;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Environment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkSecurityCameraStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "security-camera";
        [SerializeField] private string displayName = "Security Camera Sweep";
        [SerializeField] private string sweepPrompt = "Run Camera Sweep";
        [SerializeField] private string jamPrompt = "Loop Camera Feed";

        [Header("Sweep")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 44f;
        [SerializeField, Min(1f)] private float scanRadius = 13f;
        [SerializeField, Min(0f)] private float timeBonusSeconds = 2f;
        [SerializeField, Min(0f)] private float jamTimePenaltySeconds = 5f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.2f, 0.75f, 1f, 1f);
        [SerializeField] private Color usedColor = new(0.88f, 0.95f, 1f, 1f);
        [SerializeField] private Color cooldownColor = new(0.18f, 0.24f, 0.32f, 1f);
        [SerializeField] private Color jammedColor = new(1f, 0.15f, 0.65f, 1f);

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

        public void ConfigureRuntime(string id, string label, string sweepText, string jamText, float cooldown, float radius, float bonusSeconds, float penaltySeconds, Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            sweepPrompt = string.IsNullOrWhiteSpace(sweepText) ? sweepPrompt : sweepText;
            jamPrompt = string.IsNullOrWhiteSpace(jamText) ? jamPrompt : jamText;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            scanRadius = Mathf.Max(1f, radius);
            timeBonusSeconds = Mathf.Max(0f, bonusSeconds);
            jamTimePenaltySeconds = Mathf.Max(0f, penaltySeconds);
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
                return $"{displayName}: cooling {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return isBleach ? $"{jamPrompt} ({displayName})" : $"{sweepPrompt} ({Mathf.RoundToInt(scanRadius)}m)";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return ServerJam(context.InteractorClientId, cooldownSeconds * 0.8f, displayName + " looped");
            }

            string summary = BuildSweepSummary();
            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            roundState?.ServerAnnounceEnvironmentalEvent(displayName, summary, 0f);
            roundState?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, timeBonusSeconds);
            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(usedColor);
            return true;
        }

        public bool ServerJam(ulong instigatorClientId, float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
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

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, reason, 0f);
            ApplyColor(jammedColor);
            return true;
        }

        private string BuildSweepSummary()
        {
            int bleachNearby = CountNearbyBleach(out string closestDirection);
            int armedSabotage = CountReadySabotageConsoles();
            int freshEvidence = CountFreshEvidence();
            int openVents = CountOpenVents();

            string bleachText = bleachNearby > 0
                ? $"Bleach-like movement: {bleachNearby} trace(s), nearest {closestDirection}."
                : "No bleach-like movement in camera radius.";

            return $"{bleachText} Ready sabotage consoles: {armedSabotage}. Fresh evidence: {freshEvidence}. Open vents: {openVents}.";
        }

        private int CountNearbyBleach(out string closestDirection)
        {
            closestDirection = "unclear";
            PlayerKillInputController[] controllers = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            float radiusSqr = scanRadius * scanRadius;
            float bestDistanceSqr = float.MaxValue;
            int count = 0;

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

                Vector3 offset = controller.transform.position - transform.position;
                float distanceSqr = offset.sqrMagnitude;
                if (distanceSqr > radiusSqr)
                {
                    continue;
                }

                count++;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    closestDirection = BuildDirectionLabel(offset);
                }
            }

            return count;
        }

        private static int CountReadySabotageConsoles()
        {
            NetworkSabotageConsole[] consoles = FindObjectsByType<NetworkSabotageConsole>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < consoles.Length; i++)
            {
                if (consoles[i] != null && consoles[i].IsReady)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountFreshEvidence()
        {
            NetworkEvidenceShard[] shards = FindObjectsByType<NetworkEvidenceShard>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i] != null && shards[i].IsActionable)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOpenVents()
        {
            NetworkBleachVent[] vents = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < vents.Length; i++)
            {
                if (vents[i] != null && !vents[i].IsSealed)
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildDirectionLabel(Vector3 direction)
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
