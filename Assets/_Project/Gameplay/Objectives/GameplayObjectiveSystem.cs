// File: Assets/_Project/Gameplay/Objectives/GameplayObjectiveSystem.cs
using System.Text;
using HueDoneIt.Evidence;
using HueDoneIt.Gameplay.Beta;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Objectives
{
    public sealed class GameplayObjectiveSystem : MonoBehaviour
    {
        private const string TargetSceneName = BetaGameplaySceneCatalog.MainMap;

        [SerializeField] private NetworkRoundState roundState;

        [SerializeField, Range(0.1f, 1f)] private float maintenanceCompletionRatio = 0.6f;
        [SerializeField, Min(1)] private int minimumMaintenanceTasksRequired = 5;

        private TaskObjectiveBase[] _advancedTasks = System.Array.Empty<TaskObjectiveBase>();
        private NetworkRepairTask[] _repairTasks = System.Array.Empty<NetworkRepairTask>();
        private float _nextRebindTime;
        private string _cachedSummary = string.Empty;

        public event System.Action<string> SummaryChanged;

        public string CachedSummary => _cachedSummary;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallRuntime()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !BetaGameplaySceneCatalog.IsProductionGameplayScene(scene.name))
            {
                return;
            }

            if (FindFirstObjectByType<GameplayObjectiveSystem>() != null)
            {
                return;
            }

            GameObject go = new GameObject(nameof(GameplayObjectiveSystem));
            go.AddComponent<GameplayObjectiveSystem>();
        }

        private void OnEnable()
        {
            Rebind();
            RebuildSummary();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (Time.time >= _nextRebindTime)
            {
                _nextRebindTime = Time.time + 1f;
                Rebind();
                RebuildSummary();
            }
        }

        private void Rebind()
        {
            roundState ??= FindFirstObjectByType<NetworkRoundState>();

            Unsubscribe();

            _advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            _repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);

            for (int i = 0; i < _advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = _advancedTasks[i];
                if (task != null)
                {
                    task.TaskChanged += HandleAdvancedTaskChanged;
                }
            }

            for (int i = 0; i < _repairTasks.Length; i++)
            {
                NetworkRepairTask task = _repairTasks[i];
                if (task != null)
                {
                    task.TaskStateChanged += HandleRepairTaskChanged;
                }
            }
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < _advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = _advancedTasks[i];
                if (task != null)
                {
                    task.TaskChanged -= HandleAdvancedTaskChanged;
                }
            }

            for (int i = 0; i < _repairTasks.Length; i++)
            {
                NetworkRepairTask task = _repairTasks[i];
                if (task != null)
                {
                    task.TaskStateChanged -= HandleRepairTaskChanged;
                }
            }
        }

        private void HandleAdvancedTaskChanged(TaskObjectiveBase _)
        {
            RebuildSummary();
        }

        private void HandleRepairTaskChanged(RepairTaskState _, RepairTaskState __)
        {
            RebuildSummary();
        }

        private void RebuildSummary()
        {
            StringBuilder sb = new StringBuilder();

            if (roundState != null && !string.IsNullOrWhiteSpace(roundState.CurrentObjective))
            {
                sb.Append("• ");
                sb.AppendLine(roundState.CurrentObjective);
            }
            else
            {
                sb.AppendLine("• Restore the ship and survive the flood.");
            }

            if (roundState != null && roundState.CurrentPhase == RoundPhase.Reported && !string.IsNullOrWhiteSpace(roundState.MeetingSummary))
            {
                sb.Append("• Meeting vote: ");
                sb.Append(roundState.MeetingSummary);
                sb.Append(" // ");
                sb.Append(roundState.MeetingVotesCast);
                sb.Append("/");
                sb.Append(roundState.MeetingEligibleVotes);
                sb.AppendLine(" votes");
            }

            int completedAdvanced = 0;
            int requiredAdvanced = 0;
            int lockedAdvanced = 0;
            int totalAdvanced = 0;

            if (roundState != null)
            {
                roundState.GetCriticalSystemProgress(out completedAdvanced, out requiredAdvanced, out totalAdvanced, out lockedAdvanced);
            }

            for (int i = 0; i < _advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = _advancedTasks[i];
                if (task == null)
                {
                    continue;
                }

                if (task.IsCompleted)
                {
                    continue;
                }

                if (task.IsLocked)
                {
                    sb.Append("• ");
                    sb.Append(task.DisplayName);
                    sb.AppendLine(": LOCKED - route compromised.");
                    continue;
                }

                sb.Append("• ");
                sb.Append(task.DisplayName);
                sb.Append(": ");
                sb.AppendLine(string.IsNullOrWhiteSpace(task.CurrentObjectiveText) ? task.CurrentStatusText : task.CurrentObjectiveText);
            }

            if (totalAdvanced > 0)
            {
                sb.Append("• Critical systems: ");
                sb.Append(completedAdvanced);
                sb.Append("/");
                sb.Append(requiredAdvanced);
                sb.Append(" required");
                if (requiredAdvanced != totalAdvanced)
                {
                    sb.Append(" // ");
                    sb.Append(totalAdvanced);
                    sb.Append(" systems available");
                }
                if (lockedAdvanced > 0)
                {
                    sb.Append(" // ");
                    sb.Append(lockedAdvanced);
                    sb.Append(" locked");
                }

                sb.AppendLine();
            }

            int completedRepairs = 0;
            int requiredRepairs = 0;
            int totalRepairs = 0;
            int lockedRepairs = 0;

            if (roundState != null)
            {
                roundState.GetMaintenanceProgress(out completedRepairs, out requiredRepairs, out totalRepairs, out lockedRepairs);
            }
            else
            {
                for (int i = 0; i < _repairTasks.Length; i++)
                {
                    NetworkRepairTask task = _repairTasks[i];
                    if (task == null)
                    {
                        continue;
                    }

                    totalRepairs++;
                    if (task.IsCompleted)
                    {
                        completedRepairs++;
                    }
                    else if (task.CurrentState == RepairTaskState.Locked)
                    {
                        lockedRepairs++;
                    }
                }

                requiredRepairs = CalculateRequiredMaintenanceTasks(totalRepairs);
            }

            if (totalRepairs > 0)
            {
                sb.Append("• Ship maintenance quota: ");
                sb.Append(completedRepairs);
                sb.Append("/");
                sb.Append(requiredRepairs);
                sb.Append(" required");
                if (requiredRepairs != totalRepairs)
                {
                    sb.Append(" // ");
                    sb.Append(totalRepairs);
                    sb.Append(" stations available");
                }

                if (lockedRepairs > 0)
                {
                    sb.Append(" // ");
                    sb.Append(lockedRepairs);
                    sb.Append(" locked");
                }

                sb.AppendLine();
            }

            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            int activeHazards = 0;
            int suppressedHazards = 0;
            for (int i = 0; i < hazards.Length; i++)
            {
                NetworkBleachLeakHazard hazard = hazards[i];
                if (hazard == null)
                {
                    continue;
                }

                if (hazard.IsSuppressed)
                {
                    suppressedHazards++;
                }
                else
                {
                    activeHazards++;
                }
            }

            NetworkEmergencySealStation[] sealStations = FindObjectsByType<NetworkEmergencySealStation>(FindObjectsSortMode.None);
            int readySealStations = 0;
            for (int i = 0; i < sealStations.Length; i++)
            {
                if (sealStations[i] != null && sealStations[i].IsReady)
                {
                    readySealStations++;
                }
            }

            NetworkDecontaminationStation[] decontaminationStations = FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None);
            int readyDecontaminationStations = 0;
            for (int i = 0; i < decontaminationStations.Length; i++)
            {
                if (decontaminationStations[i] != null && decontaminationStations[i].IsReady)
                {
                    readyDecontaminationStations++;
                }
            }

            if (hazards.Length > 0 || sealStations.Length > 0 || decontaminationStations.Length > 0)
            {
                sb.Append("• Bleach leaks: ");
                sb.Append(activeHazards);
                sb.Append(" active // ");
                sb.Append(suppressedHazards);
                sb.Append(" suppressed // seal ");
                sb.Append(readySealStations);
                sb.Append("/");
                sb.Append(sealStations.Length);
                sb.Append(" // decon ");
                sb.Append(readyDecontaminationStations);
                sb.Append("/");
                sb.Append(decontaminationStations.Length);
                sb.AppendLine();
            }

            NetworkSafeRoomBeacon[] safeRoomBeacons = FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None);
            int usableSafeRooms = 0;
            for (int i = 0; i < safeRoomBeacons.Length; i++)
            {
                if (safeRoomBeacons[i] != null && (safeRoomBeacons[i].IsReady || safeRoomBeacons[i].IsActive))
                {
                    usableSafeRooms++;
                }
            }

            NetworkPaintScannerStation[] scannerStations = FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None);
            int readyScanners = 0;
            for (int i = 0; i < scannerStations.Length; i++)
            {
                if (scannerStations[i] != null && scannerStations[i].IsReady)
                {
                    readyScanners++;
                }
            }

            NetworkVitalsStation[] vitalsStations = FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None);
            int readyVitals = 0;
            for (int i = 0; i < vitalsStations.Length; i++)
            {
                if (vitalsStations[i] != null && vitalsStations[i].IsReady)
                {
                    readyVitals++;
                }
            }

            NetworkBleachVent[] vents = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            int openVents = 0;
            for (int i = 0; i < vents.Length; i++)
            {
                if (vents[i] != null && !vents[i].IsSealed)
                {
                    openVents++;
                }
            }

            NetworkEvidenceShard[] evidenceShards = FindObjectsByType<NetworkEvidenceShard>(FindObjectsSortMode.None);
            int actionableEvidence = 0;
            for (int i = 0; i < evidenceShards.Length; i++)
            {
                if (evidenceShards[i] != null && evidenceShards[i].IsActionable)
                {
                    actionableEvidence++;
                }
            }

            NetworkSecurityCameraStation[] securityCameraStations = FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None);
            int readySecurityCameras = 0;
            for (int i = 0; i < securityCameraStations.Length; i++)
            {
                if (securityCameraStations[i] != null && securityCameraStations[i].IsReady)
                {
                    readySecurityCameras++;
                }
            }

            NetworkAlarmTripwire[] alarmTripwires = FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None);
            int activeTripwires = 0;
            int readyTripwires = 0;
            for (int i = 0; i < alarmTripwires.Length; i++)
            {
                NetworkAlarmTripwire tripwire = alarmTripwires[i];
                if (tripwire == null)
                {
                    continue;
                }

                if (tripwire.IsArmed)
                {
                    activeTripwires++;
                }
                else if (tripwire.IsReady)
                {
                    readyTripwires++;
                }
            }

            NetworkInkWellStation[] inkWellStations = FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None);
            int readyInkWells = 0;
            for (int i = 0; i < inkWellStations.Length; i++)
            {
                if (inkWellStations[i] != null && inkWellStations[i].IsReady)
                {
                    readyInkWells++;
                }
            }

            NetworkFalseEvidenceStation[] falseEvidenceStations = FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None);
            int readyFalseEvidenceStations = 0;
            for (int i = 0; i < falseEvidenceStations.Length; i++)
            {
                if (falseEvidenceStations[i] != null && falseEvidenceStations[i].IsReady)
                {
                    readyFalseEvidenceStations++;
                }
            }

            NetworkCrewRallyStation[] rallyStations = FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None);
            int readyRallyStations = 0;
            for (int i = 0; i < rallyStations.Length; i++)
            {
                if (rallyStations[i] != null && rallyStations[i].IsReady)
                {
                    readyRallyStations++;
                }
            }

            NetworkBulkheadLockStation[] bulkheadStations = FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None);
            int readyBulkheadStations = 0;
            for (int i = 0; i < bulkheadStations.Length; i++)
            {
                if (bulkheadStations[i] != null && bulkheadStations[i].IsReady)
                {
                    readyBulkheadStations++;
                }
            }

            NetworkCalloutBeacon[] calloutBeacons = FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None);
            int readyCalloutBeacons = 0;
            for (int i = 0; i < calloutBeacons.Length; i++)
            {
                if (calloutBeacons[i] != null && calloutBeacons[i].IsReady)
                {
                    readyCalloutBeacons++;
                }
            }

            NetworkVotingPodium[] votingPodiums = FindObjectsByType<NetworkVotingPodium>(FindObjectsSortMode.None);
            NetworkEmergencyMeetingConsole meetingConsole = FindFirstObjectByType<NetworkEmergencyMeetingConsole>();
            if (safeRoomBeacons.Length > 0 || scannerStations.Length > 0 || vitalsStations.Length > 0 || securityCameraStations.Length > 0 || alarmTripwires.Length > 0 || inkWellStations.Length > 0 || falseEvidenceStations.Length > 0 || vents.Length > 0 || evidenceShards.Length > 0 || meetingConsole != null || rallyStations.Length > 0 || bulkheadStations.Length > 0 || calloutBeacons.Length > 0 || votingPodiums.Length > 0)
            {
                sb.Append("• Crew tools: safe rooms ");
                sb.Append(usableSafeRooms);
                sb.Append("/");
                sb.Append(safeRoomBeacons.Length);
                sb.Append(" // scanners ");
                sb.Append(readyScanners);
                sb.Append("/");
                sb.Append(scannerStations.Length);
                sb.Append(" // vitals ");
                sb.Append(readyVitals);
                sb.Append("/");
                sb.Append(vitalsStations.Length);
                sb.Append(" // cameras ");
                sb.Append(readySecurityCameras);
                sb.Append("/");
                sb.Append(securityCameraStations.Length);
                sb.Append(" // rally ");
                sb.Append(readyRallyStations);
                sb.Append("/");
                sb.Append(rallyStations.Length);
                sb.Append(" // bulkheads ");
                sb.Append(readyBulkheadStations);
                sb.Append("/");
                sb.Append(bulkheadStations.Length);
                sb.Append(" // callouts ");
                sb.Append(readyCalloutBeacons);
                sb.Append("/");
                sb.Append(calloutBeacons.Length);
                sb.Append(" // tripwires ");
                sb.Append(activeTripwires);
                sb.Append(" armed + ");
                sb.Append(readyTripwires);
                sb.Append(" ready");
                sb.Append(" // ink wells ");
                sb.Append(readyInkWells);
                sb.Append("/");
                sb.Append(inkWellStations.Length);
                sb.Append(" // smear kits ");
                sb.Append(readyFalseEvidenceStations);
                sb.Append("/");
                sb.Append(falseEvidenceStations.Length);
                sb.Append(" // vents open ");
                sb.Append(openVents);
                sb.Append("/");
                sb.Append(vents.Length);
                sb.Append(" // evidence ");
                sb.Append(actionableEvidence);
                sb.Append(" // vote pods ");
                sb.Append(votingPodiums.Length);
                if (meetingConsole != null)
                {
                    sb.Append(" // meeting ");
                    sb.Append(meetingConsole.IsReady ? "ready" : "cooldown");
                }

                sb.AppendLine();
            }

            if (roundState != null && roundState.EnvironmentEventCount > 0)
            {
                sb.Append("• Environmental events resolved/active: ");
                sb.Append(roundState.EnvironmentEventCount);
                sb.AppendLine();
            }


            string next = sb.ToString().TrimEnd();

            if (next == _cachedSummary)
            {
                return;
            }

            _cachedSummary = next;
            SummaryChanged?.Invoke(_cachedSummary);
        }
        private int CalculateRequiredMaintenanceTasks(int totalMaintenanceTasks)
        {
            if (totalMaintenanceTasks <= 0)
            {
                return 0;
            }

            int ratioCount = Mathf.CeilToInt(totalMaintenanceTasks * maintenanceCompletionRatio);
            int required = Mathf.Max(minimumMaintenanceTasksRequired, ratioCount);
            return Mathf.Clamp(required, 1, totalMaintenanceTasks);
        }

    }
}
