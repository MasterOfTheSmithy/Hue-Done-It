// File: Assets/_Project/Gameplay/Beta/BetaContextualHudFeedbackOverlay.cs
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Small contextual HUD pulse layer that complements GameplayInvestorHud without replacing it.
    /// It surfaces moment-to-moment beta feedback: flood warnings, objective changes, task results,
    /// low gravity, death/spectator transition, and low ship timer.
    /// </summary>
    [DefaultExecutionOrder(930)]
    [DisallowMultipleComponent]
    public sealed class BetaContextualHudFeedbackOverlay : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float scanIntervalSeconds = 0.25f;
        [SerializeField, Min(0.5f)] private float messageLifetimeSeconds = 2.4f;
        [SerializeField, Range(0.5f, 1.5f)] private float scale = 1f;

        private readonly Queue<FeedMessage> _messages = new();
        private readonly Dictionary<NetworkRepairTask, RepairTaskState> _repairStates = new();
        private readonly Dictionary<TaskObjectiveBase, RepairTaskState> _objectiveStates = new();

        private GUIStyle _titleStyle;
        private GUIStyle _messageStyle;
        private Texture2D _panelTexture;

        private NetworkRoundState _roundState;
        private FloodSequenceController _floodController;
        private PlayerLifeState _localLife;
        private NetworkPlayerAuthoritativeMover _localMover;

        private string _lastObjective = string.Empty;
        private RoundPhase _lastPhase;
        private NetworkRoundState.PressureStage _lastPressure;
        private bool _initializedRound;
        private bool _wasFloodTelegraph;
        private bool _wasFloodPulse;
        private bool _wasAlive = true;
        private bool _wasLowGravity;
        private float _nextScanTime;
        private float _lastLowTimeMessage;

        private struct FeedMessage
        {
            public string Title;
            public string Detail;
            public Color Color;
            public float EndTime;
        }

        private void Awake()
        {
            _panelTexture = new Texture2D(1, 1);
            _panelTexture.SetPixel(0, 0, new Color(0.02f, 0.02f, 0.04f, 0.78f));
            _panelTexture.Apply();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + scanIntervalSeconds;
            ResolveReferences();
            ScanRound();
            ScanFlood();
            ScanLocalPlayer();
            ScanTasks();
        }

        private void OnGUI()
        {
            BuildStylesIfNeeded();

            while (_messages.Count > 0 && Time.unscaledTime > _messages.Peek().EndTime)
            {
                _messages.Dequeue();
            }

            if (_messages.Count == 0)
            {
                return;
            }

            float width = 410f * scale;
            float height = Mathf.Min(170f * scale, 44f * _messages.Count * scale + 22f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.18f, width, height);
            GUI.DrawTexture(panel, _panelTexture);

            float y = panel.y + 12f;
            foreach (FeedMessage message in _messages)
            {
                GUI.color = message.Color;
                GUI.Label(new Rect(panel.x + 18f, y, width - 36f, 24f * scale), message.Title, _titleStyle);
                GUI.color = Color.white;
                GUI.Label(new Rect(panel.x + 18f, y + 21f * scale, width - 36f, 24f * scale), message.Detail, _messageStyle);
                y += 44f * scale;
            }

            GUI.color = Color.white;
        }

        private void BuildStylesIfNeeded()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(18f * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };

            _messageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(13f * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
        }

        private void ResolveReferences()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }

            if (_floodController == null)
            {
                _floodController = FindFirstObjectByType<FloodSequenceController>();
            }

            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject playerObject = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (playerObject != null)
            {
                _localLife = playerObject.GetComponent<PlayerLifeState>();
                _localMover = playerObject.GetComponent<NetworkPlayerAuthoritativeMover>();
            }
        }

        private void ScanRound()
        {
            if (_roundState == null)
            {
                return;
            }

            if (!_initializedRound)
            {
                _initializedRound = true;
                _lastPhase = _roundState.CurrentPhase;
                _lastPressure = _roundState.CurrentPressureStage;
                _lastObjective = _roundState.CurrentObjective;
                return;
            }

            if (_roundState.CurrentPhase != _lastPhase)
            {
                _lastPhase = _roundState.CurrentPhase;
                Push("ROUND STATE", _roundState.CurrentPhase.ToString(), new Color(0.22f, 0.82f, 1f, 1f));
            }

            if (_roundState.CurrentPressureStage != _lastPressure)
            {
                _lastPressure = _roundState.CurrentPressureStage;
                Push("SHIP PRESSURE", _roundState.CurrentPressureStage.ToString(), new Color(1f, 0.62f, 0.12f, 1f));
            }

            string objective = _roundState.CurrentObjective;
            if (!string.IsNullOrWhiteSpace(objective) && objective != _lastObjective)
            {
                _lastObjective = objective;
                Push("NEW OBJECTIVE", objective, new Color(0.72f, 1f, 0.14f, 1f));
            }

            if (_roundState.RoundTimeRemaining > 0f && _roundState.RoundTimeRemaining <= 30f && Time.unscaledTime - _lastLowTimeMessage > 8f)
            {
                _lastLowTimeMessage = Time.unscaledTime;
                Push("SHIP CRITICAL", Mathf.CeilToInt(_roundState.RoundTimeRemaining) + " seconds until catastrophic failure", new Color(1f, 0.18f, 0.16f, 1f));
            }
        }

        private void ScanFlood()
        {
            if (_floodController == null)
            {
                return;
            }

            bool telegraph = _floodController.IsPulseTelegraphActive;
            bool pulse = _floodController.IsPulseActive;
            if (telegraph && !_wasFloodTelegraph)
            {
                Push("FLOOD WARNING", "Seal rooms or move to higher ground", new Color(1f, 0.82f, 0.12f, 1f));
            }

            if (pulse && !_wasFloodPulse)
            {
                Push("FLOOD SURGE", "Water pressure is active", new Color(0.22f, 0.65f, 1f, 1f));
            }

            _wasFloodTelegraph = telegraph;
            _wasFloodPulse = pulse;
        }

        private void ScanLocalPlayer()
        {
            if (_localLife != null)
            {
                bool alive = _localLife.IsAlive;
                if (_wasAlive && !alive)
                {
                    Push("YOU DIFFUSED", "Spectate the crew or free-fly", new Color(0.92f, 0.32f, 1f, 1f));
                }
                _wasAlive = alive;
            }

            if (_localMover != null)
            {
                bool lowGravity = _localMover.IsInAlteredGravity;
                if (lowGravity && !_wasLowGravity)
                {
                    Push("LOW GRAVITY", "Use walls and ceiling routes", new Color(0.45f, 0.92f, 1f, 1f));
                }
                _wasLowGravity = lowGravity;
            }
        }

        private void ScanTasks()
        {
            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task == null)
                {
                    continue;
                }

                RepairTaskState state = task.CurrentState;
                if (!_repairStates.TryGetValue(task, out RepairTaskState previous))
                {
                    _repairStates[task] = state;
                    continue;
                }

                if (previous != state)
                {
                    _repairStates[task] = state;
                    PushTaskMessage(task.DisplayName, state);
                }
            }

            TaskObjectiveBase[] objectiveTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < objectiveTasks.Length; i++)
            {
                TaskObjectiveBase task = objectiveTasks[i];
                if (task == null)
                {
                    continue;
                }

                RepairTaskState state = task.CurrentState;
                if (!_objectiveStates.TryGetValue(task, out RepairTaskState previous))
                {
                    _objectiveStates[task] = state;
                    continue;
                }

                if (previous != state)
                {
                    _objectiveStates[task] = state;
                    PushTaskMessage(task.DisplayName, state);
                }
            }
        }

        private void PushTaskMessage(string displayName, RepairTaskState state)
        {
            switch (state)
            {
                case RepairTaskState.InProgress:
                    Push("TASK STARTED", displayName, new Color(0.22f, 0.82f, 1f, 1f));
                    break;
                case RepairTaskState.Completed:
                    Push("TASK COMPLETE", displayName, new Color(0.32f, 1f, 0.26f, 1f));
                    break;
                case RepairTaskState.Cancelled:
                case RepairTaskState.FailedAttempt:
                    Push("TASK RESET", displayName, new Color(1f, 0.32f, 0.18f, 1f));
                    break;
                case RepairTaskState.Locked:
                    Push("TASK LOCKED", displayName, new Color(1f, 0.18f, 0.18f, 1f));
                    break;
            }
        }

        private void Push(string title, string detail, Color color)
        {
            while (_messages.Count >= 3)
            {
                _messages.Dequeue();
            }

            _messages.Enqueue(new FeedMessage
            {
                Title = title,
                Detail = detail,
                Color = color,
                EndTime = Time.unscaledTime + messageLifetimeSeconds
            });
        }
    }
}
