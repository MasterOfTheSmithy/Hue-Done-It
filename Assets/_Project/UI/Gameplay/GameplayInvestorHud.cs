// File: Assets/_Project/UI/Gameplay/GameplayInvestorHud.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Flood;
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
        private Image _healthFill;
        private Image _staminaFill;
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
            if (!scene.IsValid() || Object.FindFirstObjectByType<GameplayInvestorHud>() != null)
            {
                return;
            }

            if (Object.FindFirstObjectByType<NetworkRoundState>() == null)
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
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            _objectiveText = CreateText("Objective", _root.transform, font, 22, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-460f, -16f), new Vector2(460f, -92f));
            _interactionText = CreateText("InteractPrompt", _root.transform, font, 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.2f), new Vector2(-300f, -22f), new Vector2(300f, 22f));
            _stateText = CreateText("State", _root.transform, font, 16, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(520f, 84f));
            _debugText = CreateText("Debug", _root.transform, font, 14, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-420f, -120f), new Vector2(-24f, -24f));

            _healthFill = CreateBar("Health", _root.transform, new Vector2(0f, 0f), new Vector2(24f, 96f), new Color(0.85f, 0.2f, 0.2f, 0.95f));
            _staminaFill = CreateBar("Stamina", _root.transform, new Vector2(0f, 0f), new Vector2(24f, 128f), new Color(0.2f, 0.9f, 0.45f, 0.95f));

            _interactionText.text = string.Empty;
        }

        private void BindIfNeeded()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }

            if (_pumpTask == null)
            {
                _pumpTask = FindFirstObjectByType<PumpRepairTask>();
            }
            if (_floodSequenceController == null)
            {
                _floodSequenceController = FindFirstObjectByType<FloodSequenceController>();
            }

            if (_localAvatar == null)
            {
                NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
                for (int i = 0; i < avatars.Length; i++)
                {
                    if (avatars[i].IsOwner && avatars[i].IsClient)
                    {
                        _localAvatar = avatars[i];
                        _localLifeState = _localAvatar.GetComponent<PlayerLifeState>();
                        _localStamina = _localAvatar.GetComponent<PlayerStaminaState>();
                        _localMover = _localAvatar.GetComponent<NetworkPlayerAuthoritativeMover>();
                        _localFlood = _localAvatar.GetComponent<PlayerFloodZoneTracker>();
                        _interactionDetector = _localAvatar.GetComponent<PlayerInteractionDetector>();
                        BindInteractionPrompt();
                        SetHudVisible(true);
                        break;
                    }
                }
            }
        }

        private void BindInteractionPrompt()
        {
            if (_boundPrompt || _interactionDetector == null)
            {
                return;
            }

            _interactionDetector.PromptChanged += HandlePromptChanged;
            _boundPrompt = true;
        }

        private void UnbindInteractionPrompt()
        {
            if (!_boundPrompt || _interactionDetector == null)
            {
                return;
            }

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
            _debugText.enabled = showDebugOverlay;
            if (showDebugOverlay)
            {
                _debugText.text = BuildDebugText();
            }

            _healthFill.fillAmount = (_localLifeState != null && _localLifeState.IsAlive) ? 1f : 0f;
            _staminaFill.fillAmount = _localStamina != null ? _localStamina.Normalized : (_localMover != null ? _localMover.Stamina01 : 1f);
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
                return "Objective complete: pump repaired.";
            }

            return "Reach the pump and survive the flood.";
        }

        private string BuildStateText()
        {
            string locomotion = _localMover != null ? _localMover.CurrentState.ToString() : "Unknown";
            string phase = _roundState.CurrentPhase.ToString();
            string flood = _localFlood != null ? _localFlood.CurrentZoneState.ToString() : "Dry";
            string pressure = _roundState.CurrentPressureStage.ToString();
            string danger = _floodSequenceController != null ? _floodSequenceController.BuildRoundPressureHint() : "Danger: Unknown";
            return $"Phase: {phase}   Pressure: {pressure}   Flood: {flood}   Move: {locomotion}\nDanger: {danger}";
        }

        private string BuildDebugText()
        {
            bool grounded = _localMover != null && _localMover.CurrentState == NetworkPlayerAuthoritativeMover.LocomotionState.Grounded;
            float stamina = _localStamina != null ? _localStamina.Normalized : (_localMover != null ? _localMover.Stamina01 : 1f);
            float floodLevel = _localFlood != null ? _localFlood.CurrentWaterLevel01 : 0f;
            string pulse = _floodSequenceController != null ? _floodSequenceController.BuildRoundPressureHint() : "No flood controller";
            float opacity = (_localLifeState != null && _localLifeState.IsAlive) ? 1f : 0f;
            return $"Grounded: {grounded}\nOpacity: {opacity:0.00}\nCohesion: {stamina:0.00}\nFloodLevel: {floodLevel:0.00}\nLocomotion: {_localMover?.CurrentState}\n{pulse}";
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
            bgRect.sizeDelta = new Vector2(280f, 22f);
            Image bg = panel.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);

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
            fillImage.fillOrigin = 0;
            fillImage.fillAmount = 1f;
            return fillImage;
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
