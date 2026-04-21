// File: Assets/_Project/UI/Interaction/InteractionPromptView.cs
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Players;
using UnityEngine;
using UnityEngine.UI;

namespace HueDoneIt.UI.Interaction
{
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractionDetector detector;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Text promptText;

        private bool _subscribed;

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
            TryBindDetector();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (detector == null)
            {
                TryBindDetector();
            }
        }

        private void TryBindDetector()
        {
            if (detector == null)
            {
                NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
                foreach (NetworkPlayerAvatar avatar in avatars)
                {
                    if (!avatar.IsOwner || !avatar.IsClient)
                    {
                        continue;
                    }

                    detector = avatar.GetComponent<PlayerInteractionDetector>();
                    if (detector != null)
                    {
                        break;
                    }
                }
            }

            if (detector == null || _subscribed)
            {
                return;
            }

            detector.PromptChanged += HandlePromptChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || detector == null)
            {
                return;
            }

            detector.PromptChanged -= HandlePromptChanged;
            _subscribed = false;
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
