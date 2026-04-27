// File: Assets/_Project/UI/Tasks/RepairTaskStatusView.cs
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
                    rootImage.color = new Color(0.08f, 0.05f, 0.14f, 0.94f);
                    rootImage.raycastTarget = false;

                    Outline outline = root.GetComponent<Outline>();
                    if (outline == null)
                    {
                        outline = root.AddComponent<Outline>();
                    }

                    outline.effectColor = new Color(0.68f, 0.22f, 1f, 0.65f);
                    outline.effectDistance = new Vector2(2f, -2f);
                }
            }

            if (statusText != null)
            {
                statusText.fontStyle = FontStyle.Bold;
                statusText.fontSize = Mathf.Max(statusText.fontSize, 17);
                statusText.color = new Color(1f, 1f, 1f, 0.98f);
                statusText.raycastTarget = false;
            }

            if (progressFill != null)
            {
                progressFill.color = new Color(0.68f, 0.96f, 0.14f, 0.96f);
                progressFill.type = Image.Type.Filled;
                progressFill.fillMethod = Image.FillMethod.Horizontal;
                progressFill.raycastTarget = false;

                if (progressFill.transform.parent != null)
                {
                    Image bg = progressFill.transform.parent.GetComponent<Image>();
                    if (bg != null)
                    {
                        bg.color = new Color(0f, 0f, 0f, 0.70f);
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

            string status;
            if (task is PumpRepairTask pumpTask && task.CurrentState == RepairTaskState.InProgress)
            {
                int startPercent = Mathf.RoundToInt(pumpTask.ConfirmationWindowStartNormalized * 100f);
                int endPercent = Mathf.RoundToInt(pumpTask.ConfirmationWindowEndNormalized * 100f);
                status = $"{task.DisplayName}: {Mathf.RoundToInt(progress01 * 100f)}% | Attempts {pumpTask.AttemptsRemaining}\n[E] in ~{startPercent}-{endPercent}% window\nLeaving after commit can fail";
            }
            else if (task is PumpRepairTask failedPump && task.CurrentState == RepairTaskState.FailedAttempt)
            {
                status = $"{task.DisplayName}: FAILED\nAttempts remaining: {failedPump.AttemptsRemaining}";
            }
            else if (task.CurrentState == RepairTaskState.Locked)
            {
                status = $"{task.DisplayName}: LOCKED";
            }
            else if (isCompleted)
            {
                status = $"{task.DisplayName}: Completed";
            }
            else
            {
                string checkpoint = participant.ShipCheckpointIndex > 0 ? $"\nCheckpoint {participant.ShipCheckpointIndex} reached" : string.Empty;
                status = $"{task.DisplayName}: {Mathf.RoundToInt(progress01 * 100f)}%{checkpoint}\nStay in radius. Press E again to close/reset.";
            }

            SetView(true, status, progress01);
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
