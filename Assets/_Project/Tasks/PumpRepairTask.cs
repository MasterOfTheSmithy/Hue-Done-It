// File: Assets/_Project/Tasks/PumpRepairTask.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    [DisallowMultipleComponent]
    public sealed class PumpRepairTask : NetworkRepairTask, IRepairTaskFloodSafetyProvider
    {
        [Header("Pump Puzzle")]
        [SerializeField, Min(1)] private int maxFailures = 3;
        [SerializeField, Range(0f, 1f)] private float confirmationWindowStartNormalized = 0.58f;
        [SerializeField, Range(0f, 1f)] private float confirmationWindowEndNormalized = 0.82f;

        [Header("Pump Presentation")]
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color idleColor = new(1f, 0.85f, 0.2f);
        [SerializeField] private Color activeColor = new(0.2f, 0.65f, 1f);
        [SerializeField] private Color completedColor = new(0.2f, 1f, 0.35f);
        [SerializeField] private Color cancelledColor = new(1f, 0.4f, 0.2f);
        [SerializeField] private Color failedColor = new(1f, 0.25f, 0.25f);
        [SerializeField] private Color lockedColor = new(0.55f, 0.55f, 0.6f);

        [Header("Flood Hook")]
        [SerializeField] private FloodZone linkedFloodZone;

        private readonly NetworkVariable<int> _failedAttempts =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MaterialPropertyBlock _propertyBlock;

        public int FailedAttempts => _failedAttempts.Value;
        public int AttemptsRemaining => Mathf.Max(0, maxFailures - _failedAttempts.Value);
        public bool IsLocked => CurrentState == RepairTaskState.Locked || _failedAttempts.Value >= maxFailures;
        public float ConfirmationWindowStartNormalized => confirmationWindowStartNormalized;
        public float ConfirmationWindowEndNormalized => confirmationWindowEndNormalized;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            TaskStateChanged += HandleTaskStateChanged;
            ApplyStateVisual(CurrentState);
        }

        public override void OnNetworkDespawn()
        {
            TaskStateChanged -= HandleTaskStateChanged;
            base.OnNetworkDespawn();
        }

        public bool IsTaskEnvironmentSafe(NetworkRepairTask task)
        {
            return linkedFloodZone == null || linkedFloodZone.IsSafe;
        }

        public bool ServerTryResolveConfirmation(float elapsedSeconds, ulong clientId)
        {
            if (!IsServer || CurrentState != RepairTaskState.InProgress || ActiveClientId != clientId)
            {
                return false;
            }

            float duration = Mathf.Max(0.01f, TaskDurationSeconds);
            float normalized = Mathf.Clamp01(elapsedSeconds / duration);
            bool success = normalized >= confirmationWindowStartNormalized && normalized <= confirmationWindowEndNormalized;
            if (success)
            {
                return TryCompleteTask();
            }

            RegisterFailure("Repair mistimed");
            return false;
        }

        public bool ServerTryHandleTimeout(ulong clientId)
        {
            if (!IsServer || CurrentState != RepairTaskState.InProgress || ActiveClientId != clientId)
            {
                return false;
            }

            RegisterFailure("Repair window missed");
            return false;
        }

        public bool ServerTryApplyCorrupt(ulong instigatorClientId)
        {
            if (!IsServer || IsCompleted || IsLocked)
            {
                return false;
            }

            RegisterFailure($"Corrupted by client {instigatorClientId}");
            return true;
        }

        protected override bool CanStartTask(in InteractionContext context)
        {
            return !IsLocked;
        }

        protected override string GetPromptForState(in InteractionContext context)
        {
            if (CurrentState == RepairTaskState.Completed)
            {
                return "Pump Repaired";
            }

            if (CurrentState == RepairTaskState.Locked)
            {
                return "Pump Locked: Flood release failed";
            }

            if (CurrentState == RepairTaskState.FailedAttempt)
            {
                return $"Pump Failed: {AttemptsRemaining} attempts left";
            }

            if (CurrentState == RepairTaskState.InProgress)
            {
                return ActiveClientId == context.InteractorClientId
                    ? "Stabilize Pump: press [E] in the blue window"
                    : "Pump Busy";
            }

            if (!IsTaskEnvironmentSafe(this))
            {
                return "Pump Unsafe: Zone flooded";
            }

            return $"Repair Pump ({AttemptsRemaining} attempts left)";
        }

        protected override void OnTaskCompleted()
        {
            base.OnTaskCompleted();
            ApplyStateVisual(RepairTaskState.Completed);
        }

        protected override void OnTaskCancelled(string reason)
        {
            base.OnTaskCancelled(reason);
            ApplyStateVisual(RepairTaskState.Cancelled);
        }

        protected override void OnTaskStarted(in InteractionContext context)
        {
            base.OnTaskStarted(context);
            ApplyStateVisual(RepairTaskState.InProgress);
        }

        protected override void OnServerResetTask()
        {
            _failedAttempts.Value = 0;
            ApplyStateVisual(RepairTaskState.Idle);
        }

        private void RegisterFailure(string reason)
        {
            if (!IsServer || IsCompleted || IsLocked)
            {
                return;
            }

            if (CurrentState == RepairTaskState.InProgress)
            {
                SetActiveClientId(ulong.MaxValue);
            }

            _failedAttempts.Value = Mathf.Clamp(_failedAttempts.Value + 1, 0, maxFailures);
            if (_failedAttempts.Value >= maxFailures)
            {
                SetState(RepairTaskState.Locked);
                Debug.Log($"PumpRepairTask locked after failure. Reason={reason}");
                ApplyStateVisual(RepairTaskState.Locked);
                return;
            }

            SetState(RepairTaskState.FailedAttempt);
            Debug.Log($"PumpRepairTask failure registered. AttemptsRemaining={AttemptsRemaining}. Reason={reason}");
            ApplyStateVisual(RepairTaskState.FailedAttempt);
        }

        private void HandleTaskStateChanged(RepairTaskState _, RepairTaskState current)
        {
            ApplyStateVisual(current);
        }

        private void ApplyStateVisual(RepairTaskState state)
        {
            if (statusRenderer == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            statusRenderer.GetPropertyBlock(_propertyBlock);
            Color color = GetColorForState(state);
            _propertyBlock.SetColor("_BaseColor", color);
            _propertyBlock.SetColor("_Color", color);
            statusRenderer.SetPropertyBlock(_propertyBlock);
        }

        private Color GetColorForState(RepairTaskState state)
        {
            return state switch
            {
                RepairTaskState.InProgress => activeColor,
                RepairTaskState.Completed => completedColor,
                RepairTaskState.Cancelled => cancelledColor,
                RepairTaskState.FailedAttempt => failedColor,
                RepairTaskState.Locked => lockedColor,
                _ => idleColor
            };
        }
    }
}
