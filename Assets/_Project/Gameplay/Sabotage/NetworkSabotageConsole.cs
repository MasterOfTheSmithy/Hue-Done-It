// File: Assets/_Project/Gameplay/Sabotage/NetworkSabotageConsole.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Sabotage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkSabotageConsole : NetworkInteractable
    {
        [Header("Identity")]
        [SerializeField] private string consoleId = "sabotage-console";
        [SerializeField] private string displayName = "Sabotage Console";
        [SerializeField] private string interactPrompt = "Trigger Sabotage";

        [Header("Pressure Effect")]
        [SerializeField, Min(1f)] private float cooldownSeconds = 38f;
        [SerializeField, Min(0f)] private float roundTimePenaltySeconds = 18f;
        [SerializeField] private FloodZone[] affectedZones = new FloodZone[0];
        [SerializeField] private bool escalateWetToFlooding = true;
        [SerializeField] private bool cancelActiveRepairTasks = true;

        [Header("Visual")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color readyColor = new(0.95f, 0.95f, 1f, 1f);
        [SerializeField] private Color cooldownColor = new(0.35f, 0.35f, 0.42f, 1f);
        [SerializeField] private Color activatedColor = new(0.9f, 0.2f, 0.95f, 1f);

        private readonly NetworkVariable<float> _cooldownEndServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _block;

        public string ConsoleId => consoleId;
        public string DisplayName => displayName;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEndServerTime.Value - GetServerTime());
        public bool IsReady => CooldownRemaining <= 0.001f;

        protected override void Awake()
        {
            base.Awake();
            if (statusRenderer == null)
            {
                statusRenderer = GetComponentInChildren<Renderer>();
            }

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

        public void ConfigureRuntime(
            string id,
            string label,
            string prompt,
            float cooldown,
            float timePenalty,
            FloodZone[] zones,
            Renderer renderer = null)
        {
            consoleId = string.IsNullOrWhiteSpace(id) ? consoleId : id;
            displayName = string.IsNullOrWhiteSpace(label) ? displayName : label;
            interactPrompt = string.IsNullOrWhiteSpace(prompt) ? interactPrompt : prompt;
            cooldownSeconds = Mathf.Max(1f, cooldown);
            roundTimePenaltySeconds = Mathf.Max(0f, timePenalty);
            affectedZones = zones ?? new FloodZone[0];
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
            if (context.InteractorObject == null ||
                !context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) ||
                roleController.CurrentRole != PlayerRole.Bleach)
            {
                return displayName + ": suspicious bleach-only console";
            }

            float cooldown = CooldownRemaining;
            if (cooldown > 0f)
            {
                return $"{displayName}: Recharging {Mathf.CeilToInt(cooldown)}s";
            }

            return $"{interactPrompt} ({displayName})";
        }

        public override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer || context.InteractorObject == null)
            {
                return false;
            }

            if (!context.InteractorObject.TryGetComponent(out PlayerKillInputController roleController) ||
                roleController.CurrentRole != PlayerRole.Bleach)
            {
                return false;
            }

            if (!IsReady)
            {
                return false;
            }

            NetworkRoundState roundState = FindFirstObjectByType<NetworkRoundState>();
            if (roundState != null && !roundState.ServerApplySabotagePressure(context.InteractorClientId, displayName, roundTimePenaltySeconds))
            {
                return false;
            }

            FloodSequenceController floodController = FindFirstObjectByType<FloodSequenceController>();
            floodController?.ServerTriggerReportAftershock();
            EscalateAffectedZones();
            ReactivateNearestSuppressedLeak();
            DisableNearestDecontaminationStation();
            JamNearestFloodgateStation();
            JamNearestPaintScanner();
            JamNearestSafeRoomBeacon();
            JamNearestVitalsStation();
            UnsealNearestBleachVent();
            JamNearestSecurityCamera();
            JamNearestAlarmTripwire();
            ContaminateNearestInkWell();
            CooldownNearestFalseEvidenceStation();
            JamNearestCrewRallyStation();
            JamNearestBulkheadLockStation();
            JamNearestCalloutBeacon();

            if (cancelActiveRepairTasks)
            {
                CancelOneActiveRepairTask();
            }

            _cooldownEndServerTime.Value = GetServerTime() + cooldownSeconds;
            ApplyColor(activatedColor);
            return true;
        }

        public bool ServerForceCooldown(float seconds, string reason = "Sabotage console quarantined")
        {
            if (!IsServer)
            {
                return false;
            }

            _cooldownEndServerTime.Value = Mathf.Max(_cooldownEndServerTime.Value, GetServerTime() + Mathf.Max(1f, seconds));
            ApplyColor(cooldownColor);
            return true;
        }

        private void EscalateAffectedZones()
        {
            if (affectedZones == null)
            {
                return;
            }

            for (int i = 0; i < affectedZones.Length; i++)
            {
                FloodZone zone = affectedZones[i];
                if (zone == null || zone.CurrentState == FloodZoneState.SealedSafe)
                {
                    continue;
                }

                if (zone.CurrentState == FloodZoneState.Dry)
                {
                    zone.TrySetState(FloodZoneState.Wet);
                    continue;
                }

                if (escalateWetToFlooding && zone.CurrentState == FloodZoneState.Wet)
                {
                    zone.TrySetState(FloodZoneState.Flooding);
                }
            }
        }

        private void ReactivateNearestSuppressedLeak()
        {
            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            NetworkBleachLeakHazard bestHazard = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hazards.Length; i++)
            {
                NetworkBleachLeakHazard hazard = hazards[i];
                if (hazard == null || !hazard.IsSuppressed)
                {
                    continue;
                }

                float distanceSqr = (hazard.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestHazard = hazard;
                }
            }

            bestHazard?.ServerReactivate(displayName + " restarted " + bestHazard.DisplayName);
        }

        private void DisableNearestDecontaminationStation()
        {
            NetworkDecontaminationStation[] stations = FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None);
            NetworkDecontaminationStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkDecontaminationStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerForceCooldown(18f, displayName + " contaminated " + bestStation.DisplayName);
        }

        private void JamNearestFloodgateStation()
        {
            NetworkFloodgateStation[] stations = FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None);
            NetworkFloodgateStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkFloodgateStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerJam(20f, displayName + " jammed " + bestStation.DisplayName);
        }

        private void JamNearestPaintScanner()
        {
            NetworkPaintScannerStation[] scanners = FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None);
            NetworkPaintScannerStation bestScanner = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < scanners.Length; i++)
            {
                NetworkPaintScannerStation scanner = scanners[i];
                if (scanner == null)
                {
                    continue;
                }

                float distanceSqr = (scanner.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestScanner = scanner;
                }
            }

            bestScanner?.ServerJam(16f, displayName + " jammed " + bestScanner.DisplayName);
        }

        private void JamNearestSafeRoomBeacon()
        {
            NetworkSafeRoomBeacon[] beacons = FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None);
            NetworkSafeRoomBeacon bestBeacon = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < beacons.Length; i++)
            {
                NetworkSafeRoomBeacon beacon = beacons[i];
                if (beacon == null)
                {
                    continue;
                }

                float distanceSqr = (beacon.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestBeacon = beacon;
                }
            }

            bestBeacon?.ServerJam(20f, displayName + " jammed " + bestBeacon.DisplayName);
        }

        private void JamNearestVitalsStation()
        {
            NetworkVitalsStation[] stations = FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None);
            NetworkVitalsStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkVitalsStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerJam(18f, displayName + " jammed " + bestStation.DisplayName);
        }

        private void UnsealNearestBleachVent()
        {
            NetworkBleachVent[] vents = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            NetworkBleachVent bestVent = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < vents.Length; i++)
            {
                NetworkBleachVent vent = vents[i];
                if (vent == null || !vent.IsSealed)
                {
                    continue;
                }

                float distanceSqr = (vent.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestVent = vent;
                }
            }

            bestVent?.ServerUnseal(displayName + " dissolved a vent seal.");
        }

        private void JamNearestSecurityCamera()
        {
            NetworkSecurityCameraStation[] stations = FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None);
            NetworkSecurityCameraStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkSecurityCameraStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerForceCooldown(18f, displayName + " looped " + bestStation.DisplayName);
        }

        private void JamNearestAlarmTripwire()
        {
            NetworkAlarmTripwire[] tripwires = FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None);
            NetworkAlarmTripwire bestTripwire = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < tripwires.Length; i++)
            {
                NetworkAlarmTripwire tripwire = tripwires[i];
                if (tripwire == null)
                {
                    continue;
                }

                float distanceSqr = (tripwire.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTripwire = tripwire;
                }
            }

            bestTripwire?.ServerForceCooldown(20f, displayName + " shorted " + bestTripwire.DisplayName);
        }

        private void ContaminateNearestInkWell()
        {
            NetworkInkWellStation[] stations = FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None);
            NetworkInkWellStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkInkWellStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerForceCooldown(22f, displayName + " contaminated " + bestStation.DisplayName);
        }

        private void CooldownNearestFalseEvidenceStation()
        {
            NetworkFalseEvidenceStation[] stations = FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None);
            NetworkFalseEvidenceStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkFalseEvidenceStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerForceCooldown(16f, displayName + " flushed nearby smear kit");
        }

        private void JamNearestCrewRallyStation()
        {
            NetworkCrewRallyStation[] stations = FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None);
            NetworkCrewRallyStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkCrewRallyStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerForceCooldown(22f, displayName + " disrupted " + bestStation.DisplayName);
        }

        private void JamNearestBulkheadLockStation()
        {
            NetworkBulkheadLockStation[] stations = FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None);
            NetworkBulkheadLockStation bestStation = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < stations.Length; i++)
            {
                NetworkBulkheadLockStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestStation = station;
                }
            }

            bestStation?.ServerForceCooldown(24f, displayName + " jammed " + bestStation.DisplayName);
        }

        private void JamNearestCalloutBeacon()
        {
            NetworkCalloutBeacon[] beacons = FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None);
            NetworkCalloutBeacon bestBeacon = null;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < beacons.Length; i++)
            {
                NetworkCalloutBeacon beacon = beacons[i];
                if (beacon == null)
                {
                    continue;
                }

                float distanceSqr = (beacon.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestBeacon = beacon;
                }
            }

            bestBeacon?.ServerForceCooldown(18f, displayName + " jammed " + bestBeacon.DisplayName);
        }

        private void CancelOneActiveRepairTask()
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            NetworkRepairTask nearestActive = null;
            float nearestDistanceSqr = float.MaxValue;

            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null || task.CurrentState != RepairTaskState.InProgress)
                {
                    continue;
                }

                float distanceSqr = (task.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr < nearestDistanceSqr)
                {
                    nearestDistanceSqr = distanceSqr;
                    nearestActive = task;
                }
            }

            nearestActive?.TryCancelTask("Bleach sabotage surge");
        }

        private void HandleCooldownChanged(float previous, float current)
        {
            ApplyColor(IsReady ? readyColor : cooldownColor);
        }

        private void ApplyColor(Color color)
        {
            if (statusRenderer == null)
            {
                statusRenderer = GetComponentInChildren<Renderer>();
            }

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
