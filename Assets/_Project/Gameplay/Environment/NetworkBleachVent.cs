// File: Assets/_Project/Gameplay/Environment/NetworkBleachVent.cs
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
    public sealed class NetworkBleachVent : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string ventId = "bleach-vent";
        [SerializeField] private string displayName = "Bleach Vent";
        [SerializeField] private string bleachPrompt = "Slip Through Vent";
        [SerializeField] private string sealPrompt = "Seal Vent";

        [Header("Rules")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 16f;
        [SerializeField, Min(1f)] private float sealDurationSeconds = 42f;
        [SerializeField, Min(0f)] private float exitForwardOffset = 1.2f;
        [SerializeField, Min(0f)] private float crewTimeBonusSeconds = 2f;
        [SerializeField] private NetworkBleachVent linkedVent;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.86f, 0.10f, 1f, 1f);
        [SerializeField] private Color cooldownColor = new(0.24f, 0.12f, 0.30f, 1f);
        [SerializeField] private Color sealedColor = new(0.20f, 0.95f, 0.62f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _sealedEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string VentId => ventId;
        public string DisplayName => displayName;
        public bool IsSealed => _sealedEndServerTime.Value > GetServerTime();
        public bool IsReady => !IsSealed && CooldownRemaining <= 0.001f && linkedVent != null;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEndServerTime.Value - GetServerTime());
        public float SealedRemaining => Mathf.Max(0f, _sealedEndServerTime.Value - GetServerTime());
        public NetworkBleachVent LinkedVent => linkedVent;

        protected override void Awake()
        {
            base.Awake();
            statusRenderer ??= GetComponentInChildren<Renderer>();
            ApplyCurrentColor();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _cooldownEndServerTime.OnValueChanged += HandleTimedStateChanged;
            _sealedEndServerTime.OnValueChanged += HandleTimedStateChanged;
            ApplyCurrentColor();
        }

        public override void OnNetworkDespawn()
        {
            _cooldownEndServerTime.OnValueChanged -= HandleTimedStateChanged;
            _sealedEndServerTime.OnValueChanged -= HandleTimedStateChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            ApplyCurrentColor();
        }

        public void ConfigureRuntime(string id, string label, string bleachUsePrompt, string colorSealPrompt, float cooldown, float sealDuration, float timeBonus, NetworkBleachVent linked, Renderer renderer = null)
        {
            ventId = string.IsNullOrWhiteSpace(id) ? ventId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            bleachPrompt = string.IsNullOrWhiteSpace(bleachUsePrompt) ? bleachPrompt : bleachUsePrompt;
            sealPrompt = string.IsNullOrWhiteSpace(colorSealPrompt) ? sealPrompt : colorSealPrompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            sealDurationSeconds = Mathf.Max(1f, sealDuration);
            crewTimeBonusSeconds = Mathf.Max(0f, timeBonus);
            linkedVent = linked;
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

            if (IsSealed)
            {
                return $"{displayName}: sealed {Mathf.CeilToInt(SealedRemaining)}s";
            }

            if (isBleach)
            {
                if (!IsReady)
                {
                    return $"{displayName}: vent slime cooling {Mathf.CeilToInt(CooldownRemaining)}s";
                }

                return $"{bleachPrompt} ({displayName})";
            }

            return $"{sealPrompt} ({displayName})";
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
                return ServerTryUseVent(context.InteractorObject, context.InteractorClientId);
            }

            return ServerTrySeal(context.InteractorClientId);
        }

        public bool ServerUnseal(string reason = "Vent reopened")
        {
            if (!IsServer || !IsSealed)
            {
                return false;
            }

            _sealedEndServerTime.Value = 0f;
            FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, reason, 0f);
            ApplyCurrentColor();
            return true;
        }

        private bool ServerTryUseVent(NetworkObject userObject, ulong clientId)
        {
            if (!IsServer || userObject == null || linkedVent == null || !IsReady)
            {
                return false;
            }

            Vector3 exitPosition = linkedVent.transform.position + (linkedVent.transform.forward * exitForwardOffset) + (Vector3.up * 0.2f);
            float yaw = linkedVent.transform.eulerAngles.y;
            if (userObject.TryGetComponent(out PlayerKillInputController ventUserRole))
            {
                ventUserRole.ServerExposeBleach(15f, "Vent trace");
            }

            if (userObject.TryGetComponent(out NetworkPlayerAuthoritativeMover mover))
            {
                mover.ServerTeleportTo(exitPosition, yaw);
            }
            else
            {
                userObject.transform.SetPositionAndRotation(exitPosition, Quaternion.Euler(0f, yaw, 0f));
            }

            float now = GetServerTime();
            _cooldownEndServerTime.Value = now + cooldownSeconds;
            linkedVent._cooldownEndServerTime.Value = Mathf.Max(linkedVent._cooldownEndServerTime.Value, now + cooldownSeconds * 0.5f);
            FindFirstObjectByType<NetworkRoundState>()?.ServerAnnounceEnvironmentalEvent(displayName, $"Vent vibration detected near {linkedVent.DisplayName}.", 0f);
            ApplyCurrentColor();
            linkedVent.ApplyCurrentColor();
            return true;
        }

        private bool ServerTrySeal(ulong clientId)
        {
            if (!IsServer || IsSealed)
            {
                return false;
            }

            float until = GetServerTime() + sealDurationSeconds;
            _sealedEndServerTime.Value = until;
            if (linkedVent != null)
            {
                linkedVent._sealedEndServerTime.Value = Mathf.Max(linkedVent._sealedEndServerTime.Value, until);
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            roundState?.ServerApplyCrewStabilization(clientId, displayName + " sealed", crewTimeBonusSeconds);
            ApplyCurrentColor();
            linkedVent?.ApplyCurrentColor();
            return true;
        }

        private void HandleTimedStateChanged(float previous, float current)
        {
            ApplyCurrentColor();
        }

        private void ApplyCurrentColor()
        {
            Color color = IsSealed ? sealedColor : (IsReady ? readyColor : cooldownColor);
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
