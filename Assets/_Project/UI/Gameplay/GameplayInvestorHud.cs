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
        private const string TargetSceneName = "Gameplay_Undertint";
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

        private Image _opacityFill;
        private Image _stabilityFill;
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
            if (!scene.IsValid() || scene.name != TargetSceneName)
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
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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

            Image playerPanel = CreatePanel("PlayerPanel", _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -190f), new Vector2(390f, -24f), new Color(0.07f, 0.10f, 0.18f, 0.88f));
            _playerNameText = CreateText("PlayerName", playerPanel.transform, 28, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -10f), new Vector2(360f, -44f));
            _playerRoleText = CreateText("PlayerRole", playerPanel.transform, 16, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -48f), new Vector2(360f, -74f));
            CreateText("OpacityLabel", playerPanel.transform, 16, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -88f), new Vector2(140f, -114f)).text = "OPACITY";
            _opacityFill = CreateBar("OpacityBar", playerPanel.transform, new Vector2(16f, -116f), new Vector2(340f, -96f), new Color(0.50f, 0.95f, 0.42f, 0.95f));
            CreateText("StabilityLabel", playerPanel.transform, 16, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -128f), new Vector2(140f, -154f)).text = "STABILITY";
            _stabilityFill = CreateBar("StabilityBar", playerPanel.transform, new Vector2(16f, -156f), new Vector2(340f, -136f), new Color(0.24f, 0.75f, 1f, 0.95f));

            Image roundPanel = CreatePanel("RoundPanel", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-320f, -132f), new Vector2(320f, -22f), new Color(0.06f, 0.08f, 0.16f, 0.90f));
            CreateText("GameTitle", roundPanel.transform, 24, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(20f, -8f), new Vector2(620f, -38f)).text = "HUE DONE IT // UNDERTINT";
            _timerText = CreateText("Timer", roundPanel.transform, 42, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(20f, -40f), new Vector2(620f, -84f));
            _shipText = CreateText("ShipText", roundPanel.transform, 16, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(20f, -84f), new Vector2(620f, -108f));

            _bannerPanel = CreatePanel("CenterBanner", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-360f, -92f), new Vector2(360f, 92f), new Color(0.03f, 0.03f, 0.06f, 0.88f));
            _bannerText = CreateText("CenterBannerText", _bannerPanel.transform, 26, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 24f), new Vector2(-24f, -24f));
            _bannerPanel.gameObject.SetActive(false);

            Image objectivesPanel = CreatePanel("ObjectivesPanel", _root.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-430f, -360f), new Vector2(-24f, -24f), new Color(0.08f, 0.10f, 0.07f, 0.90f));
            CreateText("ObjectivesHeader", objectivesPanel.transform, 22, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -10f), new Vector2(320f, -36f)).text = "OBJECTIVES";
            _objectiveText = CreateText("ObjectiveText", objectivesPanel.transform, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(18f, -42f), new Vector2(370f, -170f));
            CreateText("MinimapHeader", objectivesPanel.transform, 20, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -188f), new Vector2(320f, -212f)).text = "MINIMAP";
            _minimapBg = CreatePanel("Minimap", objectivesPanel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -330f), new Vector2(190f, -220f), new Color(0.04f, 0.05f, 0.08f, 0.95f));
            _minimapMarkerRoot = _minimapBg.rectTransform;
            _playerMarker = CreateMarker("PlayerMarker", _minimapMarkerRoot, new Color(1f, 1f, 1f, 1f), new Vector2(12f, 12f));

            Image inventoryPanel = CreatePanel("InventoryPanel", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-430f, 24f), new Vector2(-24f, 220f), new Color(0.05f, 0.11f, 0.18f, 0.90f));
            CreateText("InventoryHeader", inventoryPanel.transform, 22, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -10f), new Vector2(320f, -36f)).text = "INVENTORY";
            _inventoryText = CreateText("InventoryText", inventoryPanel.transform, 16, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(18f, -44f), new Vector2(370f, -170f));

            Image chatPanel = CreatePanel("ChatPanel", _root.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(430f, 250f), new Color(0.08f, 0.05f, 0.14f, 0.90f));
            CreateText("ChatHeader", chatPanel.transform, 22, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18f, -10f), new Vector2(320f, -36f)).text = "SYSTEM FEED";
            _chatText = CreateText("ChatText", chatPanel.transform, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(18f, -42f), new Vector2(390f, -200f));

            Image bottomPanel = CreatePanel("BottomPanel", _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-390f, 22f), new Vector2(390f, 190f), new Color(0.10f, 0.05f, 0.14f, 0.92f));
            _interactionText = CreateText("InteractionText", bottomPanel.transform, 24, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(18f, -14f), new Vector2(740f, -44f));
            _taskText = CreateText("TaskText", bottomPanel.transform, 16, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(18f, -52f), new Vector2(740f, -154f));

            _promptToastText = CreateText("PromptToast", _root.transform, 24, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(-420f, -24f), new Vector2(420f, 24f));
            _promptToastText.color = new Color(1f, 0.92f, 0.35f, 0.95f);
            _promptToastText.text = string.Empty;

            _debugText = CreateText("DebugText", _root.transform, 14, FontStyle.Normal, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-340f, -120f), new Vector2(-20f, -20f));
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
            bool visible = _localAvatar != null && _roundState != null;
            SetHudVisible(visible);
            if (!visible)
            {
                return;
            }

            float opacity01 = Mathf.Clamp01(_localAvatar.Opacity01);
            float stability01 = _localStamina != null ? _localStamina.Normalized : (_localMover != null ? _localMover.Stamina01 : 1f);

            UpdateRoundFeed();

            _opacityFill.fillAmount = opacity01;
            _stabilityFill.fillAmount = stability01;
            _opacityFill.color = Color.Lerp(new Color(1f, 0.18f, 0.18f, 0.95f), new Color(0.50f, 0.95f, 0.42f, 0.95f), opacity01);
            _stabilityFill.color = Color.Lerp(new Color(1f, 0.42f, 0.15f, 0.95f), new Color(0.24f, 0.75f, 1f, 0.95f), stability01);

            _playerNameText.text = string.IsNullOrWhiteSpace(_localAvatar.PlayerLabel) ? "PLAYER" : _localAvatar.PlayerLabel.ToUpperInvariant();
            _playerRoleText.text = BuildRoleLine(opacity01, stability01);
            _timerText.text = FormatTime(_roundState.RoundTimeRemaining);
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
            string role = _killController != null ? _killController.CurrentRole.ToString() : "Color";
            string flood = _localFlood != null ? _localFlood.CurrentZoneState.ToString() : "Dry";
            string gravity = _localMover != null ? BuildGravityLabel(_localMover.CurrentGravityMultiplier) : "1.00g";
            string ability = _killController != null ? _killController.BuildAbilityStatusLine() : "Color kit: Report bodies / repair / survive";
            return $"Role: {role}   Opacity {Mathf.RoundToInt(opacity01 * 100f)}%   Stability {Mathf.RoundToInt(stability01 * 100f)}%   Flood: {flood}   Gravity: {gravity}\n{ability}";
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
            sb.AppendLine(role == PlayerRole.Bleach ? "Quick Use: Bleach / Sabotage" : "Quick Use: Install / Report / Burst");
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
            return "INTERACTABLES\n- Press E on task terminals\n- Pick up required parts from dispensers\n- Install parts at matching receivers\n- Use decontamination scrubbers when saturation/stability gets dangerous\n- Use emergency seal stations to suppress bleach leaks\n- Use floodgate stations to vent wet or submerged routes\n- Use safe room beacons during critical saturation or low stability\n- Use paint scanners to find bleach residue, evidence shards, and sabotage consoles\n- Use vitals monitors to check bodies and critical saturation\n- Run security camera sweeps to locate bleach motion and open vent routes\n- Arm paint alarm tripwires across suspected killer paths\n- Reconstitute at ink wells when unstable or washed out\n- Seal bleach vents when you suspect fast killer traversal\n- Inspect fresh evidence before Bleach smears it\n- Use the emergency meeting console when suspicion is high\n- During meetings, use accusation pods near suspects or the skip pod if evidence is weak\n- Rally beacons recover nearby crew and create public group moments\n- Bulkhead locks seal dangerous flood routes; Bleach can jam them\n- Callout beacons broadcast suspicion/location pings; Bleach can fake them\n- Slime launch pads are fast traversal tools for risky routes\n- Bleach can use sabotage consoles, vents, fake callouts, and false residue smear kits\n- Follow valve / stabilizer / signal order to avoid lockouts" +
                   (string.IsNullOrWhiteSpace(nearestHint) ? string.Empty : "\n\n" + nearestHint);
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

            GameObject fillObject = new GameObject(name + "_Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(root.transform, false);

            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            Image fill = fillObject.GetComponent<Image>();
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
