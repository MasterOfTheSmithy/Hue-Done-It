// File: Assets/_Project/Tasks/NetworkRepairTask.cs
using System;
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public abstract class NetworkRepairTask : NetworkInteractable
    {
        [Header("Task Identity")]
        [SerializeField] private string taskId = "task-id";
        [SerializeField] private string displayName = "Repair Task";
        [SerializeField] private string interactPrompt = "Repair";
        [SerializeField, Min(0f)] private float taskDurationSeconds = 2f;

        [Header("Task Safety")]
        [SerializeField] private FloodZone floodZone;
        [SerializeField] private bool requireSafeFloodZone;

        private readonly NetworkVariable<byte> _state =
            new((byte)RepairTaskState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _activeClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private IRepairTaskFloodSafetyProvider _floodSafetyProvider;

        public event Action<RepairTaskState, RepairTaskState> TaskStateChanged;

        public string TaskId => taskId;
        public string DisplayName => displayName;
        public string InteractPrompt => interactPrompt;
        public float TaskDurationSeconds => taskDurationSeconds;
        public RepairTaskState CurrentState => (RepairTaskState)_state.Value;
        public ulong ActiveClientId => _activeClientId.Value;
        public bool IsCompleted => CurrentState == RepairTaskState.Completed;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _state.OnValueChanged += HandleTaskStateChanged;

            _floodSafetyProvider ??= GetComponent<IRepairTaskFloodSafetyProvider>();
            if (IsServer)
            {
                _state.Value = (byte)RepairTaskState.Idle;
                _activeClientId.Value = ulong.MaxValue;
            }
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleTaskStateChanged;
            base.OnNetworkDespawn();
        }

        public sealed override bool CanInteract(in InteractionContext context)
        {
            return IsInteractionAllowed(context);
        }

        public sealed override string GetPromptText(in InteractionContext context)
        {
            return GetPromptForState(context);
        }

        public sealed override bool TryInteract(in InteractionContext context)
        {
            if (!context.IsServer)
            {
                return false;
            }

            if (!TryBeginTask(context))
            {
                return false;
            }

            return true;
        }

        public bool CanStart(in InteractionContext context)
        {
            if (!context.IsServer)
            {
                return false;
            }

            if (CurrentState == RepairTaskState.Completed || CurrentState == RepairTaskState.InProgress)
            {
                return false;
            }

            if (!IsTaskEnvironmentSafe())
            {
                return false;
            }

            return CanStartTask(context);
        }

        public bool TryBeginTask(in InteractionContext context)
        {
            if (!CanStart(context))
            {
                return false;
            }

            SetState(RepairTaskState.InProgress);
            _activeClientId.Value = context.InteractorClientId;
            OnTaskStarted(context);
            return true;
        }

        public bool TryCancelTask(string reason = "Cancelled")
        {
            if (!IsServer || CurrentState != RepairTaskState.InProgress)
            {
                return false;
            }

            SetState(RepairTaskState.Cancelled);
            _activeClientId.Value = ulong.MaxValue;
            OnTaskCancelled(reason);
            return true;
        }

        public bool TryCompleteTask()
        {
            if (!IsServer || CurrentState != RepairTaskState.InProgress)
            {
                return false;
            }

            SetState(RepairTaskState.Completed);
            _activeClientId.Value = ulong.MaxValue;
            OnTaskCompleted();
            return true;
        }

        public bool IsTaskEnvironmentSafe()
        {
            if (!requireSafeFloodZone)
            {
                return true;
            }

            if (_floodSafetyProvider != null)
            {
                return _floodSafetyProvider.IsTaskEnvironmentSafe(this);
            }

            return floodZone == null || floodZone.IsSafe;
        }

        public bool IsActiveParticipant(ulong clientId)
        {
            return CurrentState == RepairTaskState.InProgress && ActiveClientId == clientId;
        }

        public virtual float GetCurrentProgress01()
        {
            return 0f;
        }

        protected virtual bool IsInteractionAllowed(in InteractionContext context)
        {
            if (CurrentState == RepairTaskState.Completed)
            {
                return false;
            }

            if (CurrentState == RepairTaskState.InProgress)
            {
                return ActiveClientId == context.InteractorClientId;
            }

            return true;
        }

        protected virtual string GetPromptForState(in InteractionContext context)
        {
            if (CurrentState == RepairTaskState.Completed)
            {
                return $"{displayName} [COMPLETED]";
            }

            if (CurrentState == RepairTaskState.InProgress)
            {
                return ActiveClientId == context.InteractorClientId
                    ? $"{displayName}: Repairing..."
                    : $"{displayName}: In use";
            }

            if (!IsTaskEnvironmentSafe())
            {
                return $"{displayName}: Unsafe zone";
            }

            return $"{interactPrompt} ({displayName})";
        }

        protected virtual bool CanStartTask(in InteractionContext context)
        {
            return true;
        }

        protected virtual void OnTaskStarted(in InteractionContext context)
        {
            Debug.Log($"Task '{taskId}' started by client {context.InteractorClientId}.");
        }

        protected virtual void OnTaskCancelled(string reason)
        {
            Debug.Log($"Task '{taskId}' cancelled: {reason}.");
        }

        protected virtual void OnTaskCompleted()
        {
            Debug.Log($"Task '{taskId}' completed.");
        }

        protected void SetState(RepairTaskState nextState)
        {
            if (!IsServer)
            {
                return;
            }

            _state.Value = (byte)nextState;
        }

        private void HandleTaskStateChanged(byte previousValue, byte currentValue)
        {
            TaskStateChanged?.Invoke((RepairTaskState)previousValue, (RepairTaskState)currentValue);
        }
    }
}
