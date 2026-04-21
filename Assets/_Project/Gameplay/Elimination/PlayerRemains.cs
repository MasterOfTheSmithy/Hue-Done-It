// File: Assets/_Project/Gameplay/Elimination/PlayerRemains.cs
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Round;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Elimination
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerRemains : NetworkInteractable
    {
        [Header("Evidence Presentation")]
        [SerializeField] private Renderer[] evidenceRenderers;
        [SerializeField] private StainReceiver[] nearbyStainReceivers;
        [SerializeField] private Color unreportedEvidenceColor = new(1f, 0.12f, 0.2f, 1f);
        [SerializeField] private Color reportedEvidenceColor = new(0.46f, 0.46f, 0.5f, 1f);
        [SerializeField, Range(0f, 1f)] private float remainsEvidenceIntensity = 0.92f;

        private readonly NetworkVariable<ulong> _victimClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _victimPlayerObjectId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _victimName =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isInitialized =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isReported =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public ulong VictimClientId => _victimClientId.Value;
        public ulong VictimPlayerObjectId => _victimPlayerObjectId.Value;
        public string VictimName => _victimName.Value.ToString();
        public bool IsReported => _isReported.Value;

        private void Awake()
        {
            if (evidenceRenderers == null || evidenceRenderers.Length == 0)
            {
                evidenceRenderers = GetComponentsInChildren<Renderer>(true);
            }

            if (nearbyStainReceivers == null || nearbyStainReceivers.Length == 0)
            {
                nearbyStainReceivers = GetComponentsInChildren<StainReceiver>(true);
            }

            _propertyBlock = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _isReported.OnValueChanged += HandleReportedChanged;
            ApplyEvidenceVisual(_isReported.Value, true);
        }

        public override void OnNetworkDespawn()
        {
            _isReported.OnValueChanged -= HandleReportedChanged;
            base.OnNetworkDespawn();
        }

        public void ServerInitialize(ulong victimClientId, ulong victimPlayerObjectId, string victimName)
        {
            if (!IsServer || _isInitialized.Value)
            {
                return;
            }

            _victimClientId.Value = victimClientId;
            _victimPlayerObjectId.Value = victimPlayerObjectId;
            _victimName.Value = string.IsNullOrWhiteSpace(victimName)
                ? new FixedString64Bytes($"Player {victimClientId}")
                : new FixedString64Bytes(victimName);
            _isInitialized.Value = true;
        }

        public bool ServerTryMarkReported()
        {
            if (!IsServer || _isReported.Value || !_isInitialized.Value)
            {
                return false;
            }

            _isReported.Value = true;
            return true;
        }

        public override bool CanInteract(in InteractionContext context)
        {
            if (!_isInitialized.Value || _isReported.Value)
            {
                return false;
            }

            if (context.InteractorObject == null || !context.InteractorObject.TryGetComponent(out PlayerLifeState lifeState) || !lifeState.IsAlive)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            return roundState == null || roundState.CurrentPhase == RoundPhase.FreeRoam;
        }

        public override string GetPromptText(in InteractionContext context)
        {
            if (_isReported.Value)
            {
                return "Body already reported";
            }

            if (context.InteractorObject != null &&
                context.InteractorObject.TryGetComponent(out PlayerLifeState lifeState) &&
                !lifeState.IsAlive)
            {
                return "Eliminated players cannot report";
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState != null && roundState.CurrentPhase != RoundPhase.FreeRoam)
            {
                return "Body reporting unavailable";
            }

            string targetName = string.IsNullOrWhiteSpace(VictimName) ? $"Player {VictimClientId}" : VictimName;
            return $"Report Body ({targetName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer)
            {
                return false;
            }

            BodyReportManager reportManager = FindFirstObjectByType<BodyReportManager>();
            if (reportManager == null)
            {
                Debug.LogError("No BodyReportManager found in scene.");
                return false;
            }

            return reportManager.TryReportBody(context.InteractorObject, this);
        }

        private void HandleReportedChanged(bool previous, bool current)
        {
            ApplyEvidenceVisual(current, false);
        }

        private void ApplyEvidenceVisual(bool reported, bool immediate)
        {
            Color targetColor = reported ? reportedEvidenceColor : unreportedEvidenceColor;
            ApplyRendererTint(targetColor);

            if (immediate)
            {
                return;
            }

            for (int i = 0; i < nearbyStainReceivers.Length; i++)
            {
                StainReceiver receiver = nearbyStainReceivers[i];
                if (receiver == null)
                {
                    continue;
                }

                Vector3 offset = new(((i % 3) - 1) * 0.15f, 0f, ((i / 3) - 1) * 0.13f);
                receiver.SpawnEnvironmentalEvidence(
                    targetColor,
                    transform.position + offset,
                    Vector3.up,
                    PaintEventKind.RagdollImpact,
                    !reported,
                    reported ? 0.2f : 0.32f,
                    remainsEvidenceIntensity * (reported ? 0.6f : 1f));
            }
        }

        private void ApplyRendererTint(Color color)
        {
            if (evidenceRenderers == null)
            {
                return;
            }

            for (int i = 0; i < evidenceRenderers.Length; i++)
            {
                Renderer renderer = evidenceRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                _propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
