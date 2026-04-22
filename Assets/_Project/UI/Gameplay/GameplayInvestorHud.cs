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
    // Runtime gameplay HUD used in Gameplay_Undertint.
    // This HUD intentionally includes objective text, Opacity, Stability, flood pressure, interaction prompt,
    // and current task status so solo testing is readable without authored UI prefabs.
    public sealed class GameplayInvestorHud : MonoBehaviour
    {
        [SerializeField] private bool showDebugOverlay = true;

        private Text _objectiveText;
        private Text _interactionText;
        private Text _stateText;
        private Text _taskText;
        private Text _debugText;
        private Text _opacityLabel;
        private Text _stabilityLabel;

        private Image _opacityFill;
        private Image _stabilityFill;

        private GameObject _root;

        private NetworkPlayerAvatar _localAvatar;
        private PlayerLifeState _localLifeState;
        private PlayerStaminaState _localStability;
        private NetworkPlayerAuthoritativeMover _localMover;
        private PlayerFloodZoneTracker _localFlood;
        private PlayerInteractionDetector _interactionDetector;
        private PlayerRepairTaskParticipant _taskParticipant;

        private NetworkRoundState _roundState;
        private PumpRepairTask _pumpTask;
        private FloodSequenceController _floodSequenceController;

        private bool _promptSubscribed;
        private bool _taskSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallRuntimeHud()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryAttach(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryAttach(scene);
        }

        private static void TryAttach(Scene scene)
        {
            if (!scene.IsValid() || scene.name != "Gameplay_Undertint")
            {
                return;
            }

            if (FindFirstObjectByType<GameplayInvestorHud>() != null)
            {
                return;
            }

            // Reuse an existing canvas if present; otherwise create one.
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("GameplayRuntimeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            UnbindPrompt();
            UnbindTask();
        }

        private void BuildHud()
        {
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            _root = new GameObject("InvestorHudRoot", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            RectTransform rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            _objectiveText = CreateText("Objective", _root.transform, font, 24, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-640f, -14f), new Vector2(640f, -96f));
            _stateText = CreateText("State", _root.transform, font, 17, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(700f, 126f));
            _taskText = CreateText("TaskStatus", _root.transform, font, 16, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 132f), new Vector2(780f, 230f));
            _interactionText = CreateText("InteractionPrompt", _root.transform, font, 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.18f), new Vector2(0.5f, 0.18f), new Vector2(-360f, -28f), new Vector2(360f, 28f));
            _debugText = CreateText("Debug", _root.transform, font, 14, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-420f, -152f), new Vector2(-24f, -22f));

            _opacityLabel = CreateText("OpacityLabel", _root.transform, font, 16, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 236f), new Vector2(340f, 266f));
            _stabilityLabel = CreateText("StabilityLabel", _root.transform, font, 16, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 204f), new Vector2(340f, 234f));

            _opacityFill = CreateBar("Opacity", _root.transform, new Vector2(0f, 0f), new Vector2(24f, 174f), new Color(0.88f, 0.42f, 0.42f, 0.95f));
            _stabilityFill = CreateBar("Stability", _root.transform, new Vector2(0f, 0f), new Vector2(24f, 142f), new Color(0.31f, 0.9f, 0.58f, 0.95f));

            _interactionText.text = string.Empty;
            _taskText.text = string.Empty;
        }

        private void BindIfNeeded()
        {
            _roundState ??= FindFirstObjectByType<NetworkRoundState>();
            _pumpTask ??= FindFirstObjectByType<PumpRepairTask>();
            _floodSequenceController ??= FindFirstObjectByType<FloodSequenceController>();

            if (_localAvatar != null)
            {
                return;
            }

            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            foreach (NetworkPlayerAvatar avatar in avatars)
            {
                if (!avatar.IsOwner || !avatar.IsClient)
                {
                    continue;
                }

                _localAvatar = avatar;
                _localLifeState = avatar.GetComponent<PlayerLifeState>();
                _localStability = avatar.GetComponent<PlayerStaminaState>();
                _localMover = avatar.GetComponent<NetworkPlayerAuthoritativeMover>();
                _localFlood = avatar.GetComponent<PlayerFloodZoneTracker>();
                _interactionDetector = avatar.GetComponent<PlayerInteractionDetector>();
                _taskParticipant = avatar.GetComponent<PlayerRepairTaskParticipant>();

                BindPrompt();
                BindTask();
                SetHudVisible(true);
                break;
            }
        }

        private void BindPrompt()
        {
            if (_promptSubscribed || _interactionDetector == null)
            {
                return;
            }

            _interactionDetector.PromptChanged += HandlePromptChanged;
            _promptSubscribed = true;
        }

        private void UnbindPrompt()
        {
            if (!_promptSubscribed || _interactionDetector == null)
            {
                return;
            }

            _interactionDetector.PromptChanged -= HandlePromptChanged;
            _promptSubscribed = false;
        }

        private void BindTask()
        {
            if (_taskSubscribed || _taskParticipant == null)
            {
                return;
            }

            _taskParticipant.TaskProgressUpdated += HandleTaskProgress;
            _taskSubscribed = true;
        }

        private void UnbindTask()
        {
            if (!_taskSubscribed || _taskParticipant == null)
            {
                return;
            }

            _taskParticipant.TaskProgressUpdated -= HandleTaskProgress;
            _taskSubscribed = false;
        }

        private void HandlePromptChanged(string prompt, bool visible)
        {
            _interactionText.text = visible ? prompt : string.Empty;
        }

        private void HandleTaskProgress(NetworkRepairTask task, float progress01, bool completed)
        {
            if (task == null)
            {
                _taskText.text = string.Empty;
                return;
            }

            if (task is PumpRepairTask pumpTask)
            {
                int start = Mathf.RoundToInt(pumpTask.ConfirmationWindowStartNormalized * 100f);
                int end = Mathf.RoundToInt(pumpTask.ConfirmationWindowEndNormalized * 100f);
                _taskText.text = $"Task: {pumpTask.DisplayName}\nState: {pumpTask.CurrentState}\nProgress: {Mathf.RoundToInt(progress01 * 100f)}%\nConfirm Window: {start}-{end}%\nAttempts Remaining: {pumpTask.AttemptsRemaining}";
                return;
            }

            _taskText.text = $"Task: {task.DisplayName}\nState: {task.CurrentState}\nProgress: {Mathf.RoundToInt(progress01 * 100f)}%";
        }

        private void Refresh()
        {
            if (_roundState == null || _localAvatar == null)
            {
                SetHudVisible(false);
                return;
            }

            SetHudVisible(true);

            float opacity01 = Mathf.Clamp01(_localAvatar.Opacity01);
            float stability01 = _localStability != null ? _localStability.Normalized : (_localMover != null ? _localMover.Stamina01 : 1f);

            _opacityFill.fillAmount = opacity01;
            _stabilityFill.fillAmount = stability01;

            _opacityLabel.text = $"Opacity: {Mathf.RoundToInt(opacity01 * 100f)}%";
            _stabilityLabel.text = $"Stability: {Mathf.RoundToInt(stability01 * 100f)}%";

            _objectiveText.text = BuildObjectiveText();
            _stateText.text = BuildStateText();

            _debugText.enabled = showDebugOverlay;
            if (showDebugOverlay)
            {
                _debugText.text = BuildDebugText(opacity01, stability01);
            }
        }

        private string BuildObjectiveText()
        {
            if (_localLifeState != null && !_localLifeState.IsAlive)
            {
                return "Eliminated: " + _localLifeState.LastStateReason;
            }

            if (!string.IsNullOrWhiteSpace(_roundState.CurrentObjective))
            {
                return _roundState.CurrentObjective;
            }

            if (_pumpTask != null && _pumpTask.IsCompleted)
            {
                return "Objective complete. Survive until round resolution.";
            }

            return "Repair the primary pump and survive flood pressure.";
        }

        private string BuildStateText()
        {
            string phase = _roundState.CurrentPhase.ToString();
            string pressure = _roundState.CurrentPressureStage.ToString();
            string floodState = _localFlood != null ? _localFlood.CurrentZoneState.ToString() : "Dry";
            string danger = _floodSequenceController != null ? _floodSequenceController.BuildRoundPressureHint() : "Flood status unavailable";
            string moveState = _localMover != null ? _localMover.CurrentState.ToString() : "Unknown";
            return $"Round Phase: {phase}\nPressure: {pressure}\nFlood Zone: {floodState}\nMovement: {moveState}\n{danger}";
        }

        private string BuildDebugText(float opacity01, float stability01)
        {
            float flood01 = _localFlood != null ? _localFlood.CurrentWaterLevel01 : 0f;
            float saturate01 = _localFlood != null ? _localFlood.Saturation01 : 0f;
            return $"Opacity01: {opacity01:0.00}\nStability01: {stability01:0.00}\nFloodLevel01: {flood01:0.00}\nSaturation01: {saturate01:0.00}\nTask: {_pumpTask?.CurrentState}";
        }

        private static Text CreateText(string name, Transform parent, Font font, int fontSize, TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Image CreateBar(string name, Transform parent, Vector2 anchorMinMax, Vector2 anchoredPosition, Color fillColor)
        {
            GameObject panel = new GameObject(name + "Bar", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            RectTransform bgRect = panel.GetComponent<RectTransform>();
            bgRect.anchorMin = anchorMinMax;
            bgRect.anchorMax = anchorMinMax;
            bgRect.pivot = new Vector2(0f, 0f);
            bgRect.anchoredPosition = anchoredPosition;
            bgRect.sizeDelta = new Vector2(310f, 24f);

            Image bg = panel.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.7f);

            GameObject fillObject = new GameObject(name + "Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(panel.transform, false);

            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            Image fill = fillObject.GetComponent<Image>();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;
            return fill;
        }

        private void SetHudVisible(bool visible)
        {
            if (_root != null)
            {
                _root.SetActive(visible);
            }
        }
    }
}
