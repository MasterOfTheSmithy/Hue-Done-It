// File: Assets/_Project/Gameplay/Beta/BetaTaskEndpointGuard.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Server-side task watchdog. It enforces that active tasks have a live participant inside the interaction radius.
    /// This covers CPU/disconnect/edge-case paths that may bypass the owner-side PlayerRepairTaskParticipant checks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaTaskEndpointGuard : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float tickIntervalSeconds = 0.35f;
        [SerializeField, Min(0f)] private float radiusPadding = 0.65f;
        [SerializeField, Min(0f)] private float cancelledResetDelaySeconds = 1.15f;

        private float _nextTickTime;

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (!Application.isPlaying || manager == null || !manager.IsServer)
            {
                return;
            }

            if (Time.unscaledTime < _nextTickTime)
            {
                return;
            }

            _nextTickTime = Time.unscaledTime + tickIntervalSeconds;
            TickTasks();
        }

        private void TickTasks()
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null || !task.IsSpawned)
                {
                    continue;
                }

                switch (task.CurrentState)
                {
                    case RepairTaskState.InProgress:
                        GuardInProgressTask(task);
                        break;
                    case RepairTaskState.Cancelled:
                    case RepairTaskState.FailedAttempt:
                        Invoke(nameof(DeferredResetCancelledTasks), cancelledResetDelaySeconds);
                        break;
                }
            }
        }

        private void GuardInProgressTask(NetworkRepairTask task)
        {
            ulong activeClientId = task.ActiveClientId;
            if (activeClientId == ulong.MaxValue)
            {
                task.TryCancelTask("Task reset: missing participant");
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null ||
                !manager.ConnectedClients.TryGetValue(activeClientId, out NetworkClient client) ||
                client.PlayerObject == null)
            {
                task.TryCancelTask("Task reset: participant disconnected");
                return;
            }

            PlayerLifeState lifeState = client.PlayerObject.GetComponent<PlayerLifeState>();
            if (lifeState != null && !lifeState.IsAlive)
            {
                task.TryCancelTask("Task reset: participant eliminated");
                return;
            }

            float allowedDistance = Mathf.Max(0.25f, task.MaxUseDistance + radiusPadding);
            float distance = Vector3.Distance(client.PlayerObject.transform.position, task.transform.position);
            if (distance > allowedDistance)
            {
                task.TryCancelTask("Task reset: left task radius");
                return;
            }

            if (!task.IsTaskEnvironmentSafe())
            {
                task.TryCancelTask("Task reset: unsafe flood state");
            }
        }

        private void DeferredResetCancelledTasks()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                return;
            }

            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null || !task.IsSpawned)
                {
                    continue;
                }

                if (task.CurrentState == RepairTaskState.Cancelled || task.CurrentState == RepairTaskState.FailedAttempt)
                {
                    task.ServerResetTask();
                }
            }
        }
    }
}
