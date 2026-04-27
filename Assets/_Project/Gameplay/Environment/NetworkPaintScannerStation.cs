// File: Assets/_Project/Gameplay/Environment/NetworkPaintScannerStation.cs
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
    public sealed class NetworkPaintScannerStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string scannerId = "paint-scanner";
        [SerializeField] private string displayName = "Paint Evidence Scanner";
        [SerializeField] private string interactPrompt = "Scan Evidence";

        [Header("Detection")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 35f;
        [SerializeField, Min(1f)] private float scanRadius = 9f;
        [SerializeField, Min(0f)] private float sabotageQuarantineSeconds = 12f;
        [SerializeField, Min(0f)] private float roundTimeBonusSeconds = 2f;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.95f, 0.55f, 1f, 1f);
        [SerializeField] private Color scanningColor = new(1f, 1f, 0.45f, 1f);
        [SerializeField] private Color cooldownColor = new(0.24f, 0.18f, 0.30f, 1f);
        [SerializeField] private Color jammedColor = new(1f, 0.18f, 0.32f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string ScannerId => scannerId;
        public string DisplayName => displayName;
        public bool IsReady => CooldownRemaining <= 0.001f;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEndServerTime.Value - GetServerTime());
        public float ScanRadius => scanRadius;

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

        public void ConfigureRuntime(string id, string label, string prompt, float cooldown, float radius, float quarantineSeconds, float timeBonus, Renderer renderer = null)
        {
            scannerId = string.IsNullOrWhiteSpace(id) ? scannerId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            scanRadius = Mathf.Max(1f, radius);
            sabotageQuarantineSeconds = Mathf.Max(0f, quarantineSeconds);
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
            if (context.InteractorObject != null && context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return displayName + ": color forensic terminal";
            }

            if (!IsReady)
            {
                return $"{displayName}: recalibrating {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return $"{interactPrompt} ({Mathf.RoundToInt(scanRadius)}m residue sweep)";
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

            int activeLeaks = CountActiveLeaks();
            int readySabotage = CountReadySabotageConsoles();
            int bleachResidue = CountBleachPlayersInRadius();
            int actionableEvidence = CountActionableEvidenceInRadius();
            string detail = $"Scan result: {activeLeaks} active leak(s), {readySabotage} armed sabotage console(s), {actionableEvidence} fresh evidence shard(s), bleach residue {(bleachResidue > 0 ? "detected" : "not detected")} within {Mathf.RoundToInt(scanRadius)}m.";

            NetworkSabotageConsole quarantined = QuarantineNearestSabotageConsole();
            if (quarantined != null)
            {
                detail += " Nearest sabotage console quarantined.";
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            roundState?.ServerAnnounceEnvironmentalEvent(displayName, detail, 0f);
            if (roundTimeBonusSeconds > 0f)
            {
                roundState?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, roundTimeBonusSeconds);
            }

            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(scanningColor);
            return true;
        }

        public bool ServerJam(float seconds, string reason = "Scanner jammed")
        {
            if (!IsServer)
            {
                return false;
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            ApplyColor(jammedColor);
            return true;
        }

        private int CountActiveLeaks()
        {
            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null && !hazards[i].IsSuppressed)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountReadySabotageConsoles()
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

        private int CountBleachPlayersInRadius()
        {
            PlayerKillInputController[] controllers = FindObjectsByType<PlayerKillInputController>(FindObjectsSortMode.None);
            float radiusSqr = scanRadius * scanRadius;
            int count = 0;
            for (int i = 0; i < controllers.Length; i++)
            {
                PlayerKillInputController controller = controllers[i];
                if (controller == null || controller.CurrentRole != PlayerRole.Bleach)
                {
                    continue;
                }

                if (controller.TryGetComponent(out PlayerLifeState lifeState) && !lifeState.IsAlive)
                {
                    continue;
                }

                if ((controller.transform.position - transform.position).sqrMagnitude <= radiusSqr)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountActionableEvidenceInRadius()
        {
            NetworkEvidenceShard[] shards = FindObjectsByType<NetworkEvidenceShard>(FindObjectsSortMode.None);
            int count = 0;
            float radiusSqr = scanRadius * scanRadius;
            for (int i = 0; i < shards.Length; i++)
            {
                NetworkEvidenceShard shard = shards[i];
                if (shard == null || !shard.IsActionable)
                {
                    continue;
                }

                if ((shard.transform.position - transform.position).sqrMagnitude <= radiusSqr)
                {
                    count++;
                }
            }

            return count;
        }

        private NetworkSabotageConsole QuarantineNearestSabotageConsole()
        {
            if (sabotageQuarantineSeconds <= 0f)
            {
                return null;
            }

            NetworkSabotageConsole[] consoles = FindObjectsByType<NetworkSabotageConsole>(FindObjectsSortMode.None);
            NetworkSabotageConsole best = null;
            float bestSqr = float.MaxValue;
            float maxSqr = scanRadius * scanRadius;
            for (int i = 0; i < consoles.Length; i++)
            {
                NetworkSabotageConsole console = consoles[i];
                if (console == null || !console.IsReady)
                {
                    continue;
                }

                float distanceSqr = (console.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr <= maxSqr && distanceSqr < bestSqr)
                {
                    bestSqr = distanceSqr;
                    best = console;
                }
            }

            if (best != null)
            {
                best.ServerForceCooldown(sabotageQuarantineSeconds, displayName + " quarantine");
            }

            return best;
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
