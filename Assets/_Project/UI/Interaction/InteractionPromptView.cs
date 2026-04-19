// File: Assets/_Project/UI/Interaction/InteractionPromptView.cs
using HueDoneIt.Gameplay.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace HueDoneIt.UI.Interaction
{
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractionDetector detector;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Text promptText;

        private void Awake()
        {
            if (promptRoot != null)
            {
                promptRoot.SetActive(false);
            }

            if (promptText != null)
            {
                promptText.text = string.Empty;
            }
        }

        private void OnEnable()
        {
            BindDetector();
        }

        private void OnDisable()
        {
            if (detector != null)
            {
                detector.PromptChanged -= HandlePromptChanged;
            }
        }

        private void Update()
        {
            if (detector == null)
            {
                BindDetector();
            }
        }

        private void BindDetector()
        {
            if (detector == null)
            {
                PlayerInteractionDetector[] detectors = FindObjectsByType<PlayerInteractionDetector>(FindObjectsSortMode.None);
                foreach (PlayerInteractionDetector candidate in detectors)
                {
                    if (candidate.IsOwner && candidate.IsClient)
                    {
                        detector = candidate;
                        break;
                    }
                }
            }

            if (detector != null)
            {
                detector.PromptChanged -= HandlePromptChanged;
                detector.PromptChanged += HandlePromptChanged;
            }
        }

        private void HandlePromptChanged(string prompt, bool visible)
        {
            if (promptText != null)
            {
                promptText.text = prompt;
            }

            if (promptRoot != null)
            {
                promptRoot.SetActive(visible);
            }
        }
    }
}
