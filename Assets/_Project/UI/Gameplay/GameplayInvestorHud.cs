// File: Assets/_Project/UI/Gameplay/GameplayInvestorHud.cs
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HueDoneIt.UI.Gameplay
{
    public sealed class GameplayInvestorHud : MonoBehaviour
    {
        [SerializeField] private bool showDebugOverlay = true;

        private Text _objectiveText;
        private Text _interactionText;
        private Text _stateText;
        private Text _debugText;
        private Text _opacityLabel;
        private Text _stabilityLabel;
        private Image _opacityFill;
        private Image _stabilityFill;
        private GameObject _root;

        private NetworkPlayerAvatar _localAvatar;
        private PlayerLifeState _localLifeState;
        private PlayerStaminaState _localStamina;
        private NetworkPlayerAuthoritativeMover _localMover;
        private PlayerFloodZoneTracker _localFlood;
        private PlayerInteractionDetector _interactionDetector;
        private NetworkRoundState _roundState;
        private PumpRepairTask _pumpTask;
        private FloodSequenceController _floodSequenceController;

        private bool _boundPrompt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallRuntimeHud()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryAttach(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            TryAttach(scene);
        }

        private static void TryAttach(Scene scene)
        {
            if (!scene.IsValid() || Object.FindFirstObjectByType<GameplayInvestorHud>() != null || scene.name != "Gameplay_Undertint")
            {
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new("GameplayRuntimeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }

            canvas.gameObject.AddComponent<GameplayInvestorHud>();
        }

        private void Awake()
        {
            BuildHud();
            SetHudVisible(false);
        }

        private void Update()
        {
            BindIfNeeded();
            Refresh();
        }

        private void OnDisable()
        {
            UnbindInteractionPrompt();
        }

        private void BuildHud()
        {
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _root = new GameObject("InvestorHudRoot", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            RectTransform rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;

            _objectiveText = CreateText("Objective", _root.transform, font, 23, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-550f, -16f), new Vector2(550f, -92f));
            _interactionText = CreateText("InteractPrompt", _root.transform, font, 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.19f), new Vector2(-360f, -24f), new Vector2(360f, 24f));
            _stateText = CreateText("State", _root.transform, font, 17, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(650f, 125f));
            _debugText = CreateText("Debug", _root.transform, font, 14, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-430f, -150f), new Vector2(-24f, -24f));
            _opacityLabel = CreateText("OpacityLabel", _root.transform, font, 16, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 120f), new Vector2(340f, 150f));
            _stabilityLabel = CreateText("StabilityLabel", _root.transform, font, 16, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 152f), new Vector2(340f, 182f));

            _opacityFill = CreateBar("Opacity", _root.transform, new Vector2(0f, 0f), new Vector2(24f, 90f), new Color(0.88f, 0.42f, 0.42f, 0.95f));
            _stabilityFill = CreateBar("Stability", _root.transform, new Vector2(0f, 0f), new Vector2(24f, 122f), new Color(0.31f, 0.9f, 0.58f, 0.95f));
            _interactionText.text = string.Empty;
        }

        private void BindIfNeeded()
        {
            _roundState ??= FindFirstObjectByType<NetworkRoundState>();
            _pumpTask ??= FindFirstObjectByType<PumpRepairTask>();
            _floodSequenceController ??= FindFirstObjectByType<FloodSequenceController>();

            if (_localAvatar == null)
            {
                foreach (NetworkPlayerAvatar avatar in FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None))
                {
                    if (!avatar.IsOwner || !avatar.IsClient) continue;
                    _localAvatar = avatar;
                    _localLifeState = avatar.GetComponent<PlayerLifeState>();
                    _localStamina = avatar.GetComponent<PlayerStaminaState>();
                    _localMover = avatar.GetComponent<NetworkPlayerAuthoritativeMover>();
                    _localFlood = avatar.GetComponent<PlayerFloodZoneTracker>();
                    _interactionDetector = avatar.GetComponent<PlayerInteractionDetector>();
                    BindInteractionPrompt();
                    SetHudVisible(true);
                    break;
                }
            }
        }

        private void BindInteractionPrompt()
        {
            if (_boundPrompt || _interactionDetector == null) return;
            _interactionDetector.PromptChanged += HandlePromptChanged;
            _boundPrompt = true;
        }

        private void UnbindInteractionPrompt()
        {
            if (!_boundPrompt || _interactionDetector == null) return;
            _interactionDetector.PromptChanged -= HandlePromptChanged;
            _boundPrompt = false;
        }

        private void HandlePromptChanged(string prompt, bool visible)
        {
            _interactionText.text = visible ? prompt : string.Empty;
        }

        private void Refresh()
        {
            if (_roundState == null || _localAvatar == null)
            {
                SetHudVisible(false);
                return;
            }

            SetHudVisible(true);
            _objectiveText.text = BuildObjectiveText();
            _stateText.text = BuildStateText();

            float opacity01 = _localAvatar != null ? _localAvatar.Opacity01 : 1f;
            float stability01 = _localStamina != null ? _localStamina.Normalized : 1f;
            _opacityFill.fillAmount = opacity01;
            _stabilityFill.fillAmount = stability01;
            _opacityLabel.text = $"Opacity: {Mathf.RoundToInt(opacity01 * 100f)}%";
            _stabilityLabel.text = $"Stability: {Mathf.RoundToInt(stability01 * 100f)}%";

            _debugText.enabled = showDebugOverlay;
            if (showDebugOverlay)
            {
                _debugText.text = BuildDebugText();
            }
        }

        private string BuildObjectiveText()
        {
            if (_localLifeState != null && !_localLifeState.IsAlive)
            {
                return "ELIMINATED - " + _localLifeState.LastStateReason;
            }

            if (!string.IsNullOrWhiteSpace(_roundState.CurrentObjective))
            {
                return _roundState.CurrentObjective;
            }

            if (_pumpTask != null && _pumpTask.IsCompleted)
            {
                return "Objective complete: Primary pump repaired. Survive the resolution phase.";
            }

            return "Repair the main pump, avoid flood pressure, and report remains when found.";
        }

        private string BuildStateText()
        {
            string phase = _roundState.CurrentPhase.ToString();
            string flood = _localFlood != null ? _localFlood.CurrentZoneState.ToString() : "Dry";
            string pressure = _roundState.CurrentPressureStage.ToString();
            string danger = _floodSequenceController != null ? _floodSequenceController.BuildRoundPressureHint() : "Danger: Unknown";
            string locomotion = _localMover != null ? _localMover.CurrentState.ToString() : "Unknown";
            return $"Round: {phase}   Pressure: {pressure}   Flood Zone: {flood}\nMovement: {locomotion}\n{danger}";
        }

        private string BuildDebugText()
        {
            float floodLevel = _localFlood != null ? _localFlood.CurrentWaterLevel01 : 0f;
            float opacity = _localAvatar != null ? _localAvatar.Opacity01 : 1f;
            float stability = _localStamina != null ? _localStamina.Normalized : 1f;
            return $"Task: {_pumpTask?.CurrentState}\nOpacity: {opacity:0.00}\nStability: {stability:0.00}\nFlood: {floodLevel:0.00}";
        }

        private static Text CreateText(string name, Transform parent, Font font, int fontSize, TextAnchor align, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Image CreateBar(string name, Transform parent, Vector2 anchorMinMax, Vector2 anchoredPos, Color fillColor)
        {
            GameObject panel = new(name + "Bar", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform bgRect = panel.GetComponent<RectTransform>();
            bgRect.anchorMin = anchorMinMax;
            bgRect.anchorMax = anchorMinMax;
            bgRect.pivot = new Vector2(0f, 0f);
            bgRect.anchoredPosition = anchoredPos;
            bgRect.sizeDelta = new Vector2(300f, 22f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.66f);

            GameObject fill = new(name + "Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(panel.transform, false);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 1f;
            return fillImage;
        }

        private void SetHudVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }
    }
}
