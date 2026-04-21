// File: Assets/_Project/Tasks/PlayerRepairTaskParticipant.cs
using System;
using HueDoneIt.Gameplay.Interaction;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerInteractionDetector))]
    public sealed class PlayerRepairTaskParticipant : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float maxRepairRange = 2.5f;
        [SerializeField] private Key puzzleConfirmKey = Key.E;

        private readonly NetworkVariable<ulong> _activeTaskNetworkObjectId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _taskStartedServerTime =
            new(0f, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private NetworkRepairTask _trackedTask;
        private bool _hasSentTerminalRequest;

        public event Action<NetworkRepairTask, float, bool> TaskProgressUpdated;

        public bool HasActiveTask => _trackedTask != null;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _activeTaskNetworkObjectId.OnValueChanged += HandleActiveTaskChanged;

            if (IsOwner && IsClient)
            {
                ResolveTrackedTask(_activeTaskNetworkObjectId.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            _activeTaskNetworkObjectId.OnValueChanged -= HandleActiveTaskChanged;
            UnbindTrackedTask();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsOwner || !IsClient || !IsSpawned)
            {
                return;
            }

            if (_trackedTask == null)
            {
                return;
            }

            float duration = Mathf.Max(0.01f, _trackedTask.TaskDurationSeconds);
            float elapsed = Mathf.Max(0f, (float)NetworkManager.ServerTime.Time - _taskStartedServerTime.Value);
            float progress = Mathf.Clamp01(elapsed / duration);
            bool completed = _trackedTask.CurrentState == RepairTaskState.Completed;
            TaskProgressUpdated?.Invoke(_trackedTask, progress, completed);

            if (_trackedTask.CurrentState == RepairTaskState.Cancelled ||
                _trackedTask.CurrentState == RepairTaskState.Completed ||
                _trackedTask.CurrentState == RepairTaskState.FailedAttempt ||
                _trackedTask.CurrentState == RepairTaskState.Locked)
            {
                return;
            }

            if (_trackedTask.CurrentState != RepairTaskState.InProgress)
            {
                return;
            }

            if (_trackedTask.ActiveClientId != OwnerClientId)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && _trackedTask is PumpRepairTask && keyboard[puzzleConfirmKey].wasPressedThisFrame)
            {
                RequestPumpConfirmServerRpc(_trackedTask.NetworkObjectId);
            }

            float distance = Vector3.Distance(transform.position, _trackedTask.transform.position);
            if (distance > maxRepairRange || !_trackedTask.IsTaskEnvironmentSafe())
            {
                if (!_hasSentTerminalRequest)
                {
                    _hasSentTerminalRequest = true;
                    RequestCancelRepairServerRpc(_trackedTask.NetworkObjectId, "Repair interrupted");
                }

                return;
            }

            if (progress >= 1f && !_hasSentTerminalRequest)
            {
                _hasSentTerminalRequest = true;

                if (_trackedTask is PumpRepairTask)
                {
                    RequestPumpTimeoutServerRpc(_trackedTask.NetworkObjectId);
                }
                else
                {
                    RequestCompleteRepairServerRpc(_trackedTask.NetworkObjectId);
                }
            }
        }

        [ServerRpc]
        private void RequestCompleteRepairServerRpc(ulong taskNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            if (!TryGetTaskForSender(taskNetworkObjectId, rpcParams.Receive.SenderClientId, out NetworkRepairTask task, out _))
            {
                return;
            }

            if (task.CurrentState != RepairTaskState.InProgress)
            {
                return;
            }

            if ((float)(NetworkManager.ServerTime.Time - _taskStartedServerTime.Value) < task.TaskDurationSeconds)
            {
                return;
            }

            task.TryCompleteTask();
            ClearActiveTaskServerState();
        }

        [ServerRpc]
        private void RequestPumpConfirmServerRpc(ulong taskNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            if (!TryGetTaskForSender(taskNetworkObjectId, rpcParams.Receive.SenderClientId, out NetworkRepairTask task, out _))
            {
                return;
            }

            if (task is not PumpRepairTask pumpTask)
            {
                return;
            }

            float elapsed = Mathf.Max(0f, (float)NetworkManager.ServerTime.Time - _taskStartedServerTime.Value);
            pumpTask.ServerTryResolveConfirmation(elapsed, rpcParams.Receive.SenderClientId);
            if (pumpTask.CurrentState != RepairTaskState.InProgress)
            {
                ClearActiveTaskServerState();
            }
        }

        [ServerRpc]
        private void RequestPumpTimeoutServerRpc(ulong taskNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            if (!TryGetTaskForSender(taskNetworkObjectId, rpcParams.Receive.SenderClientId, out NetworkRepairTask task, out _))
            {
                return;
            }

            if (task is not PumpRepairTask pumpTask)
            {
                return;
            }

            pumpTask.ServerTryHandleTimeout(rpcParams.Receive.SenderClientId);
            if (pumpTask.CurrentState != RepairTaskState.InProgress)
            {
                ClearActiveTaskServerState();
            }
        }

        [ServerRpc]
        private void RequestCancelRepairServerRpc(ulong taskNetworkObjectId, string reason, ServerRpcParams rpcParams = default)
        {
            if (!TryGetTaskForSender(taskNetworkObjectId, rpcParams.Receive.SenderClientId, out NetworkRepairTask task, out _))
            {
                return;
            }

            if (task.CurrentState != RepairTaskState.InProgress)
            {
                return;
            }

            if (task.ActiveClientId != rpcParams.Receive.SenderClientId)
            {
                return;
            }

            task.TryCancelTask(reason);
            ClearActiveTaskServerState();
        }

        public void ServerRegisterTaskStart(NetworkRepairTask task, ulong clientId)
        {
            if (!IsServer || task == null)
            {
                return;
            }

            if (!task.IsActiveParticipant(clientId))
            {
                return;
            }

            _activeTaskNetworkObjectId.Value = task.NetworkObjectId;
            _taskStartedServerTime.Value = (float)NetworkManager.ServerTime.Time;
        }

        [ServerRpc]
        public void NotifyTaskStartedServerRpc(ulong taskNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(senderId, out NetworkClient client) || client.PlayerObject == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(taskNetworkObjectId, out NetworkObject taskObject))
            {
                return;
            }

            if (!taskObject.TryGetComponent(out NetworkRepairTask task))
            {
                return;
            }

            if (!task.IsActiveParticipant(senderId))
            {
                return;
            }

            _activeTaskNetworkObjectId.Value = taskNetworkObjectId;
            _taskStartedServerTime.Value = (float)NetworkManager.ServerTime.Time;
        }

        private void ClearActiveTaskServerState()
        {
            _activeTaskNetworkObjectId.Value = ulong.MaxValue;
            _taskStartedServerTime.Value = 0f;
        }

        private bool TryGetTaskForSender(ulong taskNetworkObjectId, ulong senderClientId, out NetworkRepairTask task, out NetworkObject playerObject)
        {
            task = null;
            playerObject = null;

            if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) || client.PlayerObject == null)
            {
                return false;
            }

            playerObject = client.PlayerObject;

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(taskNetworkObjectId, out NetworkObject taskObject))
            {
                return false;
            }

            if (!taskObject.TryGetComponent(out task))
            {
                return false;
            }

            if (task.ActiveClientId != senderClientId)
            {
                return false;
            }

            float distance = Vector3.Distance(playerObject.transform.position, task.transform.position);
            if (distance > task.MaxUseDistance)
            {
                task.TryCancelTask("Moved out of range");
                return false;
            }

            if (!task.IsTaskEnvironmentSafe())
            {
                task.TryCancelTask("Task environment became unsafe");
                return false;
            }

            return true;
        }

        private void HandleActiveTaskChanged(ulong _, ulong current)
        {
            if (!IsOwner || !IsClient)
            {
                return;
            }

            ResolveTrackedTask(current);
        }

        private void ResolveTrackedTask(ulong networkObjectId)
        {
            UnbindTrackedTask();
            _hasSentTerminalRequest = false;

            if (networkObjectId == ulong.MaxValue)
            {
                TaskProgressUpdated?.Invoke(null, 0f, false);
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject taskObject) ||
                !taskObject.TryGetComponent(out _trackedTask))
            {
                _trackedTask = null;
                TaskProgressUpdated?.Invoke(null, 0f, false);
                return;
            }

            _trackedTask.TaskStateChanged += HandleTrackedTaskStateChanged;
        }

        private void UnbindTrackedTask()
        {
            if (_trackedTask != null)
            {
                _trackedTask.TaskStateChanged -= HandleTrackedTaskStateChanged;
                _trackedTask = null;
            }
        }

        private void HandleTrackedTaskStateChanged(RepairTaskState _, RepairTaskState current)
        {
            if (current is RepairTaskState.Completed or RepairTaskState.Cancelled or RepairTaskState.FailedAttempt or RepairTaskState.Locked)
            {
                _hasSentTerminalRequest = false;
                TaskProgressUpdated?.Invoke(_trackedTask, current == RepairTaskState.Completed ? 1f : 0f, current == RepairTaskState.Completed);
            }
        }
    }
}
