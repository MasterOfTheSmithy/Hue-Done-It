// File: Assets/_Project/Evidence/NetworkEvidenceShard.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Evidence
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkEvidenceShard : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string evidenceId = "evidence-shard";
        [SerializeField] private string displayName = "Paint Evidence";
        [SerializeField] private string inspectPrompt = "Inspect Evidence";
        [SerializeField] private string tamperPrompt = "Smear Evidence";

        [Header("Lifecycle")]
        [SerializeField, Min(1f)] private float autoExpireSeconds = 180f;
        [SerializeField, Min(0f)] private float tamperTimePenaltySeconds = 3f;
        [SerializeField, Min(0f)] private float inspectionTimeBonusSeconds = 2f;
        [SerializeField] private string initialClueText = "Bleach residue found near the body.";
        [SerializeField] private ulong initialSuspectedClientId = ulong.MaxValue;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color freshColor = new(1f, 0.95f, 0.24f, 1f);
        [SerializeField] private Color inspectedColor = new(0.25f, 1f, 0.75f, 1f);
        [SerializeField] private Color tamperedColor = new(0.55f, 0.16f, 0.65f, 1f);

        private readonly NetworkVariable<bool> _isInspected =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isTampered =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _expireAtServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _suspectedClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString128Bytes> _clueText =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string EvidenceId => evidenceId;
        public string DisplayName => displayName;
        public bool IsInspected => _isInspected.Value;
        public bool IsTampered => _isTampered.Value;
        public bool IsActionable => !IsInspected && !IsTampered;
        public ulong SuspectedClientId => _suspectedClientId.Value;
        public string ClueText => _clueText.Value.ToString();

        protected override void Awake()
        {
            base.Awake();
            statusRenderer ??= GetComponentInChildren<Renderer>();
            ApplyCurrentColor();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _isInspected.OnValueChanged += HandleStateChanged;
            _isTampered.OnValueChanged += HandleStateChanged;
            ApplyCurrentColor();

            if (IsServer)
            {
                if (_expireAtServerTime.Value <= 0f)
                {
                    _expireAtServerTime.Value = GetServerTime() + autoExpireSeconds;
                }

                if (_clueText.Value.Length == 0)
                {
                    _clueText.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString128(string.IsNullOrWhiteSpace(initialClueText) ? "Bleach residue found near the body." : initialClueText);
                }

                if (_suspectedClientId.Value == ulong.MaxValue)
                {
                    _suspectedClientId.Value = initialSuspectedClientId;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _isInspected.OnValueChanged -= HandleStateChanged;
            _isTampered.OnValueChanged -= HandleStateChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            ApplyCurrentColor();

            if (!IsServer || !IsSpawned || _expireAtServerTime.Value <= 0f)
            {
                return;
            }

            if (GetServerTime() >= _expireAtServerTime.Value && !IsInspected)
            {
                NetworkObject.Despawn(true);
            }
        }

        public void ConfigureRuntime(string id, string label, string clue, ulong suspectedClientId, float expireSeconds, Renderer renderer = null)
        {
            evidenceId = string.IsNullOrWhiteSpace(id) ? evidenceId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            initialClueText = string.IsNullOrWhiteSpace(clue) ? "Bleach residue found near the body." : clue;
            initialSuspectedClientId = suspectedClientId;
            autoExpireSeconds = Mathf.Max(1f, expireSeconds);
            statusRenderer = renderer != null ? renderer : GetComponentInChildren<Renderer>();

            if (IsServer && IsSpawned)
            {
                _clueText.Value = HueDoneIt.Core.Netcode.FixedStringUtility.ToFixedString128(initialClueText);
                _suspectedClientId.Value = initialSuspectedClientId;
                _expireAtServerTime.Value = GetServerTime() + autoExpireSeconds;
            }

            ApplyCurrentColor();
        }

        public override bool CanInteract(in InteractionContext context)
        {
            if (context.InteractorObject == null || IsInspected)
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
            if (IsInspected)
            {
                return displayName + ": logged";
            }

            if (IsTampered)
            {
                return displayName + ": smeared residue";
            }

            if (context.InteractorObject != null &&
                context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                roleController.CurrentRole == PlayerRole.Bleach)
            {
                return $"{tamperPrompt} ({displayName})";
            }

            return $"{inspectPrompt} ({displayName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null || IsInspected)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) &&
                roleController.CurrentRole == PlayerRole.Bleach)
            {
                return ServerTryTamper(context.InteractorClientId);
            }

            return ServerTryInspect(context.InteractorClientId);
        }

        public bool ServerTryInspect(ulong inspectorClientId)
        {
            if (!IsServer || IsInspected || IsTampered)
            {
                return false;
            }

            _isInspected.Value = true;
            string suspect = _suspectedClientId.Value == ulong.MaxValue ? "unknown" : $"client {_suspectedClientId.Value}";
            string clue = string.IsNullOrWhiteSpace(ClueText) ? "Fresh bleach residue logged." : ClueText;
            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            roundState?.ServerAnnounceEnvironmentalEvent(displayName, $"Evidence logged by client {inspectorClientId}. {clue} Suspect trace: {suspect}.", 0f);
            roundState?.ServerApplyCrewStabilization(inspectorClientId, displayName, inspectionTimeBonusSeconds);
            ApplyCurrentColor();
            return true;
        }

        public bool ServerTryTamper(ulong tamperClientId)
        {
            if (!IsServer || IsInspected || IsTampered)
            {
                return false;
            }

            _isTampered.Value = true;
            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            roundState?.ServerApplySabotagePressure(tamperClientId, displayName + " tamper", tamperTimePenaltySeconds);
            ApplyCurrentColor();
            return true;
        }

        private void HandleStateChanged(bool previous, bool current)
        {
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            Color color = IsTampered ? tamperedColor : (IsInspected ? inspectedColor : freshColor);
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
