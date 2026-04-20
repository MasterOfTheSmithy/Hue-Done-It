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
            SetView(false, string.Empty, 0f);
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
            if (task == null)
            {
                SetView(false, string.Empty, 0f);
                return;
            }

            string status = isCompleted
                ? $"{task.DisplayName}: Completed"
                : $"{task.DisplayName}: {Mathf.RoundToInt(progress01 * 100f)}%";

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
