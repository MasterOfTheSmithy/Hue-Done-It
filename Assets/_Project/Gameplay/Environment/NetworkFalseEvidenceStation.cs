// File: Assets/_Project/Gameplay/Environment/NetworkFalseEvidenceStation.cs
using HueDoneIt.Evidence;
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
    public sealed class NetworkFalseEvidenceStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "false-evidence-station";
        [SerializeField] private string displayName = "Residue Smear Kit";
        [SerializeField] private string smearPrompt = "Plant False Residue";

        [Header("Decoy")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 72f;
        [SerializeField, Min(5f)] private float decoyLifetimeSeconds = 90f;
        [SerializeField, Min(0f)] private float timePenaltySeconds = 6f;
        [SerializeField, Min(0.1f)] private float spawnOffset = 1.2f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.88f, 0.16f, 1f, 1f);
        [SerializeField] private Color usedColor = new(1f, 0.72f, 0.18f, 1f);
        [SerializeField] private Color cooldownColor = new(0.26f, 0.18f, 0.32f, 1f);

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

        public void ConfigureRuntime(string id, string label, string prompt, float cooldown, float lifetime, float penalty, float offset, Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            smearPrompt = string.IsNullOrWhiteSpace(prompt) ? smearPrompt : prompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            decoyLifetimeSeconds = Mathf.Max(5f, lifetime);
            timePenaltySeconds = Mathf.Max(0f, penalty);
            spawnOffset = Mathf.Max(0.1f, offset);
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

            if (!isBleach)
            {
                return displayName + ": suspicious smear kit";
            }

            if (!IsReady)
            {
                return $"{displayName}: residue drying {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return $"{smearPrompt} ({displayName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || !IsReady)
            {
                return false;
            }

            if (!context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) || roleController.CurrentRole != PlayerRole.Bleach)
            {
                return false;
            }

            SpawnDecoyEvidence(context.InteractorObject, context.InteractorClientId);
            FindFirstObjectByType<NetworkRoundState>()?.ServerApplySabotagePressure(context.InteractorClientId, displayName, timePenaltySeconds);
            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(usedColor);
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

        private void SpawnDecoyEvidence(NetworkObject instigatorObject, ulong instigatorClientId)
        {
            Vector3 forward = instigatorObject != null ? instigatorObject.transform.forward : transform.forward;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = transform.forward.sqrMagnitude > 0.01f ? transform.forward : Vector3.forward;
            }

            Vector3 spawnPosition = transform.position + forward.normalized * spawnOffset + Vector3.up * 0.18f;
            GameObject shardObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shardObject.name = $"DecoyEvidence_{Time.frameCount}";
            shardObject.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            shardObject.transform.localScale = Vector3.one * 0.36f;

            Collider colliderRef = shardObject.GetComponent<Collider>();
            if (colliderRef != null)
            {
                colliderRef.isTrigger = false;
            }

            NetworkObject networkObject = shardObject.GetComponent<NetworkObject>() ?? shardObject.AddComponent<NetworkObject>();
            NetworkEvidenceShard evidence = shardObject.GetComponent<NetworkEvidenceShard>() ?? shardObject.AddComponent<NetworkEvidenceShard>();
            ulong framedClientId = ResolveDecoySuspectClientId(instigatorClientId);
            string clue = framedClientId == ulong.MaxValue
                ? "Contradictory residue was planted here. Trace is intentionally muddy."
                : $"Contradictory residue points toward client {framedClientId}, but the pattern looks smeared.";

            evidence.ConfigureRuntime($"decoy-{Time.frameCount}", "Suspicious Residue Smear", clue, framedClientId, decoyLifetimeSeconds, shardObject.GetComponentInChildren<Renderer>());
            networkObject.Spawn(true);
        }

        private ulong ResolveDecoySuspectClientId(ulong instigatorClientId)
        {
            PlayerKillInputController[] controllers = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                PlayerKillInputController controller = controllers[i];
                if (controller == null || controller.CurrentRole != PlayerRole.Color || controller.NetworkObject == null)
                {
                    continue;
                }

                if (controller.NetworkObject.OwnerClientId == instigatorClientId)
                {
                    continue;
                }

                if (controller.TryGetComponent(out PlayerLifeState lifeState) && lifeState.IsAlive)
                {
                    return controller.NetworkObject.OwnerClientId;
                }
            }

            return ulong.MaxValue;
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
