// File: Assets/_Project/Tasks/TaskObjectiveBase.cs
using System;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Round;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public abstract class TaskObjectiveBase : NetworkBehaviour
    {
        [Header("Task Identity")]
        [SerializeField] private string taskId = "task-objective";
        [SerializeField] private string displayName = "Advanced Task";

        [Header("Runtime Rules")]
        [SerializeField, Min(0)] private int maxFailuresBeforeLock = 3;
        [SerializeField, Min(1f)] private float interactReleaseTimeoutSeconds = 20f;

        private readonly NetworkVariable<byte> _state =
            new((byte)RepairTaskState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _currentStepIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _failureCount =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _activeClientId =
            new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _lastInteractionServerTime =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString128Bytes> _objectiveText =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString128Bytes> _statusText =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetworkRoundState _roundState;

        public event Action<TaskObjectiveBase> TaskChanged;

        public string TaskId => taskId;
        public string DisplayName => displayName;
        public RepairTaskState CurrentState => (RepairTaskState)_state.Value;
        public int CurrentStepIndex => _currentStepIndex.Value;
        public int FailureCount => _failureCount.Value;
        public ulong ActiveClientId => _activeClientId.Value;
        public string CurrentObjectiveText => _objectiveText.Value.ToString();
        public string CurrentStatusText => _statusText.Value.ToString();
        public bool IsCompleted => CurrentState == RepairTaskState.Completed;
        public bool IsLocked => CurrentState == RepairTaskState.Locked;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _state.OnValueChanged += HandleStateChanged;
            _currentStepIndex.OnValueChanged += HandleStepChanged;
            _failureCount.OnValueChanged += HandleFailureChanged;
            _objectiveText.OnValueChanged += HandleObjectiveChanged;
            _statusText.OnValueChanged += HandleStatusChanged;
            _activeClientId.OnValueChanged += HandleOperatorChanged;

            if (IsServer)
            {
                ServerResetTask();
            }
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
            _currentStepIndex.OnValueChanged -= HandleStepChanged;
            _failureCount.OnValueChanged -= HandleFailureChanged;
            _objectiveText.OnValueChanged -= HandleObjectiveChanged;
            _statusText.OnValueChanged -= HandleStatusChanged;
            _activeClientId.OnValueChanged -= HandleOperatorChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            if (CurrentState != RepairTaskState.InProgress || _activeClientId.Value == ulong.MaxValue)
            {
                return;
            }

            if (interactReleaseTimeoutSeconds <= 0f)
            {
                return;
            }

            if ((GetServerTime() - _lastInteractionServerTime.Value) < interactReleaseTimeoutSeconds)
            {
                return;
            }

            _activeClientId.Value = ulong.MaxValue;
            _state.Value = (byte)RepairTaskState.Idle;
            SetStatus("Task released due to inactivity.");
        }

        public void ServerResetTask()
        {
            if (!IsServer)
            {
                return;
            }

            _state.Value = (byte)RepairTaskState.Idle;
            _currentStepIndex.Value = 0;
            _failureCount.Value = 0;
            _activeClientId.Value = ulong.MaxValue;
            _lastInteractionServerTime.Value = 0f;
            _objectiveText.Value = default;
            _statusText.Value = default;

            OnServerResetTask();
            NotifyTaskChanged();
        }

        public bool CanUseStep(string stepId, in InteractionContext context)
        {
            if (context.InteractorObject == null)
            {
                return false;
            }

            if (context.InteractorObject.TryGetComponent(out PlayerLifeState lifeState) && !lifeState.IsAlive)
            {
                return false;
            }

            if (CurrentState == RepairTaskState.Completed || CurrentState == RepairTaskState.Locked)
            {
                return false;
            }

            if (!IsActionPhase())
            {
                return false;
            }

            if (_activeClientId.Value != ulong.MaxValue && _activeClientId.Value != context.InteractorClientId)
            {
                return false;
            }

            return CanUseStepInternal(stepId, context);
        }

        public string GetPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            if (CurrentState == RepairTaskState.Completed)
            {
                return $"{displayName}: Completed";
            }

            if (CurrentState == RepairTaskState.Locked)
            {
                return $"{displayName}: Locked";
            }

            if (!IsActionPhase())
            {
                return $"{displayName}: Wait for live round";
            }

            return BuildPromptForStep(stepId, context, fallbackPrompt);
        }

        public bool ServerTryInteractStep(string stepId, in InteractionContext context)
        {
            if (!context.IsServer || !CanUseStep(stepId, context))
            {
                return false;
            }

            if (CurrentState == RepairTaskState.Idle || CurrentState == RepairTaskState.FailedAttempt)
            {
                _state.Value = (byte)RepairTaskState.InProgress;
            }

            if (_activeClientId.Value == ulong.MaxValue)
            {
                _activeClientId.Value = context.InteractorClientId;
            }

            _lastInteractionServerTime.Value = GetServerTime();
            bool result = HandleStepInteraction(stepId, context);
            NotifyTaskChanged();
            return result;
        }

        public void ServerReleaseActiveOperator(string status)
        {
            if (!IsServer || CurrentState != RepairTaskState.InProgress)
            {
                return;
            }

            _activeClientId.Value = ulong.MaxValue;
            _state.Value = (byte)RepairTaskState.Idle;
            SetStatus(status);
            NotifyTaskChanged();
        }

        protected void SetObjective(string text)
        {
            _objectiveText.Value = new FixedString128Bytes(string.IsNullOrWhiteSpace(text) ? string.Empty : text);
        }

        protected void SetStatus(string text)
        {
            _statusText.Value = new FixedString128Bytes(string.IsNullOrWhiteSpace(text) ? string.Empty : text);
        }

        protected void AdvanceToNextStep(string nextObjective, string status)
        {
            _currentStepIndex.Value = Mathf.Max(0, _currentStepIndex.Value + 1);
            _state.Value = (byte)RepairTaskState.InProgress;
            SetObjective(nextObjective);
            SetStatus(status);
        }

        protected void CompleteTask(string completionStatus)
        {
            _state.Value = (byte)RepairTaskState.Completed;
            _activeClientId.Value = ulong.MaxValue;
            SetObjective($"{displayName}: Completed");
            SetStatus(completionStatus);
            OnCompleted();
        }

        protected void RegisterFailure(string status)
        {
            _failureCount.Value = Mathf.Max(0, _failureCount.Value + 1);
            _activeClientId.Value = ulong.MaxValue;

            if (maxFailuresBeforeLock > 0 && _failureCount.Value >= maxFailuresBeforeLock)
            {
                _state.Value = (byte)RepairTaskState.Locked;
                SetObjective($"{displayName}: Locked");
                SetStatus(status);
                OnLockedOut();
                return;
            }

            _state.Value = (byte)RepairTaskState.FailedAttempt;
            SetStatus(status);
        }

        protected virtual bool CanUseStepInternal(string stepId, in InteractionContext context)
        {
            return !string.IsNullOrWhiteSpace(stepId);
        }

        protected virtual string BuildPromptForStep(string stepId, in InteractionContext context, string fallbackPrompt)
        {
            return $"{fallbackPrompt} ({displayName})";
        }

        protected virtual void OnServerResetTask()
        {
        }

        protected virtual void OnCompleted()
        {
        }

        protected virtual void OnLockedOut()
        {
        }

        protected abstract bool HandleStepInteraction(string stepId, in InteractionContext context);

        private bool IsActionPhase()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }

            return _roundState == null || _roundState.CurrentPhase == RoundPhase.FreeRoam;
        }

        private float GetServerTime()
        {
            return NetworkManager == null ? 0f : (float)NetworkManager.ServerTime.Time;
        }

        private void NotifyTaskChanged()
        {
            TaskChanged?.Invoke(this);
        }

        private void HandleStateChanged(byte _, byte __) => NotifyTaskChanged();
        private void HandleStepChanged(int _, int __) => NotifyTaskChanged();
        private void HandleFailureChanged(int _, int __) => NotifyTaskChanged();
        private void HandleObjectiveChanged(FixedString128Bytes _, FixedString128Bytes __) => NotifyTaskChanged();
        private void HandleStatusChanged(FixedString128Bytes _, FixedString128Bytes __) => NotifyTaskChanged();
        private void HandleOperatorChanged(ulong _, ulong __) => NotifyTaskChanged();
    }
}
