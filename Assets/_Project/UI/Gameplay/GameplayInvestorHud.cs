// File: Assets/_Project/UI/Gameplay/GameplayInvestorHud.cs
using System.Collections.Generic;
using System.Text;
using HueDoneIt.Evidence;
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Inventory;
using HueDoneIt.Gameplay.Objectives;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Beta;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Gameplay.Sabotage;
using HueDoneIt.Roles;
using HueDoneIt.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HueDoneIt.UI.Gameplay
{
    public sealed class GameplayInvestorHud : MonoBehaviour
    {
        private const string TargetSceneName = BetaGameplaySceneCatalog.MainMap;
        private const int CanvasSortingOrder = 5000;
        private const float MapWorldMinX = -28f;
        private const float MapWorldMaxX = 28f;
        private const float MapWorldMinZ = -22f;
        private const float MapWorldMaxZ = 22f;
        private const int FeedCapacity = 10;

        [SerializeField] private bool showDebugOverlay;

        private Font _font;
        private GameObject _root;

        private Text _playerNameText;
        private Text _playerRoleText;
        private Text _timerText;
        private Text _shipText;
        private Text _objectiveText;
        private Text _taskText;
        private Text _interactionText;
        private Text _chatText;
        private Text _debugText;
        private Text _inventoryText;
        private Text _promptToastText;
        private Text _spectatorText;

        private Image _opacityFill;
        private Image _stabilityFill;
        private Image _diffusionFill;
        private Image _bannerPanel;
        private Text _bannerText;
        private Image _minimapBg;
        private RectTransform _minimapMarkerRoot;
        private RectTransform _playerMarker;
        private readonly List<RectTransform> _taskMarkers = new();

        private readonly Queue<string> _feedLines = new();
        private readonly List<MapMarkerEntry> _trackedMarkerTargets = new();
        private Bounds _dynamicMapBounds;
        private float _nextMapBoundsRefreshTime;

        private readonly struct MapMarkerEntry
        {
            public readonly Transform Target;
            public readonly Color Color;
            public readonly Vector2 Size;

            public MapMarkerEntry(Transform target, Color color, Vector2 size)
            {
                Target = target;
                Color = color;
                Size = size;
            }
        }

        private NetworkPlayerAvatar _localAvatar;
        private PlayerLifeState _localLifeState;
        private PlayerStaminaState _localStamina;
        private PlayerFloodZoneTracker _localFlood;
        private NetworkPlayerAuthoritativeMover _localMover;
        private PlayerInteractionDetector _interactionDetector;
        private PlayerKillInputController _killController;
        private PlayerRepairTaskParticipant _taskParticipant;
        private PlayerInventoryState _inventoryState;

        private NetworkRoundState _roundState;
        private PumpRepairTask _pumpTask;
        private FloodSequenceController _floodController;
        private GameplayObjectiveSystem _objectiveSystem;

        private float _lastPromptToastTime;
        private string _lastPromptText = string.Empty;
        private string _cachedObjectiveSummary = string.Empty;
        private NetworkRepairTask _currentRepairTask;
        private float _currentRepairTaskProgress;
        private ulong _lastTaskFeedNetworkObjectId = ulong.MaxValue;
        private int _lastTaskFeedProgressBucket = -1;
        private RepairTaskState _lastTaskFeedState = (RepairTaskState)255;
        private TaskObjectiveBase[] _boundAdvancedTasks = System.Array.Empty<TaskObjectiveBase>();
        private readonly Dictionary<TaskObjectiveBase, string> _advancedTaskStatusCache = new();
        private RoundPhase _lastFeedPhase = (RoundPhase)255;
        private string _lastFeedRoundMessage = string.Empty;
        private NetworkRoundState.PressureStage _lastFeedPressureStage = (NetworkRoundState.PressureStage)255;

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
            if (!scene.IsValid() || !BetaGameplaySceneCatalog.IsProductionGameplayScene(scene.name))
            {
                return;
            }

            if (FindFirstObjectByType<GameplayInvestorHud>() != null)
            {
                return;
            }

            GameObject canvasObject = new GameObject("GameplayRuntimeHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GameplayInvestorHud>();
        }

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            }

            BuildHud();
            SetHudVisible(SceneManager.GetActiveScene().name == TargetSceneName);
        }

        private void Update()
        {
            BindIfNeeded();
            Refresh();
        }

        private void OnDisable()
        {
            if (_interactionDetector != null)
            {
                _interactionDetector.PromptChanged -= HandlePromptChanged;
            }

            if (_taskParticipant != null)
            {
                _taskParticipant.TaskProgressUpdated -= HandleTaskProgressUpdated;
            }

            if (_inventoryState != null)
            {
                _inventoryState.InventoryChanged -= HandleInventoryChanged;
            }

            if (_objectiveSystem != null)
            {
                _objectiveSystem.SummaryChanged -= HandleObjectiveSummaryChanged;
            }

            UnbindAdvancedTasks();
        }

        private void BuildHud()
        {
            _root = new GameObject("GameplayHudRoot", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            RectTransform rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image vignette = CreatePanel("HudVignette", _root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.08f));
            vignette.raycastTarget = false;

            Image playerPanel = CreateSplashCard(
                "PlayerPanel",
                _root.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(26f, -486f),
                new Vector2(432f, -24f),
                new Color(0.05f, 0.03f, 0.12f, 0.92f),
                new Color(0.16f, 0.92f, 1f, 0.96f),
                "PLAYER STATUS",
                new Color(1f, 1f, 1f, 0.98f));

            Image avatarMedallion = CreatePanel("AvatarMedallion", playerPanel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -166f), new Vector2(118f, -62f), new Color(0.08f, 0.10f, 0.22f, 0.95f));
            Outline avatarOutline = avatarMedallion.gameObject.GetComponent<Outline>();
            if (avatarOutline != null)
            {
                avatarOutline.effectColor = new Color(0.15f, 0.92f, 1f, 0.65f);
                avatarOutline.effectDistance = new Vector2(3f, -3f);
            }
            Text avatarText = CreateText("AvatarText", avatarMedallion.transform, 44, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            avatarText.text = "?";
            avatarText.color = new Color(0.55f, 0.95f, 1f, 1f);

            _playerNameText = CreateText("PlayerName", playerPanel.transform, 28, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(132f, -58f), new Vector2(386f, -88f));
            _playerNameText.color = new Color(1f, 1f, 1f, 0.98f);
            _playerRoleText = CreateText("PlayerRole", playerPanel.transform, 14, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(132f, -96f), new Vector2(392f, -154f));
            _playerRoleText.color = new Color(0.72f, 0.92f, 1f, 0.96f);

            CreateText("OpacityLabel", playerPanel.transform, 14, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -176f), new Vector2(170f, -198f)).text = "OPACITY";
            _opacityFill = CreateBar("OpacityBar", playerPanel.transform, new Vector2(18f, -226f), new Vector2(388f, -202f), new Color(0.48f, 0.96f, 0.22f, 0.98f));
            CreateText("StabilityLabel", playerPanel.transform, 14, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -230f), new Vector2(170f, -252f)).text = "STABILITY";
            _stabilityFill = CreateBar("StabilityBar", playerPanel.transform, new Vector2(18f, -280f), new Vector2(388f, -256f), new Color(0.12f, 0.76f, 1f, 0.98f));
            CreateText("DiffusionLabel", playerPanel.transform, 14, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -284f), new Vector2(170f, -306f)).text = "DIFFUSION";
            _diffusionFill = CreateBar("DiffusionBar", playerPanel.transform, new Vector2(18f, -334f), new Vector2(388f, -310f), new Color(1f, 0.24f, 0.42f, 0.98f));

            CreateRosterCard(playerPanel.transform, "CrewCardA", new Vector2(18f, -454f), new Vector2(188f, -358f), new Color(0.12f, 0.88f, 1f, 0.96f), "CREW ALPHA");
            CreateRosterCard(playerPanel.transform, "CrewCardB", new Vector2(200f, -454f), new Vector2(370f, -358f), new Color(1f, 0.22f, 0.62f, 0.96f), "CREW BETA");

            Image roundPanel = CreateSplashCard(
                "RoundPanel",
                _root.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(-360f, -166f),
                new Vector2(360f, -24f),
                new Color(0.06f, 0.04f, 0.12f, 0.94f),
                new Color(0.08f, 0.78f, 1f, 0.96f),
                "HUE DONE IT",
                new Color(1f, 1f, 1f, 1f));
            CreateMiniHeader(roundPanel.transform, "ModeHeader", new Vector2(18f, -102f), new Vector2(188f, -58f), new Color(0.09f, 0.68f, 1f, 0.92f), "GAME MODE", 15);
            CreateMiniHeader(roundPanel.transform, "RoundHeader", new Vector2(530f, -102f), new Vector2(702f, -58f), new Color(1f, 0.14f, 0.54f, 0.92f), "ROUND", 15);
            _timerText = CreateText("Timer", roundPanel.transform, 46, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(198f, -112f), new Vector2(522f, -46f));
            _timerText.color = Color.white;
            _shipText = CreateText("ShipText", roundPanel.transform, 16, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(56f, -154f), new Vector2(664f, -112f));
            _shipText.color = new Color(0.84f, 0.92f, 1f, 0.96f);

            _bannerPanel = CreateSplashCard(
                "CenterBanner",
                _root.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-340f, -88f),
                new Vector2(340f, 88f),
                new Color(0.03f, 0.03f, 0.08f, 0.92f),
                new Color(1f, 0.84f, 0.14f, 0.96f),
                "EVENT",
                new Color(0.08f, 0.05f, 0f, 1f));
            _bannerText = CreateText("CenterBannerText", _bannerPanel.transform, 24, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(22f, 20f), new Vector2(-22f, -22f));
            _bannerText.color = new Color(1f, 1f, 1f, 0.98f);
            _bannerPanel.gameObject.SetActive(false);

            Image objectivesPanel = CreateSplashCard(
                "ObjectivesPanel",
                _root.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-424f, -346f),
                new Vector2(-24f, -24f),
                new Color(0.05f, 0.06f, 0.08f, 0.94f),
                new Color(1f, 0.86f, 0.12f, 0.96f),
                "OBJECTIVES",
                new Color(0.12f, 0.08f, 0f, 1f));
            _objectiveText = CreateText("ObjectiveText", objectivesPanel.transform, 16, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -64f), new Vector2(370f, -226f));
            _objectiveText.color = new Color(1f, 1f, 1f, 0.97f);

            Image minimapPanel = CreateSplashCard(
                "MinimapPanel",
                _root.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-424f, -642f),
                new Vector2(-24f, -372f),
                new Color(0.06f, 0.04f, 0.10f, 0.94f),
                new Color(0.66f, 0.22f, 1f, 0.96f),
                "MINIMAP",
                new Color(1f, 1f, 1f, 1f));
            _minimapBg = CreatePanel("Minimap", minimapPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -240f), new Vector2(-20f, -64f), new Color(0.07f, 0.07f, 0.10f, 0.96f));
            _minimapMarkerRoot = _minimapBg.rectTransform;
            _playerMarker = CreateMarker("PlayerMarker", _minimapMarkerRoot, new Color(0.96f, 1f, 0.28f, 1f), new Vector2(14f, 14f));

            Image promptPanel = CreateSplashCard(
                "PromptPanel",
                _root.transform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-404f, -52f),
                new Vector2(-24f, 22f),
                new Color(0.09f, 0.12f, 0.03f, 0.94f),
                new Color(0.68f, 0.96f, 0.12f, 0.96f),
                "NOTICE",
                new Color(0.08f, 0.11f, 0f, 1f));
            _promptToastText = CreateText("PromptToast", promptPanel.transform, 20, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(18f, -56f), new Vector2(360f, -16f));
            _promptToastText.color = new Color(1f, 1f, 1f, 0.98f);
            _promptToastText.text = string.Empty;

            Image inventoryPanel = CreateSplashCard(
                "InventoryPanel",
                _root.transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-468f, 24f),
                new Vector2(-24f, 248f),
                new Color(0.04f, 0.08f, 0.16f, 0.94f),
                new Color(0.10f, 0.72f, 1f, 0.96f),
                "INVENTORY",
                new Color(0f, 0f, 0f, 1f));
            _inventoryText = CreateText("InventoryText", inventoryPanel.transform, 15, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(22f, -64f), new Vector2(420f, -150f));
            _inventoryText.color = new Color(1f, 1f, 1f, 0.97f);
            CreateQuickSlot(inventoryPanel.transform, "QuickSlot1", new Vector2(20f, 20f), new Vector2(96f, 92f), new Color(1f, 0.22f, 0.72f, 0.94f), "1");
            CreateQuickSlot(inventoryPanel.transform, "QuickSlot2", new Vector2(108f, 20f), new Vector2(184f, 92f), new Color(1f, 0.78f, 0.10f, 0.94f), "2");
            CreateQuickSlot(inventoryPanel.transform, "QuickSlot3", new Vector2(196f, 20f), new Vector2(272f, 92f), new Color(0.12f, 0.76f, 1f, 0.94f), "3");

            Image chatPanel = CreateSplashCard(
                "ChatPanel",
                _root.transform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(24f, 24f),
                new Vector2(406f, 196f),
                new Color(0.08f, 0.04f, 0.12f, 0.50f),
                new Color(0.62f, 0.22f, 1f, 0.62f),
                "FEED",
                new Color(1f, 1f, 1f, 1f));
            _chatText = CreateText("ChatText", chatPanel.transform, 13, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(20f, -60f), new Vector2(368f, -20f));
            _chatText.color = new Color(1f, 1f, 1f, 0.62f);

            Image bottomPanel = CreateSplashCard(
                "BottomPanel",
                _root.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-436f, 22f),
                new Vector2(436f, 248f),
                new Color(0.10f, 0.04f, 0.14f, 0.95f),
                new Color(0.86f, 0.24f, 0.64f, 0.96f),
                "INTERACT",
                new Color(1f, 1f, 1f, 1f));
            _interactionText = CreateText("InteractionText", bottomPanel.transform, 22, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(24f, -60f), new Vector2(848f, -92f));
            _interactionText.color = new Color(0.92f, 1f, 0.22f, 0.98f);
            _taskText = CreateText("TaskText", bottomPanel.transform, 15, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(24f, -104f), new Vector2(848f, -176f));
            _taskText.color = new Color(1f, 1f, 1f, 0.97f);
            CreateAbilityButton(bottomPanel.transform, "Ability1", new Vector2(24f, 22f), new Vector2(168f, 112f), new Color(0.10f, 0.78f, 1f, 0.96f), "ABILITY 1", "Q");
            CreateAbilityButton(bottomPanel.transform, "Ability2", new Vector2(182f, 22f), new Vector2(326f, 112f), new Color(1f, 0.24f, 0.62f, 0.96f), "ABILITY 2", "E");
            CreateAbilityButton(bottomPanel.transform, "Ability3", new Vector2(340f, 22f), new Vector2(484f, 112f), new Color(1f, 0.74f, 0.10f, 0.96f), "ABILITY 3", "R");
            CreateAbilityButton(bottomPanel.transform, "Ability4", new Vector2(498f, 22f), new Vector2(642f, 112f), new Color(0.68f, 0.94f, 0.12f, 0.96f), "ABILITY 4", "F");
            CreateAbilityButton(bottomPanel.transform, "Ability5", new Vector2(656f, 22f), new Vector2(800f, 112f), new Color(0.62f, 0.22f, 1f, 0.96f), "ITEM", "X");

            CreateCrosshair(_root.transform);

            _spectatorText = CreateText("SpectatorText", _root.transform, 18, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-320f, -188f), new Vector2(320f, -154f));
            _spectatorText.color = new Color(1f, 0.86f, 0.22f, 0.98f);
            _spectatorText.text = string.Empty;

            _debugText = CreateText("DebugText", _root.transform, 13, FontStyle.Bold, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-360f, -108f), new Vector2(-18f, -20f));
            _debugText.color = new Color(1f, 1f, 1f, 0.92f);
            _debugText.text = string.Empty;
        }

        private void BindIfNeeded()
        {
            _roundState ??= FindFirstObjectByType<NetworkRoundState>();
            _pumpTask ??= FindFirstObjectByType<PumpRepairTask>();
            _floodController ??= FindFirstObjectByType<FloodSequenceController>();
            _objectiveSystem ??= FindFirstObjectByType<GameplayObjectiveSystem>();

            if (_objectiveSystem != null)
            {
                _objectiveSystem.SummaryChanged -= HandleObjectiveSummaryChanged;
                _objectiveSystem.SummaryChanged += HandleObjectiveSummaryChanged;
            }

            RebindAdvancedTasksIfNeeded();

            if (_localAvatar != null)
            {
                return;
            }

            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (!avatar.IsOwner || !avatar.IsClient)
                {
                    continue;
                }

                _localAvatar = avatar;
                _localLifeState = avatar.GetComponent<PlayerLifeState>();
                _localStamina = avatar.GetComponent<PlayerStaminaState>();
                _localFlood = avatar.GetComponent<PlayerFloodZoneTracker>();
                _localMover = avatar.GetComponent<NetworkPlayerAuthoritativeMover>();
                _interactionDetector = avatar.GetComponent<PlayerInteractionDetector>();
                _killController = avatar.GetComponent<PlayerKillInputController>();
                _taskParticipant = avatar.GetComponent<PlayerRepairTaskParticipant>();
                _inventoryState = avatar.GetComponent<PlayerInventoryState>();

                if (_interactionDetector != null)
                {
                    _interactionDetector.PromptChanged -= HandlePromptChanged;
                    _interactionDetector.PromptChanged += HandlePromptChanged;
                }

                if (_taskParticipant != null)
                {
                    _taskParticipant.TaskProgressUpdated -= HandleTaskProgressUpdated;
                    _taskParticipant.TaskProgressUpdated += HandleTaskProgressUpdated;
                }

                if (_inventoryState != null)
                {
                    _inventoryState.InventoryChanged -= HandleInventoryChanged;
                    _inventoryState.InventoryChanged += HandleInventoryChanged;
                }

                AddFeedLine("SYSTEM: HUD online.");
                break;
            }
        }

        private void HandlePromptChanged(string prompt, bool visible)
        {
            _interactionText.text = visible ? prompt : string.Empty;
            if (visible && prompt != _lastPromptText)
            {
                _lastPromptText = prompt;
                _lastPromptToastTime = Time.time;
                _promptToastText.text = prompt;
                AddFeedLine("SYSTEM: " + prompt);
            }
        }

        private void HandleTaskProgressUpdated(NetworkRepairTask task, float progress01, bool completed)
        {
            _currentRepairTask = task;
            _currentRepairTaskProgress = progress01;
            if (task == null)
            {
                _lastTaskFeedNetworkObjectId = ulong.MaxValue;
                _lastTaskFeedProgressBucket = -1;
                _lastTaskFeedState = (RepairTaskState)255;
                return;
            }

            int progressBucket = Mathf.Clamp(Mathf.FloorToInt(progress01 * 5f), 0, 5);
            bool taskChanged = _lastTaskFeedNetworkObjectId != task.NetworkObjectId;
            bool stateChanged = _lastTaskFeedState != task.CurrentState;
            bool progressChanged = progressBucket != _lastTaskFeedProgressBucket;

            if (!taskChanged && !stateChanged && !progressChanged)
            {
                return;
            }

            _lastTaskFeedNetworkObjectId = task.NetworkObjectId;
            _lastTaskFeedProgressBucket = progressBucket;
            _lastTaskFeedState = task.CurrentState;

            AddFeedLine(completed
                ? $"SYSTEM: Completed {task.DisplayName}."
                : $"SYSTEM: {task.DisplayName} {task.CurrentState} ({Mathf.RoundToInt(progress01 * 100f)}%).");
        }
        private void RebindAdvancedTasksIfNeeded()
        {
            TaskObjectiveBase[] tasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            System.Array.Sort(tasks, (a, b) => string.CompareOrdinal(a != null ? a.TaskId : string.Empty, b != null ? b.TaskId : string.Empty));

            bool same = tasks.Length == _boundAdvancedTasks.Length;
            if (same)
            {
                for (int i = 0; i < tasks.Length; i++)
                {
                    if (tasks[i] != _boundAdvancedTasks[i])
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same)
            {
                return;
            }

            UnbindAdvancedTasks();
            _boundAdvancedTasks = tasks;
            for (int i = 0; i < _boundAdvancedTasks.Length; i++)
            {
                TaskObjectiveBase task = _boundAdvancedTasks[i];
                if (task == null)
                {
                    continue;
                }

                task.TaskChanged += HandleAdvancedTaskChanged;
                _advancedTaskStatusCache[task] = BuildAdvancedTaskFeedKey(task);
            }
        }

        private void UnbindAdvancedTasks()
        {
            if (_boundAdvancedTasks != null)
            {
                for (int i = 0; i < _boundAdvancedTasks.Length; i++)
                {
                    TaskObjectiveBase task = _boundAdvancedTasks[i];
                    if (task != null)
                    {
                        task.TaskChanged -= HandleAdvancedTaskChanged;
                    }
                }
            }

            _boundAdvancedTasks = System.Array.Empty<TaskObjectiveBase>();
            _advancedTaskStatusCache.Clear();
        }

        private void HandleAdvancedTaskChanged(TaskObjectiveBase task)
        {
            if (task == null)
            {
                return;
            }

            string next = BuildAdvancedTaskFeedKey(task);
            if (_advancedTaskStatusCache.TryGetValue(task, out string previous) && previous == next)
            {
                return;
            }

            _advancedTaskStatusCache[task] = next;
            AddFeedLine($"SYSTEM: {task.DisplayName} {task.CurrentState}. {task.CurrentStatusText}");
        }

        private static string BuildAdvancedTaskFeedKey(TaskObjectiveBase task)
        {
            return task == null
                ? string.Empty
                : $"{task.CurrentState}|{task.CurrentStepIndex}|{task.FailureCount}|{task.CurrentStatusText}";
        }


        private void HandleInventoryChanged()
        {
            AddFeedLine("SYSTEM: Inventory updated.");
        }

        private void HandleObjectiveSummaryChanged(string summary)
        {
            if (summary == _cachedObjectiveSummary)
            {
                return;
            }

            _cachedObjectiveSummary = summary;
            AddFeedLine("SYSTEM: Objectives updated.");
        }

        private void Refresh()
        {
            bool inGameplayScene = SceneManager.GetActiveScene().name == TargetSceneName;
            SetHudVisible(inGameplayScene);
            if (!inGameplayScene)
            {
                return;
            }

            if (_localAvatar == null || _roundState == null)
            {
                RefreshBindingFallback();
                return;
            }

            float opacity01 = Mathf.Clamp01(_localAvatar.Opacity01);
            float stability01 = _localStamina != null ? _localStamina.Normalized : (_localMover != null ? _localMover.Stamina01 : 1f);

            UpdateRoundFeed();

            float diffusion01 = _localFlood != null ? _localFlood.Saturation01 : Mathf.Clamp01(1f - opacity01);

            _opacityFill.fillAmount = opacity01;
            _stabilityFill.fillAmount = stability01;
            if (_diffusionFill != null)
            {
                _diffusionFill.fillAmount = diffusion01;
                _diffusionFill.color = Color.Lerp(new Color(0.10f, 0.28f, 0.75f, 0.85f), new Color(1f, 0.12f, 0.06f, 0.98f), diffusion01);
            }

            _opacityFill.color = Color.Lerp(new Color(1f, 0.18f, 0.18f, 0.95f), new Color(0.50f, 0.95f, 0.42f, 0.95f), opacity01);
            _stabilityFill.color = Color.Lerp(new Color(1f, 0.42f, 0.15f, 0.95f), new Color(0.24f, 0.75f, 1f, 0.95f), stability01);

            _playerNameText.text = string.IsNullOrWhiteSpace(_localAvatar.PlayerLabel) ? "PLAYER" : _localAvatar.PlayerLabel.ToUpperInvariant();
            _playerRoleText.text = BuildRoleLine(opacity01, stability01);
            _spectatorText.text = _localLifeState != null && !_localLifeState.IsAlive
                ? "SPECTATOR // Tab cycle players // F free camera"
                : string.Empty;
            _timerText.text = "SHIP EXPLODES IN " + FormatTime(_roundState.RoundTimeRemaining);
            _shipText.text = BuildShipLine();
            _objectiveText.text = BuildObjectives();
            _inventoryText.text = BuildInventory();
            _taskText.text = BuildTaskStatus();
            _chatText.text = BuildFeed();
            _debugText.enabled = showDebugOverlay;
            _debugText.text = showDebugOverlay ? BuildDebugText(opacity01, stability01) : string.Empty;
            RefreshCenterBanner();

            if (Time.time - _lastPromptToastTime > 3f)
            {
                _promptToastText.text = string.Empty;
            }

            RefreshMinimap();
        }

        private void RefreshBindingFallback()
        {
            _opacityFill.fillAmount = 1f;
            _stabilityFill.fillAmount = 1f;
            if (_diffusionFill != null)
            {
                _diffusionFill.fillAmount = 0f;
            }
            _playerNameText.text = "PLAYER BINDING";
            _playerRoleText.text = _localAvatar == null ? "Waiting for local player avatar" : "Waiting for round state";
            _timerText.text = "--:--";
            _shipText.text = "HUD ONLINE // BINDING GAMEPLAY SYSTEMS";
            _objectiveText.text = "If this persists after spawning, the local player prefab or NetworkRoundState failed to spawn/bind.";
            _inventoryText.text = "Slot 1: --\nSlot 2: --\nSlot 3: --";
            _taskText.text = "No active task bound yet.";
            _chatText.text = BuildFeed();
            _interactionText.text = string.Empty;
            _spectatorText.text = string.Empty;
            _debugText.enabled = showDebugOverlay;
            _debugText.text = showDebugOverlay ? "HUD fallback visible. LocalAvatar=" + (_localAvatar != null) + " RoundState=" + (_roundState != null) : string.Empty;
            _bannerPanel.gameObject.SetActive(true);
            _bannerText.text = "HUD ONLINE\nWaiting for gameplay bindings";
        }

        private void UpdateRoundFeed()
        {
            if (_roundState == null)
            {
                return;
            }

            if (_lastFeedPhase != _roundState.CurrentPhase)
            {
                _lastFeedPhase = _roundState.CurrentPhase;
                AddFeedLine("ROUND: Phase changed to " + _roundState.CurrentPhase + ".");
            }

            if (_lastFeedPressureStage != _roundState.CurrentPressureStage)
            {
                _lastFeedPressureStage = _roundState.CurrentPressureStage;
                AddFeedLine("ROUND: Pressure stage " + _roundState.CurrentPressureStage + ".");
            }

            string message = _roundState.RoundMessage;
            if (!string.IsNullOrWhiteSpace(message) && message != _lastFeedRoundMessage)
            {
                _lastFeedRoundMessage = message;
                AddFeedLine("ROUND: " + message);
            }
        }

        private string BuildRoleLine(float opacity01, float stability01)
        {
            string role = _killController != null ? _killController.CurrentRole.ToString().ToUpperInvariant() : "COLOR";
            string flood = _localFlood != null ? _localFlood.CurrentZoneState.ToString().ToUpperInvariant() : "DRY";
            string gravity = _localMover != null ? BuildGravityLabel(_localMover.CurrentGravityMultiplier) : "1.00g";
            float diffusion01 = _localFlood != null ? _localFlood.Saturation01 : Mathf.Clamp01(1f - opacity01);
            return $"ROLE: {role}   FLOOD: {flood}   GRAVITY: {gravity}\nOPACITY {Mathf.RoundToInt(opacity01 * 100f)}%   STABILITY {Mathf.RoundToInt(stability01 * 100f)}%   DIFFUSION {Mathf.RoundToInt(diffusion01 * 100f)}%";
        }

        private string BuildShipLine()
        {
            string phase = _roundState.CurrentPhase.ToString();
            string pressure = _roundState.CurrentPressureStage.ToString();
            string pressurePercent = Mathf.RoundToInt(_roundState.Pressure01 * 100f) + "%";
            string blowup = _roundState.RoundTimeRemaining <= 25f ? " // SHIP CRITICAL" : string.Empty;
            string pressureEvents = $" // Sabotage {_roundState.SabotageEventCount} / Stabilized {_roundState.CrewStabilizationEventCount} / Events {_roundState.EnvironmentEventCount}";
            string meeting = _roundState.CurrentPhase == RoundPhase.Reported
                ? $" // Votes {_roundState.MeetingVotesCast}/{_roundState.MeetingEligibleVotes}"
                : string.Empty;
            return $"Phase: {phase} // Pressure: {pressure} ({pressurePercent}){pressureEvents}{meeting}{blowup}";
        }

        private string BuildObjectives()
        {
            if (_objectiveSystem != null && !string.IsNullOrWhiteSpace(_objectiveSystem.CachedSummary))
            {
                return _objectiveSystem.CachedSummary;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(_roundState.CurrentObjective)
                ? "• Restore the ship and survive."
                : "• " + _roundState.CurrentObjective);

            if (_roundState.CurrentPhase == RoundPhase.Reported && !string.IsNullOrWhiteSpace(_roundState.MeetingSummary))
            {
                sb.AppendLine("• Meeting: " + _roundState.MeetingSummary);
            }

            if (_pumpTask != null)
            {
                sb.AppendLine($"• Main Pump: {_pumpTask.CurrentState} ({_pumpTask.AttemptsRemaining} attempts)");
            }

            return sb.ToString().TrimEnd();
        }

        private string BuildInventory()
        {
            if (_inventoryState == null)
            {
                return "1. [Missing Inventory Component]\n2. [Missing Inventory Component]\n3. [Missing Inventory Component]";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(_inventoryState.BuildInventorySummary());

            PlayerRole role = _killController != null ? _killController.CurrentRole : PlayerRole.Color;
            sb.AppendLine();
            sb.AppendLine(role == PlayerRole.Bleach ? "Bleach kit ready near targets/sabotage." : "Use items at matching task stations.");
            sb.AppendLine("G: drop first held item");
            return sb.ToString().TrimEnd();
        }

        private string BuildTaskStatus()
        {
            if (_currentRepairTask != null)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Task: {_currentRepairTask.DisplayName}");
                sb.AppendLine($"State: {_currentRepairTask.CurrentState}");
                sb.AppendLine($"Progress: {Mathf.RoundToInt(_currentRepairTaskProgress * 100f)}%");

                if (_currentRepairTask is PumpRepairTask pumpTask)
                {
                    sb.Append($"Pump window: {Mathf.RoundToInt(pumpTask.ConfirmationWindowStartNormalized * 100f)}% - {Mathf.RoundToInt(pumpTask.ConfirmationWindowEndNormalized * 100f)}%");
                }
                else if (_currentRepairTask is ShipRepairTask shipTask)
                {
                    sb.Append($"Difficulty: {shipTask.Difficulty}. Stay in range and hit timing gates.");
                }

                return sb.ToString();
            }

            if (_interactionDetector != null && _interactionDetector.CurrentInteractable is TaskStepInteractable stepInteractable && stepInteractable.OwnerTask != null)
            {
                TaskObjectiveBase ownerTask = stepInteractable.OwnerTask;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Task: {ownerTask.DisplayName}");
                sb.AppendLine($"State: {ownerTask.CurrentState}");
                sb.AppendLine($"Objective: {ownerTask.CurrentObjectiveText}");
                sb.AppendLine($"Status: {ownerTask.CurrentStatusText}");
                sb.Append($"Failures: {ownerTask.FailureCount}");
                return sb.ToString();
            }

            string nearestHint = BuildNearestObjectiveHint();
            return "READY\nE: use highlighted task or item\nG: drop first held item\nR: report when available" +
                   (string.IsNullOrWhiteSpace(nearestHint) ? string.Empty : "\n" + nearestHint);
        }

        private string BuildNearestObjectiveHint()
        {
            if (_localAvatar == null)
            {
                return string.Empty;
            }

            Transform bestTarget = null;
            string bestLabel = string.Empty;
            float bestDistanceSqr = float.MaxValue;

            TaskObjectiveBase[] advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = advancedTasks[i];
                if (task == null || task.IsCompleted || task.IsLocked)
                {
                    continue;
                }

                float distanceSqr = (task.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = task.transform;
                    bestLabel = task.DisplayName;
                }
            }

            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task == null || task.IsCompleted || task.CurrentState == RepairTaskState.Locked)
                {
                    continue;
                }

                float distanceSqr = (task.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = task.transform;
                    bestLabel = task.DisplayName;
                }
            }

            NetworkDecontaminationStation[] decontaminationStations = FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None);
            for (int i = 0; i < decontaminationStations.Length; i++)
            {
                NetworkDecontaminationStation station = decontaminationStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkEmergencySealStation[] sealStations = FindObjectsByType<NetworkEmergencySealStation>(FindObjectsSortMode.None);
            for (int i = 0; i < sealStations.Length; i++)
            {
                NetworkEmergencySealStation station = sealStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkFloodgateStation[] floodgateStations = FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None);
            for (int i = 0; i < floodgateStations.Length; i++)
            {
                NetworkFloodgateStation station = floodgateStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkSafeRoomBeacon[] safeRoomBeacons = FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None);
            for (int i = 0; i < safeRoomBeacons.Length; i++)
            {
                NetworkSafeRoomBeacon beacon = safeRoomBeacons[i];
                if (beacon == null || (!beacon.IsReady && !beacon.IsActive))
                {
                    continue;
                }

                float distanceSqr = (beacon.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = beacon.transform;
                    bestLabel = beacon.DisplayName;
                }
            }

            NetworkPaintScannerStation[] scannerStations = FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None);
            for (int i = 0; i < scannerStations.Length; i++)
            {
                NetworkPaintScannerStation scanner = scannerStations[i];
                if (scanner == null || !scanner.IsReady)
                {
                    continue;
                }

                float distanceSqr = (scanner.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = scanner.transform;
                    bestLabel = scanner.DisplayName;
                }
            }

            NetworkVitalsStation[] vitalsStations = FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None);
            for (int i = 0; i < vitalsStations.Length; i++)
            {
                NetworkVitalsStation station = vitalsStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkBleachVent[] vents = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            for (int i = 0; i < vents.Length; i++)
            {
                NetworkBleachVent vent = vents[i];
                if (vent == null || vent.IsSealed)
                {
                    continue;
                }

                float distanceSqr = (vent.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = vent.transform;
                    bestLabel = vent.DisplayName;
                }
            }

            NetworkEvidenceShard[] evidenceShards = FindObjectsByType<NetworkEvidenceShard>(FindObjectsSortMode.None);
            for (int i = 0; i < evidenceShards.Length; i++)
            {
                NetworkEvidenceShard shard = evidenceShards[i];
                if (shard == null || !shard.IsActionable)
                {
                    continue;
                }

                float distanceSqr = (shard.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = shard.transform;
                    bestLabel = shard.DisplayName;
                }
            }

            NetworkEmergencyMeetingConsole meetingConsole = FindFirstObjectByType<NetworkEmergencyMeetingConsole>();
            if (meetingConsole != null && meetingConsole.IsReady)
            {
                float distanceSqr = (meetingConsole.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = meetingConsole.transform;
                    bestLabel = meetingConsole.DisplayName;
                }
            }

            NetworkSecurityCameraStation[] cameraStations = FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None);
            for (int i = 0; i < cameraStations.Length; i++)
            {
                NetworkSecurityCameraStation station = cameraStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkAlarmTripwire[] tripwires = FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None);
            for (int i = 0; i < tripwires.Length; i++)
            {
                NetworkAlarmTripwire tripwire = tripwires[i];
                if (tripwire == null || (!tripwire.IsReady && !tripwire.IsArmed))
                {
                    continue;
                }

                float distanceSqr = (tripwire.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = tripwire.transform;
                    bestLabel = tripwire.DisplayName;
                }
            }

            NetworkInkWellStation[] inkWells = FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None);
            for (int i = 0; i < inkWells.Length; i++)
            {
                NetworkInkWellStation station = inkWells[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkFalseEvidenceStation[] smearKits = FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None);
            for (int i = 0; i < smearKits.Length; i++)
            {
                NetworkFalseEvidenceStation station = smearKits[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            if (_roundState != null && _roundState.CurrentPhase == RoundPhase.Reported)
            {
                NetworkVotingPodium[] votingPodiums = FindObjectsByType<NetworkVotingPodium>(FindObjectsSortMode.None);
                for (int i = 0; i < votingPodiums.Length; i++)
                {
                    NetworkVotingPodium podium = votingPodiums[i];
                    if (podium == null)
                    {
                        continue;
                    }

                    float distanceSqr = (podium.transform.position - _localAvatar.transform.position).sqrMagnitude;
                    if (distanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
                        bestTarget = podium.transform;
                        bestLabel = podium.DisplayName;
                    }
                }
            }

            NetworkCrewRallyStation[] rallyStations = FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None);
            for (int i = 0; i < rallyStations.Length; i++)
            {
                NetworkCrewRallyStation station = rallyStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkBulkheadLockStation[] bulkheadStations = FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None);
            for (int i = 0; i < bulkheadStations.Length; i++)
            {
                NetworkBulkheadLockStation station = bulkheadStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                float distanceSqr = (station.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = station.transform;
                    bestLabel = station.DisplayName;
                }
            }

            NetworkCalloutBeacon[] calloutBeacons = FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None);
            for (int i = 0; i < calloutBeacons.Length; i++)
            {
                NetworkCalloutBeacon beacon = calloutBeacons[i];
                if (beacon == null || !beacon.IsReady)
                {
                    continue;
                }

                float distanceSqr = (beacon.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = beacon.transform;
                    bestLabel = beacon.DisplayName;
                }
            }

            NetworkSabotageConsole[] sabotageConsoles = FindObjectsByType<NetworkSabotageConsole>(FindObjectsSortMode.None);
            for (int i = 0; i < sabotageConsoles.Length; i++)
            {
                NetworkSabotageConsole sabotageConsole = sabotageConsoles[i];
                if (sabotageConsole == null || !sabotageConsole.IsReady)
                {
                    continue;
                }

                float distanceSqr = (sabotageConsole.transform.position - _localAvatar.transform.position).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = sabotageConsole.transform;
                    bestLabel = sabotageConsole.DisplayName;
                }
            }

            if (bestTarget == null)
            {
                return string.Empty;
            }

            float distance = Mathf.Sqrt(bestDistanceSqr);
            Vector3 localDirection = _localAvatar.transform.InverseTransformDirection((bestTarget.position - _localAvatar.transform.position).normalized);
            string direction = Mathf.Abs(localDirection.x) > Mathf.Abs(localDirection.z)
                ? (localDirection.x >= 0f ? "right" : "left")
                : (localDirection.z >= 0f ? "ahead" : "behind");

            return $"Nearest objective: {bestLabel} // {distance:0}m {direction}";
        }

        private void RefreshCenterBanner()
        {
            if (_bannerPanel == null || _bannerText == null || _roundState == null)
            {
                return;
            }

            string banner = BuildCenterBannerText();
            bool show = !string.IsNullOrWhiteSpace(banner);
            _bannerPanel.gameObject.SetActive(show);
            _bannerText.text = banner;
        }

        private string BuildCenterBannerText()
        {
            if (_roundState == null)
            {
                return string.Empty;
            }

            switch (_roundState.CurrentPhase)
            {
                case RoundPhase.Intro:
                    return BuildIntroBanner();

                case RoundPhase.Crash:
                    return "IMPACT\nBrace for launch. Movement unlocks after crash stabilization.";

                case RoundPhase.Reported:
                    return $"REPORT / EMERGENCY VOTE\nUse accusation pods near suspects or the skip pod. Votes {_roundState.MeetingVotesCast}/{_roundState.MeetingEligibleVotes}.\n{_roundState.MeetingSummary}";

                case RoundPhase.Resolved:
                    return $"ROUND RESOLVED: {_roundState.Winner}\n{_roundState.RoundMessage}\n{BuildProgressLine()}";

                case RoundPhase.PostRound:
                    return "RESETTING SHIP\nNext round begins when players are ready.";
            }

            if (_floodController != null)
            {
                if (_floodController.IsPulseActive)
                {
                    return $"FLOOD SURGE ACTIVE\n{_floodController.PulseZoneId} // {Mathf.CeilToInt(_floodController.PulseSecondsRemaining)}s remaining";
                }

                if (_floodController.IsPulseTelegraphActive && _floodController.SecondsUntilPulse <= 5f)
                {
                    return $"SURGE WARNING\n{_floodController.PulseZoneId} floods in {Mathf.CeilToInt(_floodController.SecondsUntilPulse)}s";
                }
            }

            return string.Empty;
        }

        private string BuildIntroBanner()
        {
            string role = _killController != null ? _killController.CurrentRole.ToString() : "Unassigned";
            string ability = _killController != null ? _killController.BuildAbilityStatusLine() : "Prepare to repair.";
            return $"ROLE: {role}\n{ability}\nColor wins by stabilizing systems. Bleach wins by hiding, stalling, or diffusing everyone.";
        }

        private string BuildProgressLine()
        {
            if (_roundState == null)
            {
                return string.Empty;
            }

            _roundState.GetCriticalSystemProgress(out int criticalDone, out int criticalRequired, out int criticalTotal, out int criticalLocked);
            _roundState.GetMaintenanceProgress(out int maintenanceDone, out int maintenanceRequired, out int maintenanceTotal, out int maintenanceLocked);
            return $"Critical {criticalDone}/{criticalRequired} required ({criticalLocked}/{criticalTotal} locked) // Maintenance {maintenanceDone}/{maintenanceRequired} required ({maintenanceLocked}/{maintenanceTotal} locked)";
        }

        private string BuildFeed()
        {
            if (_feedLines.Count == 0)
            {
                return "[SYSTEM] Waiting for activity...";
            }

            StringBuilder sb = new StringBuilder();
            foreach (string line in _feedLines)
            {
                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        private string BuildDebugText(float opacity01, float stability01)
        {
            float flood01 = _localFlood != null ? _localFlood.CurrentWaterLevel01 : 0f;
            float sat01 = _localFlood != null ? _localFlood.Saturation01 : 0f;
            float gravity = _localMover != null ? _localMover.CurrentGravityMultiplier : 1f;
            int activeLeaks = CountActiveBleachLeaks();
            return $"Opacity {opacity01:0.00}\nStability {stability01:0.00}\nFlood {flood01:0.00}\nSaturation {sat01:0.00}\nGravity {gravity:0.00}x\nActive leaks {activeLeaks}\nReady decon {CountReadyDecontaminationStations()}\nReady floodgates {CountReadyFloodgateStations()}\nSafe rooms {CountReadySafeRoomBeacons()}\nScanners {CountReadyPaintScannerStations()}\nVitals {CountReadyVitalsStations()}\nOpen vents {CountOpenBleachVents()}\nEvidence {CountActionableEvidenceShards()}\nCameras {CountReadySecurityCameraStations()}\nTripwires {CountRelevantAlarmTripwires()}\nInk wells {CountReadyInkWellStations()}\nSmear kits {CountReadyFalseEvidenceStations()}\nRally {CountReadyCrewRallyStations()}\nBulkheads {CountReadyBulkheadLocks()}\nCallouts {CountReadyCalloutBeacons()}\nVote pods {CountMeetingVotingPodiums()}\nVotes {_roundState.MeetingVotesCast}/{_roundState.MeetingEligibleVotes}\nMeeting {(IsMeetingConsoleReady() ? "ready" : "locked")}";
        }

        private static int CountActiveBleachLeaks()
        {
            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            int active = 0;
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null && !hazards[i].IsSuppressed)
                {
                    active++;
                }
            }

            return active;
        }

        private static int CountReadyDecontaminationStations()
        {
            NetworkDecontaminationStation[] stations = FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None);
            int ready = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    ready++;
                }
            }

            return ready;
        }

        private static int CountReadyFloodgateStations()
        {
            NetworkFloodgateStation[] stations = FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None);
            int ready = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    ready++;
                }
            }

            return ready;
        }

        private static int CountReadySafeRoomBeacons()
        {
            NetworkSafeRoomBeacon[] beacons = FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None);
            int ready = 0;
            for (int i = 0; i < beacons.Length; i++)
            {
                if (beacons[i] != null && (beacons[i].IsReady || beacons[i].IsActive))
                {
                    ready++;
                }
            }

            return ready;
        }

        private static int CountReadyPaintScannerStations()
        {
            NetworkPaintScannerStation[] stations = FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None);
            int ready = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    ready++;
                }
            }

            return ready;
        }

        private static int CountReadyVitalsStations()
        {
            NetworkVitalsStation[] stations = FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None);
            int ready = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    ready++;
                }
            }

            return ready;
        }

        private static int CountOpenBleachVents()
        {
            NetworkBleachVent[] vents = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            int open = 0;
            for (int i = 0; i < vents.Length; i++)
            {
                if (vents[i] != null && !vents[i].IsSealed)
                {
                    open++;
                }
            }

            return open;
        }

        private static int CountActionableEvidenceShards()
        {
            NetworkEvidenceShard[] shards = FindObjectsByType<NetworkEvidenceShard>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i] != null && shards[i].IsActionable)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountReadySecurityCameraStations()
        {
            NetworkSecurityCameraStation[] stations = FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRelevantAlarmTripwires()
        {
            NetworkAlarmTripwire[] tripwires = FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < tripwires.Length; i++)
            {
                if (tripwires[i] != null && (tripwires[i].IsReady || tripwires[i].IsArmed))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountReadyInkWellStations()
        {
            NetworkInkWellStation[] stations = FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountReadyFalseEvidenceStations()
        {
            NetworkFalseEvidenceStation[] stations = FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsMeetingConsoleReady()
        {
            NetworkEmergencyMeetingConsole meetingConsole = FindFirstObjectByType<NetworkEmergencyMeetingConsole>();
            return meetingConsole != null && meetingConsole.IsReady;
        }

        private static int CountReadyCrewRallyStations()
        {
            NetworkCrewRallyStation[] stations = FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountReadyBulkheadLocks()
        {
            NetworkBulkheadLockStation[] stations = FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].IsReady)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountReadyCalloutBeacons()
        {
            NetworkCalloutBeacon[] beacons = FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < beacons.Length; i++)
            {
                if (beacons[i] != null && beacons[i].IsReady)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountMeetingVotingPodiums()
        {
            return FindObjectsByType<NetworkVotingPodium>(FindObjectsSortMode.None).Length;
        }

        private static string BuildGravityLabel(float gravityMultiplier)
        {
            if (gravityMultiplier <= 0.06f)
            {
                return "ZERO-G";
            }

            if (gravityMultiplier < 0.98f)
            {
                return $"{gravityMultiplier:0.00}g";
            }

            return "1.00g";
        }

        private void AddFeedLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (_feedLines.Count >= FeedCapacity)
            {
                _feedLines.Dequeue();
            }

            _feedLines.Enqueue(line);
        }

        private void RefreshMinimap()
        {
            if (_minimapMarkerRoot == null || _localAvatar == null)
            {
                return;
            }

            _trackedMarkerTargets.Clear();
            RefreshDynamicMapBounds();

            Vector2 size = _minimapMarkerRoot.rect.size;
            _playerMarker.anchoredPosition = WorldToMap(_localAvatar.transform.position, size);
            _playerMarker.localRotation = Quaternion.Euler(0f, 0f, -_localAvatar.transform.eulerAngles.y);

            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task == null || task.IsCompleted)
                {
                    continue;
                }

                AddMapMarker(task.transform, new Color(0.22f, 0.82f, 1f, 1f), new Vector2(8f, 8f));
            }

            TaskObjectiveBase[] advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = advancedTasks[i];
                if (task == null || task.IsCompleted || task.IsLocked)
                {
                    continue;
                }

                AddMapMarker(task.transform, new Color(1f, 0.78f, 0.18f, 1f), new Vector2(9f, 9f));
            }

            NetworkDecontaminationStation[] decontaminationMapStations = FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None);
            for (int i = 0; i < decontaminationMapStations.Length; i++)
            {
                NetworkDecontaminationStation station = decontaminationMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.25f, 1f, 0.78f, 1f), new Vector2(8f, 8f));
            }

            NetworkEmergencySealStation[] sealStations = FindObjectsByType<NetworkEmergencySealStation>(FindObjectsSortMode.None);
            for (int i = 0; i < sealStations.Length; i++)
            {
                NetworkEmergencySealStation station = sealStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.42f, 1f, 0.28f, 1f), new Vector2(8f, 8f));
            }

            NetworkFloodgateStation[] floodgateMapStations = FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None);
            for (int i = 0; i < floodgateMapStations.Length; i++)
            {
                NetworkFloodgateStation station = floodgateMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.22f, 0.55f, 1f, 1f), new Vector2(8f, 8f));
            }

            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            for (int i = 0; i < hazards.Length; i++)
            {
                NetworkBleachLeakHazard hazard = hazards[i];
                if (hazard == null || hazard.IsSuppressed)
                {
                    continue;
                }

                AddMapMarker(hazard.transform, new Color(0.95f, 0.95f, 0.95f, 1f), new Vector2(10f, 10f));
            }

            NetworkSafeRoomBeacon[] safeRoomMapBeacons = FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None);
            for (int i = 0; i < safeRoomMapBeacons.Length; i++)
            {
                NetworkSafeRoomBeacon beacon = safeRoomMapBeacons[i];
                if (beacon == null || (!beacon.IsReady && !beacon.IsActive))
                {
                    continue;
                }

                AddMapMarker(beacon.transform, beacon.IsActive ? new Color(0.32f, 1f, 1f, 1f) : new Color(0.28f, 1f, 0.48f, 1f), new Vector2(9f, 9f));
            }

            NetworkPaintScannerStation[] scannerMapStations = FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None);
            for (int i = 0; i < scannerMapStations.Length; i++)
            {
                NetworkPaintScannerStation scanner = scannerMapStations[i];
                if (scanner == null || !scanner.IsReady)
                {
                    continue;
                }

                AddMapMarker(scanner.transform, new Color(1f, 0.55f, 1f, 1f), new Vector2(8f, 8f));
            }

            NetworkVitalsStation[] vitalsMapStations = FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None);
            for (int i = 0; i < vitalsMapStations.Length; i++)
            {
                NetworkVitalsStation station = vitalsMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.22f, 1f, 0.95f, 1f), new Vector2(8f, 8f));
            }

            NetworkBleachVent[] ventMapTargets = FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None);
            for (int i = 0; i < ventMapTargets.Length; i++)
            {
                NetworkBleachVent vent = ventMapTargets[i];
                if (vent == null)
                {
                    continue;
                }

                AddMapMarker(vent.transform, vent.IsSealed ? new Color(0.2f, 1f, 0.58f, 1f) : new Color(0.85f, 0.12f, 1f, 1f), new Vector2(7f, 7f));
            }

            NetworkEvidenceShard[] evidenceMapShards = FindObjectsByType<NetworkEvidenceShard>(FindObjectsSortMode.None);
            for (int i = 0; i < evidenceMapShards.Length; i++)
            {
                NetworkEvidenceShard shard = evidenceMapShards[i];
                if (shard == null || !shard.IsActionable)
                {
                    continue;
                }

                AddMapMarker(shard.transform, new Color(1f, 0.95f, 0.15f, 1f), new Vector2(7f, 7f));
            }

            NetworkEmergencyMeetingConsole meetingMapConsole = FindFirstObjectByType<NetworkEmergencyMeetingConsole>();
            if (meetingMapConsole != null && meetingMapConsole.IsReady)
            {
                AddMapMarker(meetingMapConsole.transform, new Color(1f, 0.18f, 0.18f, 1f), new Vector2(10f, 10f));
            }

            if (_roundState != null && _roundState.CurrentPhase == RoundPhase.Reported)
            {
                NetworkVotingPodium[] votingMapPodiums = FindObjectsByType<NetworkVotingPodium>(FindObjectsSortMode.None);
                for (int i = 0; i < votingMapPodiums.Length; i++)
                {
                    NetworkVotingPodium podium = votingMapPodiums[i];
                    if (podium == null)
                    {
                        continue;
                    }

                    AddMapMarker(podium.transform, podium.IsSkipVote ? new Color(0.18f, 0.85f, 1f, 1f) : new Color(1f, 0.38f, 0.12f, 1f), new Vector2(9f, 9f));
                }
            }

            NetworkCrewRallyStation[] rallyMapStations = FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None);
            for (int i = 0; i < rallyMapStations.Length; i++)
            {
                NetworkCrewRallyStation station = rallyMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.2f, 1f, 0.48f, 1f), new Vector2(8f, 8f));
            }

            NetworkBulkheadLockStation[] bulkheadMapStations = FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None);
            for (int i = 0; i < bulkheadMapStations.Length; i++)
            {
                NetworkBulkheadLockStation station = bulkheadMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.16f, 0.72f, 1f, 1f), new Vector2(8f, 8f));
            }

            NetworkCalloutBeacon[] calloutMapBeacons = FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None);
            for (int i = 0; i < calloutMapBeacons.Length; i++)
            {
                NetworkCalloutBeacon beacon = calloutMapBeacons[i];
                if (beacon == null || !beacon.IsReady)
                {
                    continue;
                }

                AddMapMarker(beacon.transform, new Color(1f, 0.9f, 0.16f, 1f), new Vector2(7f, 7f));
            }

            NetworkSlimeLaunchPad[] launchMapPads = FindObjectsByType<NetworkSlimeLaunchPad>(FindObjectsSortMode.None);
            for (int i = 0; i < launchMapPads.Length; i++)
            {
                NetworkSlimeLaunchPad pad = launchMapPads[i];
                if (pad == null)
                {
                    continue;
                }

                AddMapMarker(pad.transform, new Color(0.75f, 1f, 0.24f, 1f), new Vector2(6f, 6f));
            }

            NetworkSecurityCameraStation[] cameraMapStations = FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None);
            for (int i = 0; i < cameraMapStations.Length; i++)
            {
                NetworkSecurityCameraStation station = cameraMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.18f, 0.68f, 1f, 1f), new Vector2(8f, 8f));
            }

            NetworkAlarmTripwire[] tripwireMapTargets = FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None);
            for (int i = 0; i < tripwireMapTargets.Length; i++)
            {
                NetworkAlarmTripwire tripwire = tripwireMapTargets[i];
                if (tripwire == null || (!tripwire.IsReady && !tripwire.IsArmed))
                {
                    continue;
                }

                AddMapMarker(tripwire.transform, tripwire.IsArmed ? new Color(1f, 0.92f, 0.18f, 1f) : new Color(0.35f, 1f, 0.52f, 1f), new Vector2(7f, 7f));
            }

            NetworkInkWellStation[] inkWellMapStations = FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None);
            for (int i = 0; i < inkWellMapStations.Length; i++)
            {
                NetworkInkWellStation station = inkWellMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.94f, 0.22f, 1f, 1f), new Vector2(8f, 8f));
            }

            NetworkFalseEvidenceStation[] smearKitMapStations = FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None);
            for (int i = 0; i < smearKitMapStations.Length; i++)
            {
                NetworkFalseEvidenceStation station = smearKitMapStations[i];
                if (station == null || !station.IsReady)
                {
                    continue;
                }

                AddMapMarker(station.transform, new Color(0.82f, 0.16f, 1f, 1f), new Vector2(8f, 8f));
            }

            NetworkSabotageConsole[] sabotageConsoles = FindObjectsByType<NetworkSabotageConsole>(FindObjectsSortMode.None);
            for (int i = 0; i < sabotageConsoles.Length; i++)
            {
                NetworkSabotageConsole sabotageConsole = sabotageConsoles[i];
                if (sabotageConsole == null || !sabotageConsole.IsReady)
                {
                    continue;
                }

                AddMapMarker(sabotageConsole.transform, new Color(1f, 0.18f, 0.38f, 1f), new Vector2(9f, 9f));
            }

            EnsureTaskMarkers(_trackedMarkerTargets.Count);
            for (int i = 0; i < _taskMarkers.Count; i++)
            {
                bool active = i < _trackedMarkerTargets.Count && _trackedMarkerTargets[i].Target != null;
                _taskMarkers[i].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                MapMarkerEntry entry = _trackedMarkerTargets[i];
                _taskMarkers[i].anchoredPosition = WorldToMap(entry.Target.position, size);
                _taskMarkers[i].sizeDelta = entry.Size;
                Image markerImage = _taskMarkers[i].GetComponent<Image>();
                if (markerImage != null)
                {
                    markerImage.color = entry.Color;
                }
            }
        }

        private void AddMapMarker(Transform target, Color color, Vector2 markerSize)
        {
            if (target == null)
            {
                return;
            }

            _trackedMarkerTargets.Add(new MapMarkerEntry(target, color, markerSize));
        }

        private void RefreshDynamicMapBounds()
        {
            if (Time.unscaledTime < _nextMapBoundsRefreshTime && _dynamicMapBounds.size.sqrMagnitude > 0.01f)
            {
                return;
            }

            _nextMapBoundsRefreshTime = Time.unscaledTime + 1f;
            Bounds bounds = new Bounds(_localAvatar.transform.position, Vector3.one * 12f);
            bool initialized = false;

            EncapsulateMapPoint(ref bounds, ref initialized, _localAvatar.transform.position);

            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider colliderRef = colliders[i];
                if (colliderRef == null || colliderRef.isTrigger || colliderRef.GetComponentInParent<NetworkPlayerAvatar>() != null)
                {
                    continue;
                }

                Bounds candidate = colliderRef.bounds;
                Vector3 candidateSize = candidate.size;
                if ((candidateSize.x * candidateSize.y * candidateSize.z) < 0.001f)
                {
                    continue;
                }

                EncapsulateMapPoint(ref bounds, ref initialized, candidate.min);
                EncapsulateMapPoint(ref bounds, ref initialized, candidate.max);
            }

            if (!initialized)
            {
                bounds = new Bounds(_localAvatar.transform.position, new Vector3(MapWorldMaxX - MapWorldMinX, 1f, MapWorldMaxZ - MapWorldMinZ));
            }

            Vector3 finalSize = bounds.size;
            finalSize.x = Mathf.Max(finalSize.x + 8f, 40f);
            finalSize.z = Mathf.Max(finalSize.z + 8f, 32f);
            finalSize.y = 1f;
            bounds.size = finalSize;
            _dynamicMapBounds = bounds;
        }

        private static void EncapsulateMapPoint(ref Bounds bounds, ref bool initialized, Vector3 position)
        {
            if (!initialized)
            {
                bounds = new Bounds(position, Vector3.one * 12f);
                initialized = true;
                return;
            }

            bounds.Encapsulate(position);
        }

        private void EnsureTaskMarkers(int count)
        {
            while (_taskMarkers.Count < count)
            {
                _taskMarkers.Add(CreateMarker("TaskMarker_" + _taskMarkers.Count, _minimapMarkerRoot, new Color(0.22f, 0.82f, 1f, 1f), new Vector2(8f, 8f)));
            }
        }

        private Vector2 WorldToMap(Vector3 world, Vector2 size)
        {
            Bounds bounds = _dynamicMapBounds.size.sqrMagnitude > 0.01f
                ? _dynamicMapBounds
                : new Bounds(Vector3.zero, new Vector3(MapWorldMaxX - MapWorldMinX, 1f, MapWorldMaxZ - MapWorldMinZ));

            float minX = bounds.center.x - (bounds.size.x * 0.5f);
            float maxX = bounds.center.x + (bounds.size.x * 0.5f);
            float minZ = bounds.center.z - (bounds.size.z * 0.5f);
            float maxZ = bounds.center.z + (bounds.size.z * 0.5f);
            float x = Mathf.Clamp01(Mathf.InverseLerp(minX, maxX, world.x));
            float y = Mathf.Clamp01(Mathf.InverseLerp(minZ, maxZ, world.z));
            return new Vector2(Mathf.Lerp(12f, size.x - 12f, x), Mathf.Lerp(12f, size.y - 12f, y));
        }

        private Image CreateSplashCard(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color bodyColor, Color accentColor, string title, Color titleColor)
        {
            Image body = CreatePanel(name, parent, anchorMin, anchorMax, offsetMin, offsetMax, bodyColor);
            Outline outline = body.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(accentColor.r * 0.35f, accentColor.g * 0.35f, accentColor.b * 0.35f, 0.95f);
                outline.effectDistance = new Vector2(3f, -3f);
            }

            Shadow shadow = body.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
            shadow.effectDistance = new Vector2(4f, -4f);

            Image header = CreatePanel(name + "_Header", body.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -54f), new Vector2(-16f, -14f), new Color(accentColor.r, accentColor.g, accentColor.b, 0.96f));
            Outline headerOutline = header.GetComponent<Outline>();
            if (headerOutline != null)
            {
                headerOutline.effectColor = new Color(0f, 0f, 0f, 0.28f);
                headerOutline.effectDistance = new Vector2(2f, -2f);
            }

            Text headerText = CreateText(name + "_HeaderText", header.transform, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));
            headerText.text = title;
            headerText.color = titleColor;

            CreatePanel(name + "_AccentDotA", body.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -26f), new Vector2(-12f, -14f), accentColor);
            CreatePanel(name + "_AccentDotB", body.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(-28f, -28f), new Color(accentColor.r, accentColor.g, accentColor.b, 0.82f));
            return body;
        }

        private void CreateMiniHeader(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, Color color, string title, int fontSize)
        {
            Image header = CreatePanel(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), offsetMin, offsetMax, color);
            Text headerText = CreateText(name + "Text", header.transform, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            headerText.text = title;
            headerText.color = Color.white;
        }

        private void CreateRosterCard(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, Color color, string label)
        {
            Image card = CreatePanel(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), offsetMin, offsetMax, new Color(0.05f, 0.07f, 0.14f, 0.92f));
            Outline outline = card.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.95f);
            }
            Image badge = CreatePanel(name + "Badge", card.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -50f), new Vector2(50f, -10f), color);
            Text badgeText = CreateText(name + "BadgeText", badge.transform, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            badgeText.text = "●";
            badgeText.color = new Color(0.05f, 0.05f, 0.09f, 1f);
            Text title = CreateText(name + "Title", card.transform, 15, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(58f, -14f), new Vector2(160f, -34f));
            title.text = label;
            title.color = Color.white;
            Text sub = CreateText(name + "Sub", card.transform, 13, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(58f, -38f), new Vector2(160f, -62f));
            sub.text = "ROLE / STATUS";
            sub.color = color;
        }

        private void CreateQuickSlot(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, Color color, string slotKey)
        {
            Image slot = CreatePanel(name, parent, new Vector2(0f, 0f), new Vector2(0f, 0f), offsetMin, offsetMax, new Color(0.04f, 0.05f, 0.09f, 0.95f));
            Outline outline = slot.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(color.r * 0.4f, color.g * 0.4f, color.b * 0.4f, 0.95f);
            }

            Text icon = CreateText(name + "Icon", slot.transform, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 8f), new Vector2(0f, -10f));
            icon.text = "●";
            icon.color = color;

            Text key = CreateText(name + "Key", slot.transform, 16, FontStyle.Bold, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 4f), new Vector2(0f, 0f));
            key.text = slotKey;
            key.color = Color.white;
        }

        private void CreateAbilityButton(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, Color color, string label, string key)
        {
            Image button = CreatePanel(name, parent, new Vector2(0f, 0f), new Vector2(0f, 0f), offsetMin, offsetMax, new Color(0.05f, 0.05f, 0.10f, 0.95f));
            Outline outline = button.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.95f);
            }

            Image iconBubble = CreatePanel(name + "Icon", button.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-26f, -50f), new Vector2(26f, -8f), color);
            Text glyph = CreateText(name + "Glyph", iconBubble.transform, 24, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            glyph.text = "✦";
            glyph.color = new Color(0.05f, 0.05f, 0.09f, 1f);

            Text title = CreateText(name + "Label", button.transform, 15, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(8f, -58f), new Vector2(136f, -78f));
            title.text = label;
            title.color = Color.white;

            Text keycap = CreateText(name + "Key", button.transform, 20, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(8f, -84f), new Vector2(136f, -106f));
            keycap.text = key;
            keycap.color = color;
        }

        private void CreateCrosshair(Transform parent)
        {
            Image dot = CreatePanel("CrosshairDot", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-4f, -4f), new Vector2(4f, 4f), new Color(1f, 1f, 1f, 0.96f));
            dot.raycastTarget = false;
            CreatePanel("CrosshairUp", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-1f, 18f), new Vector2(1f, 42f), new Color(1f, 1f, 1f, 0.96f));
            CreatePanel("CrosshairDown", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-1f, -42f), new Vector2(1f, -18f), new Color(1f, 1f, 1f, 0.96f));
            CreatePanel("CrosshairLeft", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-42f, -1f), new Vector2(-18f, 1f), new Color(1f, 1f, 1f, 0.96f));
            CreatePanel("CrosshairRight", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, -1f), new Vector2(42f, 1f), new Color(1f, 1f, 1f, 0.96f));
        }

        private Image CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);

            return image;
        }

        private Image CreateBar(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax, Color fillColor)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.offsetMin = offsetMin;
            rootRect.offsetMax = offsetMax;

            Image bg = root.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.72f);
            bg.raycastTarget = false;

            GameObject fillObject = new GameObject(name + "_Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(root.transform, false);

            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            Image fill = fillObject.GetComponent<Image>();
            fill.raycastTarget = false;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;
            fill.color = fillColor;
            return fill;
        }

        private RectTransform CreateMarker(string name, Transform parent, Color color, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private Text CreateText(string name, Transform parent, int fontSize, FontStyle style, TextAnchor anchor, Vector2 offsetMin, Vector2 offsetMax)
        {
            return CreateText(name, parent, fontSize, style, anchor, new Vector2(0f, 1f), new Vector2(0f, 1f), offsetMin, offsetMax);
        }

        private Text CreateText(string name, Transform parent, int fontSize, FontStyle style, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Text text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;

            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.82f);
            outline.effectDistance = new Vector2(1f, -1f);

            return text;
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainder = totalSeconds % 60;
            return $"{minutes:00}:{remainder:00}";
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
