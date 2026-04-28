// File: Assets/_Project/UI/Tasks/RepairTaskStatusView.cs
using System.Text;
using HueDoneIt.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace HueDoneIt.UI.Tasks
{
    public sealed class RepairTaskStatusView : MonoBehaviour
    {
        [SerializeField] private PlayerRepairTaskParticipant participant;
        [SerializeField] private GameObject root;
        [SerializeField] private Text statusText;
        [SerializeField] private Image progressFill;

        private void Awake()
        {
            StyleExistingView();
            SetView(false, string.Empty, 0f);
        }

        private void StyleExistingView()
        {
            if (root != null)
            {
                Image rootImage = root.GetComponent<Image>();
                if (rootImage != null)
                {
                    rootImage.color = new Color(0.08f, 0.05f, 0.14f, 0.90f);
                    rootImage.raycastTarget = false;

                    Outline outline = root.GetComponent<Outline>();
                    if (outline == null)
                    {
                        outline = root.AddComponent<Outline>();
                    }

                    outline.effectColor = new Color(0.68f, 0.22f, 1f, 0.50f);
                    outline.effectDistance = new Vector2(2f, -2f);
                }
            }

            if (statusText != null)
            {
                statusText.fontStyle = FontStyle.Bold;
                statusText.fontSize = Mathf.Max(statusText.fontSize, 16);
                statusText.color = new Color(1f, 1f, 1f, 0.96f);
                statusText.raycastTarget = false;
            }

            if (progressFill != null)
            {
                progressFill.color = new Color(0.68f, 0.96f, 0.14f, 0.92f);
                progressFill.type = Image.Type.Filled;
                progressFill.fillMethod = Image.FillMethod.Horizontal;
                progressFill.raycastTarget = false;

                if (progressFill.transform.parent != null)
                {
                    Image bg = progressFill.transform.parent.GetComponent<Image>();
                    if (bg != null)
                    {
                        bg.color = new Color(0f, 0f, 0f, 0.60f);
                        bg.raycastTarget = false;
                    }
                }
            }
        }

        private void OnEnable()
        {
            BindParticipant();
        }

        private void OnDisable()
        {
            if (participant != null)
            {
                participant.TaskProgressUpdated -= HandleTaskProgressUpdated;
            }
        }

        private void Update()
        {
            if (participant == null)
            {
                BindParticipant();
            }

            if (participant != null && participant.HasActiveTask && !participant.IsWithinActiveTaskRange)
            {
                SetView(false, string.Empty, 0f);
            }
        }

        private void BindParticipant()
        {
            if (participant == null)
            {
                PlayerRepairTaskParticipant[] participants = FindObjectsByType<PlayerRepairTaskParticipant>(FindObjectsSortMode.None);
                foreach (PlayerRepairTaskParticipant candidate in participants)
                {
                    if (candidate.IsOwner && candidate.IsClient)
                    {
                        participant = candidate;
                        break;
                    }
                }
            }

            if (participant != null)
            {
                participant.TaskProgressUpdated -= HandleTaskProgressUpdated;
                participant.TaskProgressUpdated += HandleTaskProgressUpdated;
            }
        }

        private void HandleTaskProgressUpdated(NetworkRepairTask task, float progress01, bool isCompleted)
        {
            if (task == null || participant == null || !participant.IsWithinActiveTaskRange)
            {
                SetView(false, string.Empty, 0f);
                return;
            }

            SetView(true, BuildStatus(task, progress01, isCompleted), progress01);
        }

        private string BuildStatus(NetworkRepairTask task, float progress01, bool isCompleted)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(task.DisplayName.ToUpperInvariant());

            if (task.CurrentState == RepairTaskState.Locked)
            {
                sb.Append("\nLOCKED // Too many failed attempts.");
                sb.Append("\nMove to another station or wait for the next round reset.");
                return sb.ToString();
            }

            if (isCompleted)
            {
                sb.Append("\nCOMPLETED // System stable.");
                return sb.ToString();
            }

            if (task is PumpRepairTask pumpTask)
            {
                int startPercent = Mathf.RoundToInt(pumpTask.ConfirmationWindowStartNormalized * 100f);
                int endPercent = Mathf.RoundToInt(pumpTask.ConfirmationWindowEndNormalized * 100f);

                if (task.CurrentState == RepairTaskState.FailedAttempt)
                {
                    sb.Append("\nFAILED ATTEMPT");
                    sb.Append("\n1. Re-enter the station.");
                    sb.Append("\n2. Wait for the blue confirm band.");
                    sb.Append("\n3. Press [E] once inside the window.");
                    sb.Append($"\nAttempts left: {pumpTask.AttemptsRemaining}");
                    return sb.ToString();
                }

                sb.Append($"\nProgress: {Mathf.RoundToInt(progress01 * 100f)}% // Attempts left: {pumpTask.AttemptsRemaining}");
                sb.Append("\n1. Stay in the repair zone.");
                sb.Append($"\n2. Press [E] between {startPercent}% and {endPercent}%.");
                sb.Append("\n3. Do not leave after committing or the repair can fail.");
                return sb.ToString();
            }

            if (task is ShipRepairTask)
            {
                sb.Append($"\nProgress: {Mathf.RoundToInt(progress01 * 100f)}%");
                sb.Append("\n1. Stay inside the active repair path.");
                sb.Append("\n2. Advance checkpoint by checkpoint.");
                sb.Append("\n3. Keep moving until the final checkpoint closes the task.");
                if (participant.ShipCheckpointIndex > 0)
                {
                    sb.Append($"\nCurrent checkpoint: {participant.ShipCheckpointIndex}");
                }
                return sb.ToString();
            }

            if (task.CurrentState == RepairTaskState.FailedAttempt)
            {
                sb.Append("\nFAILED ATTEMPT");
                sb.Append("\nRe-enter the station and repeat the steps cleanly.");
                return sb.ToString();
            }

            sb.Append($"\nProgress: {Mathf.RoundToInt(progress01 * 100f)}%");
            sb.Append("\n1. Stay within the task radius.");
            sb.Append("\n2. Watch the prompt for the next interaction.");
            sb.Append("\n3. Press [E] again when the station asks for a commit or finish.");
            return sb.ToString();
        }

        private void SetView(bool visible, string text, float progress01)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }

            if (statusText != null)
            {
                statusText.text = text;
            }

            if (progressFill != null)
            {
                progressFill.fillAmount = Mathf.Clamp01(progress01);
            }
        }
    }
}
