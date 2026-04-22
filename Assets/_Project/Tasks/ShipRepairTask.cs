// File: Assets/_Project/Tasks/ShipRepairTask.cs
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;

namespace HueDoneIt.Tasks
{
    // This is a generic ship repair task used to populate the map with a full maintenance workload.
    // It extends the existing server-authoritative NetworkRepairTask flow and relies on PlayerRepairTaskParticipant
    // for progress ticking, commitment lock, and completion/cancel requests.
    [DisallowMultipleComponent]
    public sealed class ShipRepairTask : NetworkRepairTask
    {
        public enum DifficultyTier : byte
        {
            Easy = 0,
            Medium = 1,
            Hard = 2
        }

        [Header("Ship Task Tuning")]
        [SerializeField] private DifficultyTier difficulty = DifficultyTier.Easy;
        [SerializeField, Min(0)] private int maxFailuresBeforeLock = 0;
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color idleColor = new(0.8f, 0.8f, 0.82f, 1f);
        [SerializeField] private Color activeColor = new(0.2f, 0.7f, 1f, 1f);
        [SerializeField] private Color completeColor = new(0.2f, 1f, 0.45f, 1f);
        [SerializeField] private Color failedColor = new(1f, 0.35f, 0.3f, 1f);
        [SerializeField] private Color lockedColor = new(0.45f, 0.45f, 0.45f, 1f);

        private int _failureCount;
        private MaterialPropertyBlock _block;

        public DifficultyTier Difficulty => difficulty;

        protected override bool CanStartTask(in InteractionContext context)
        {
            // This ensures locked stations cannot be restarted after repeated failures.
            return maxFailuresBeforeLock <= 0 || _failureCount < maxFailuresBeforeLock;
        }

        protected override string GetPromptForState(in InteractionContext context)
        {
            // This provides explicit difficulty and lock state to players on interact prompt.
            if (maxFailuresBeforeLock > 0 && _failureCount >= maxFailuresBeforeLock)
            {
                return $"{DisplayName}: Locked";
            }

            if (CurrentState == RepairTaskState.Completed)
            {
                return $"{DisplayName}: Completed";
            }

            if (CurrentState == RepairTaskState.InProgress)
            {
                return ActiveClientId == context.InteractorClientId
                    ? $"{DisplayName}: Committing ({difficulty})"
                    : $"{DisplayName}: In use";
            }

            return $"{InteractPrompt} ({difficulty})";
        }

        protected override void OnTaskStarted(in InteractionContext context)
        {
            base.OnTaskStarted(context);
            ApplyColor(activeColor);
        }

        protected override void OnTaskCompleted()
        {
            base.OnTaskCompleted();
            ApplyColor(completeColor);
        }

        protected override void OnTaskCancelled(string reason)
        {
            base.OnTaskCancelled(reason);

            // This applies optional failure accumulation for medium/hard stations that should punish mistakes.
            if (maxFailuresBeforeLock > 0)
            {
                _failureCount = Mathf.Clamp(_failureCount + 1, 0, maxFailuresBeforeLock);
            }

            if (maxFailuresBeforeLock > 0 && _failureCount >= maxFailuresBeforeLock)
            {
                SetState(RepairTaskState.Locked);
                ApplyColor(lockedColor);
                return;
            }

            SetState(RepairTaskState.FailedAttempt);
            ApplyColor(failedColor);
        }

        protected override void OnServerResetTask()
        {
            _failureCount = 0;
            ApplyColor(idleColor);
        }

        private void ApplyColor(Color color)
        {
            // This keeps placeholder world props readable without requiring authored VFX assets.
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
    }
}
