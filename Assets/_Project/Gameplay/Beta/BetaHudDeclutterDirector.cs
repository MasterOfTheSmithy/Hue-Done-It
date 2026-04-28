// File: Assets/_Project/Gameplay/Beta/BetaHudDeclutterDirector.cs
using UnityEngine;
using UnityEngine.UI;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Runtime-only HUD polish layer. It keeps the important information visible, but lowers the weight
    /// of the activity feed and hides transient panels when they are not actively helping the player.
    /// </summary>
    [DefaultExecutionOrder(960)]
    [DisallowMultipleComponent]
    public sealed class BetaHudDeclutterDirector : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 0.5f;
        [SerializeField, Range(0.1f, 1f)] private float activityFeedAlpha = 0.54f;
        [SerializeField, Range(0.1f, 1f)] private float corePanelAlpha = 0.88f;
        [SerializeField, Range(0.1f, 1f)] private float chatTextAlpha = 0.76f;

        private float _nextRefreshTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            ApplyHudDeclutter();
        }

        private void ApplyHudDeclutter()
        {
            GameObject hudRoot = GameObject.Find("GameplayRuntimeHudCanvas");
            if (hudRoot == null)
            {
                return;
            }

            RectTransform rootRect = hudRoot.transform as RectTransform;
            if (rootRect == null)
            {
                return;
            }

            ApplyGeneralPanel(rootRect, "ObjectivesPanel", corePanelAlpha, Vector3.one * 0.97f);
            ApplyGeneralPanel(rootRect, "MinimapPanel", corePanelAlpha, Vector3.one * 0.96f);
            ApplyGeneralPanel(rootRect, "InventoryPanel", corePanelAlpha, Vector3.one * 0.97f);
            ApplyGeneralPanel(rootRect, "BottomPanel", 0.90f, Vector3.one);
            ApplyActivityFeed(rootRect);
            ApplyPromptVisibility(rootRect);
        }

        private void ApplyGeneralPanel(RectTransform rootRect, string panelName, float alpha, Vector3 scale)
        {
            Transform panel = rootRect.Find(panelName);
            if (panel == null)
            {
                return;
            }

            SetPanelAlpha(panel.gameObject, alpha);
            panel.localScale = scale;
        }

        private void ApplyActivityFeed(RectTransform rootRect)
        {
            Transform panel = rootRect.Find("ChatPanel");
            if (panel == null)
            {
                return;
            }

            RectTransform rect = panel as RectTransform;
            if (rect != null)
            {
                rect.offsetMin = new Vector2(18f, 18f);
                rect.offsetMax = new Vector2(380f, 182f);
            }

            SetPanelAlpha(panel.gameObject, activityFeedAlpha);

            Text chatText = panel.GetComponentInChildren<Text>(true);
            if (chatText != null)
            {
                Color color = chatText.color;
                color.a = chatTextAlpha;
                chatText.color = color;
            }

            Outline outline = panel.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(0.42f, 0.18f, 0.72f, 0.35f);
            }
        }

        private void ApplyPromptVisibility(RectTransform rootRect)
        {
            Transform panel = rootRect.Find("PromptPanel");
            if (panel == null)
            {
                return;
            }

            Text promptText = panel.Find("PromptToast") != null ? panel.Find("PromptToast").GetComponent<Text>() : null;
            bool hasPrompt = promptText != null && !string.IsNullOrWhiteSpace(promptText.text);

            panel.gameObject.SetActive(hasPrompt);
            if (hasPrompt)
            {
                SetPanelAlpha(panel.gameObject, 0.92f);
            }
        }

        private static void SetPanelAlpha(GameObject panel, float alpha)
        {
            if (panel == null)
            {
                return;
            }

            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = panel.AddComponent<CanvasGroup>();
            }

            group.alpha = alpha;
        }
    }
}
