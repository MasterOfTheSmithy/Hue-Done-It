// File: Assets/_Project/Gameplay/Beta/BetaPlaytestDiagnostics.cs
using System.Text;
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// One-key diagnostic snapshot for friend beta testers. It prints enough state to make screenshots/logs actionable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaPlaytestDiagnostics : MonoBehaviour
    {
        [SerializeField] private KeyCode dumpKey = KeyCode.F8;
        [SerializeField] private bool copyToClipboard = true;

        private void Update()
        {
            if (BetaInputBridge.GetKeyDown(dumpKey))
            {
                DumpSnapshot();
            }
        }

        private void DumpSnapshot()
        {
            string snapshot = BuildSnapshot();
            Debug.Log(snapshot);
            if (copyToClipboard)
            {
                GUIUtility.systemCopyBuffer = snapshot;
            }
        }

        private static string BuildSnapshot()
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("=== HUE DONE IT BETA SNAPSHOT ===");
            sb.AppendLine("Time: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                sb.AppendLine("Network: missing NetworkManager");
            }
            else
            {
                sb.AppendLine($"Network: server={manager.IsServer}, host={manager.IsHost}, client={manager.IsClient}, connected={manager.ConnectedClientsIds.Count}");
                if (manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
                {
                    Transform t = manager.LocalClient.PlayerObject.transform;
                    sb.AppendLine($"LocalPlayer: {t.position} / {manager.LocalClient.PlayerObject.name}");
                }
                else
                {
                    sb.AppendLine("LocalPlayer: missing");
                }
            }

            NetworkRoundState round = FindObjectOfType<NetworkRoundState>();
            if (round != null)
            {
                sb.AppendLine($"Round: phase={round.CurrentPhase}, winner={round.Winner}, time={round.RoundTimeRemaining:0.0}, pressure={round.CurrentPressureStage}/{round.Pressure01:0.00}");
                sb.AppendLine("Objective: " + round.CurrentObjective);
                sb.AppendLine("Message: " + round.RoundMessage);
                round.GetMaintenanceProgress(out int maintenanceDone, out int maintenanceRequired, out int maintenanceTotal, out int maintenanceLocked);
                round.GetCriticalSystemProgress(out int criticalDone, out int criticalRequired, out int criticalTotal, out int criticalLocked);
                sb.AppendLine($"Tasks: maintenance {maintenanceDone}/{maintenanceRequired} total={maintenanceTotal} locked={maintenanceLocked}; critical {criticalDone}/{criticalRequired} total={criticalTotal} locked={criticalLocked}");
            }
            else
            {
                sb.AppendLine("Round: missing");
            }

            FloodSequenceController flood = FindObjectOfType<FloodSequenceController>();
            if (flood != null)
            {
                sb.AppendLine($"Flood: {flood.BuildRoundPressureHint()}");
            }

            FloodZone[] zones = FindObjectsOfType<FloodZone>();
            sb.AppendLine("FloodZones: " + zones.Length);
            for (int i = 0; i < Mathf.Min(zones.Length, 8); i++)
            {
                FloodZone zone = zones[i];
                if (zone != null)
                {
                    sb.AppendLine($"  - {zone.ZoneId}: {zone.CurrentState}, level={zone.WaterLevel01:0.00}, safe={zone.IsSafe}");
                }
            }

            NetworkRepairTask[] tasks = FindObjectsOfType<NetworkRepairTask>();
            sb.AppendLine("RepairTasks: " + tasks.Length);
            for (int i = 0; i < Mathf.Min(tasks.Length, 12); i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task != null)
                {
                    sb.AppendLine($"  - {task.DisplayName}: {task.CurrentState}, active={task.ActiveClientId}, safe={task.IsTaskEnvironmentSafe()}, progress={task.GetCurrentProgress01():0.00}");
                }
            }

            sb.AppendLine("=== END SNAPSHOT ===");
            return sb.ToString();
        }
    }
}
