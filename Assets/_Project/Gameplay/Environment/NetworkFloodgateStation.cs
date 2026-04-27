// File: Assets/_Project/Gameplay/Environment/NetworkFloodgateStation.cs
using HueDoneIt.Flood;
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
    public sealed class NetworkFloodgateStation : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string stationId = "floodgate-station";
        [SerializeField] private string displayName = "Floodgate Control";
        [SerializeField] private string interactPrompt = "Vent Floodgate";

        [Header("Effect")]
        [SerializeField] private FloodZone[] targetZones = new FloodZone[0];
        [SerializeField] private NetworkBleachLeakHazard[] linkedHazards = new NetworkBleachLeakHazard[0];
        [SerializeField, Min(1f)] private float cooldownSeconds = 50f;
        [SerializeField, Min(0f)] private float hazardSuppressionSeconds = 18f;
        [SerializeField, Min(0f)] private float roundTimeBonusSeconds = 5f;
        [SerializeField] private bool colorPlayersOnly = true;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.08f, 0.95f, 0.78f, 1f);
        [SerializeField] private Color cooldownColor = new(0.16f, 0.24f, 0.32f, 1f);
        [SerializeField] private Color usedColor = new(0.92f, 1f, 0.35f, 1f);
        [SerializeField] private Color jammedColor = new(0.95f, 0.24f, 0.92f, 1f);

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

        public void ConfigureRuntime(string id, string label, string prompt, FloodZone[] zones, NetworkBleachLeakHazard[] hazards, float cooldown, float suppressionSeconds, float timeBonusSeconds, Renderer renderer = null)
        {
            stationId = string.IsNullOrWhiteSpace(id) ? stationId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            targetZones = zones ?? new FloodZone[0];
            linkedHazards = hazards ?? new NetworkBleachLeakHazard[0];
            cooldownSeconds = Mathf.Max(1f, cooldown);
            hazardSuppressionSeconds = Mathf.Max(0f, suppressionSeconds);
            roundTimeBonusSeconds = Mathf.Max(0f, timeBonusSeconds);
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
            if (context.InteractorObject != null && context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) && colorPlayersOnly && roleController.CurrentRole == PlayerRole.Bleach)
            {
                return displayName + ": crew-only pressure controls";
            }

            if (!IsReady)
            {
                return $"{displayName}: cycling {Mathf.CeilToInt(CooldownRemaining)}s";
            }

            return $"{interactPrompt} ({CountDangerousZones()} wet routes / {CountActiveLinkedHazards()} leaks)";
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

            int changed = 0;
            if (targetZones != null)
            {
                for (int i = 0; i < targetZones.Length; i++)
                {
                    FloodZone zone = targetZones[i];
                    if (zone == null)
                    {
                        continue;
                    }

                    if (zone.CurrentState is FloodZoneState.Flooding or FloodZoneState.Submerged)
                    {
                        changed += zone.TrySetState(FloodZoneState.Wet) ? 1 : 0;
                    }
                    else if (zone.CurrentState == FloodZoneState.Wet)
                    {
                        changed += zone.TrySetState(FloodZoneState.SealedSafe) ? 1 : 0;
                    }
                }
            }

            if (linkedHazards != null)
            {
                for (int i = 0; i < linkedHazards.Length; i++)
                {
                    NetworkBleachLeakHazard hazard = linkedHazards[i];
                    if (hazard != null && !hazard.IsSuppressed)
                    {
                        changed += hazard.ServerSuppressFor(hazardSuppressionSeconds, displayName + " vented " + hazard.DisplayName) ? 1 : 0;
                    }
                }
            }

            if (changed <= 0)
            {
                return false;
            }

            FindFirstObjectByType<NetworkRoundState>()?.ServerApplyCrewStabilization(context.InteractorClientId, displayName, roundTimeBonusSeconds);
            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(usedColor);
            return true;
        }

        public bool ServerJam(float seconds, string reason)
        {
            if (!IsServer)
            {
                return false;
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            if (targetZones != null)
            {
                for (int i = 0; i < targetZones.Length; i++)
                {
                    FloodZone zone = targetZones[i];
                    if (zone != null && zone.CurrentState == FloodZoneState.SealedSafe)
                    {
                        zone.TrySetState(FloodZoneState.Wet);
                    }
                }
            }

            ApplyColor(jammedColor);
            return true;
        }

        private int CountDangerousZones()
        {
            int count = 0;
            if (targetZones == null)
            {
                return count;
            }

            for (int i = 0; i < targetZones.Length; i++)
            {
                FloodZone zone = targetZones[i];
                if (zone != null && zone.CurrentState is FloodZoneState.Wet or FloodZoneState.Flooding or FloodZoneState.Submerged)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountActiveLinkedHazards()
        {
            int count = 0;
            if (linkedHazards == null)
            {
                return count;
            }

            for (int i = 0; i < linkedHazards.Length; i++)
            {
                NetworkBleachLeakHazard hazard = linkedHazards[i];
                if (hazard != null && !hazard.IsSuppressed)
                {
                    count++;
                }
            }

            return count;
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
