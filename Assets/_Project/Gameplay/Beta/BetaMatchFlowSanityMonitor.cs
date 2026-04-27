// File: Assets/_Project/Gameplay/Beta/BetaMatchFlowSanityMonitor.cs
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Server-side softlock monitor for the beta loop. It does not forcibly win/lose the round; it keeps broken task
    /// states from silently trapping the session and announces actionable problems to testers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaMatchFlowSanityMonitor : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float scanIntervalSeconds = 5f;
        [SerializeField, Min(5f)] private float warningCooldownSeconds = 24f;
        [SerializeField, Min(2f)] private float autoResetCancelledAfterSeconds = 3f;

        private float _nextScanTime;
        private float _nextWarningTime;

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                return;
            }

            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            ScanLoopState();
        }

        private void ScanLoopState()
        {
            NetworkRoundState round = FindObjectOfType<NetworkRoundState>();
            NetworkRepairTask[] tasks = FindObjectsOfType<NetworkRepairTask>();

            int total = 0;
            int completed = 0;
            int available = 0;
            int inProgress = 0;
            int lockedOrUnsafe = 0;
            int cancelledOrFailed = 0;

            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null)
                {
                    continue;
                }

                total++;
                switch (task.CurrentState)
                {
                    case RepairTaskState.Completed:
                        completed++;
                        break;
                    case RepairTaskState.InProgress:
                        inProgress++;
                        break;
                    case RepairTaskState.Idle:
                        if (task.IsTaskEnvironmentSafe())
                        {
                            available++;
                        }
                        else
                        {
                            lockedOrUnsafe++;
                        }
                        break;
                    case RepairTaskState.Cancelled:
                    case RepairTaskState.FailedAttempt:
                        cancelledOrFailed++;
                        TryAutoResetTask(task);
                        break;
                    default:
                        lockedOrUnsafe++;
                        break;
                }
            }

            if (total == 0)
            {
                Announce(round, "Beta monitor: no repair tasks found. Match can only end through role/flood logic.");
                return;
            }

            if (completed >= total)
            {
                return;
            }

            if (available == 0 && inProgress == 0)
            {
                Announce(round, $"Beta monitor: no usable tasks. Locked/unsafe {lockedOrUnsafe}, resettable {cancelledOrFailed}.");
            }
        }

        private void TryAutoResetTask(NetworkRepairTask task)
        {
            if (task == null || !task.IsServer || !task.IsSpawned)
            {
                return;
            }

            float started = task.TaskStartServerTime;
            if (started > 0f && NetworkManager.Singleton != null)
            {
                float elapsed = (float)NetworkManager.Singleton.ServerTime.Time - started;
                if (elapsed < autoResetCancelledAfterSeconds)
                {
                    return;
                }
            }

            task.ServerResetTask();
        }

        private void Announce(NetworkRoundState round, string message)
        {
            if (Time.unscaledTime < _nextWarningTime)
            {
                return;
            }

            _nextWarningTime = Time.unscaledTime + warningCooldownSeconds;
            if (round != null && round.IsServer && round.CurrentPhase == RoundPhase.FreeRoam)
            {
                round.ServerAnnounceEnvironmentalEvent("Beta Monitor", message, 0f);
            }
            else
            {
                Debug.LogWarning("[BetaMatchFlowSanityMonitor] " + message);
            }
        }
    }
}
