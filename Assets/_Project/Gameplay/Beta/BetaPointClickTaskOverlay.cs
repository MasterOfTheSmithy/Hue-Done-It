// File: Assets/_Project/Gameplay/Beta/BetaPointClickTaskOverlay.cs
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(1200)]
    public sealed class BetaPointClickTaskOverlay : MonoBehaviour
    {
        private enum MiniGameMode
        {
            SortNodes,
            DragPatch,
            ConnectCircuit
        }

        [SerializeField] private bool showOverlay = true;
        [SerializeField, Min(1)] private int requiredClicks = 4;
        [SerializeField] private Vector2 panelSize = new(420f, 286f);

        private PlayerRepairTaskParticipant _localParticipant;
        private NetworkRepairTask _currentTask;
        private int _taskHash;
        private int _clicks;
        private int _selectedConnector = -1;
        private float _dragProgress;
        private bool _draggingPatch;
        private bool _completedFromUi;
        private bool _cursorCaptured;
        private bool _previousCursorVisible;
        private CursorLockMode _previousCursorLockMode;
        private MiniGameMode _mode;
        private string _status = string.Empty;
        private readonly Rect[] _targets = new Rect[6];

        private void Update()
        {
            ResolveLocalParticipant();

            NetworkRepairTask activeTask = _localParticipant != null ? _localParticipant.ActiveTask : null;
            if (activeTask != _currentTask)
            {
                _currentTask = activeTask;
                ResetPuzzle(activeTask);
            }

            if (IsOverlayActive())
            {
                EnsureCursorForTask();
            }
            else
            {
                RestoreCursorIfNeeded();
            }
        }

        private void OnDisable()
        {
            RestoreCursorIfNeeded();
        }

        private void OnGUI()
        {
            if (!IsOverlayActive())
            {
                return;
            }

            Rect panel = new Rect(24f, Mathf.Max(18f, Screen.height - panelSize.y - 24f), panelSize.x, panelSize.y);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, panel.height - 20f));
            GUILayout.Label(BuildTitle());
            GUILayout.Label(_currentTask.DisplayName);
            GUILayout.Label(BuildInstruction());
            if (!string.IsNullOrWhiteSpace(_status))
            {
                GUILayout.Label(_status);
            }
            GUILayout.EndArea();

            switch (_mode)
            {
                case MiniGameMode.DragPatch:
                    DrawDragPatch(panel);
                    break;
                case MiniGameMode.ConnectCircuit:
                    DrawConnectCircuit(panel);
                    break;
                default:
                    DrawSortNodes(panel);
                    break;
            }

            if (GUI.Button(new Rect(panel.x + panel.width - 96f, panel.y + panel.height - 40f, 78f, 28f), "Cancel"))
            {
                CancelTask("Task UI cancelled");
            }
        }

        private bool IsOverlayActive()
        {
            return showOverlay &&
                   _localParticipant != null &&
                   _currentTask != null &&
                   _localParticipant.HasActiveTask &&
                   _localParticipant.IsWithinActiveTaskRange &&
                   _currentTask.CurrentState == RepairTaskState.InProgress;
        }

        private void DrawSortNodes(Rect panel)
        {
            Event evt = Event.current;
            for (int i = 0; i < requiredClicks && i < _targets.Length; i++)
            {
                Rect target = OffsetRect(_targets[i], panel.position);
                bool done = i < _clicks;
                Color previous = GUI.color;
                GUI.color = done ? new Color(0.25f, 1f, 0.45f, 0.85f) : new Color(1f, 0.85f, 0.2f, 0.95f);
                GUI.Box(target, done ? "OK" : (i + 1).ToString());
                GUI.color = previous;

                if (!done && evt.type == EventType.MouseDown && evt.button == 0 && target.Contains(evt.mousePosition))
                {
                    if (i == _clicks)
                    {
                        _clicks++;
                        _status = $"Sorted {_clicks}/{requiredClicks}";
                    }
                    else
                    {
                        _status = "Wrong node. Follow the numbered pulse.";
                    }

                    evt.Use();

                    if (_clicks >= requiredClicks)
                    {
                        CompleteTask();
                    }

                    break;
                }
            }
        }

        private void DrawDragPatch(Rect panel)
        {
            Event evt = Event.current;
            Rect source = new Rect(panel.x + 42f, panel.y + 126f, 86f, 58f);
            Rect channel = new Rect(panel.x + 126f, panel.y + 146f, panel.width - 246f, 18f);
            Rect target = new Rect(panel.x + panel.width - 124f, panel.y + 124f, 82f, 62f);
            float patchX = Mathf.Lerp(source.x, target.x, _dragProgress);
            Rect patch = new Rect(patchX, source.y + 6f, 68f, 46f);

            GUI.Box(source, "GOO");
            GUI.Box(channel, string.Empty);
            GUI.Box(target, "LEAK");
            GUI.Box(patch, _draggingPatch ? "DRAG" : "PATCH");

            if (evt.type == EventType.MouseDown && evt.button == 0 && patch.Contains(evt.mousePosition))
            {
                _draggingPatch = true;
                _status = "Drag the patch into the leak.";
                evt.Use();
            }

            if (_draggingPatch && (evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove))
            {
                _dragProgress = Mathf.Clamp01(Mathf.InverseLerp(source.x, target.x, evt.mousePosition.x));
                evt.Use();
            }

            if (_draggingPatch && evt.type == EventType.MouseUp && evt.button == 0)
            {
                _draggingPatch = false;
                _dragProgress = Mathf.Clamp01(Mathf.InverseLerp(source.x, target.x, evt.mousePosition.x));
                if (target.Contains(evt.mousePosition) || _dragProgress > 0.88f)
                {
                    _status = "Patch seated.";
                    CompleteTask();
                }
                else
                {
                    _dragProgress = 0f;
                    _status = "Patch slipped. Try again.";
                }

                evt.Use();
            }
        }

        private void DrawConnectCircuit(Rect panel)
        {
            Event evt = Event.current;
            Color previous = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                bool connected = i < _clicks;
                Rect left = new Rect(panel.x + 58f, panel.y + 108f + (i * 44f), 86f, 30f);
                Rect right = new Rect(panel.x + panel.width - 144f, panel.y + 108f + (i * 44f), 86f, 30f);
                GUI.color = connected ? new Color(0.25f, 1f, 0.45f, 0.9f) : (_selectedConnector == i ? new Color(1f, 0.84f, 0.16f, 1f) : Color.white);
                GUI.Box(left, connected ? "OK" : $"OUT {i + 1}");
                GUI.Box(right, connected ? "OK" : $"IN {i + 1}");

                if (!connected && evt.type == EventType.MouseDown && evt.button == 0)
                {
                    if (left.Contains(evt.mousePosition))
                    {
                        _selectedConnector = i;
                        _status = $"Route output {i + 1} to input {i + 1}.";
                        evt.Use();
                    }
                    else if (right.Contains(evt.mousePosition))
                    {
                        if (_selectedConnector == i)
                        {
                            _clicks++;
                            _selectedConnector = -1;
                            _status = $"Circuit {_clicks}/3 connected.";
                            evt.Use();
                            if (_clicks >= 3)
                            {
                                CompleteTask();
                            }
                        }
                        else
                        {
                            _selectedConnector = -1;
                            _status = "Mismatched input. Start the pair again.";
                            evt.Use();
                        }
                    }
                }
            }

            GUI.color = previous;
        }

        private void ResolveLocalParticipant()
        {
            if (_localParticipant != null && _localParticipant.IsSpawned && _localParticipant.IsOwner)
            {
                return;
            }

            _localParticipant = null;
            PlayerRepairTaskParticipant[] participants = FindObjectsByType<PlayerRepairTaskParticipant>(FindObjectsSortMode.None);
            for (int i = 0; i < participants.Length; i++)
            {
                PlayerRepairTaskParticipant participant = participants[i];
                if (participant != null && participant.IsSpawned && participant.IsOwner)
                {
                    _localParticipant = participant;
                    return;
                }
            }
        }

        private void ResetPuzzle(NetworkRepairTask activeTask)
        {
            _clicks = 0;
            _selectedConnector = -1;
            _dragProgress = 0f;
            _draggingPatch = false;
            _completedFromUi = false;
            _status = string.Empty;
            _taskHash = activeTask != null ? activeTask.GetInstanceID() : 0;
            _mode = ResolveMode(activeTask);
            RebuildTargets();
        }

        private void RebuildTargets()
        {
            float seed = Mathf.Abs(_taskHash * 0.01731f);
            for (int i = 0; i < _targets.Length; i++)
            {
                float x = 34f + Mathf.Repeat(Mathf.Sin(seed + i * 3.17f) * 1000f, panelSize.x - 100f);
                float y = 88f + Mathf.Repeat(Mathf.Cos(seed + i * 5.91f) * 1000f, panelSize.y - 135f);
                _targets[i] = new Rect(x, y, 58f, 30f);
            }
        }

        private static Rect OffsetRect(Rect rect, Vector2 offset)
        {
            return new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);
        }

        private MiniGameMode ResolveMode(NetworkRepairTask task)
        {
            if (task == null)
            {
                return MiniGameMode.SortNodes;
            }

            string name = task.DisplayName == null ? string.Empty : task.DisplayName.ToLowerInvariant();
            if (name.Contains("patch") || name.Contains("leak") || name.Contains("hull") || name.Contains("seal"))
            {
                return MiniGameMode.DragPatch;
            }

            if (name.Contains("circuit") || name.Contains("relay") || name.Contains("power") || name.Contains("fuse"))
            {
                return MiniGameMode.ConnectCircuit;
            }

            return MiniGameMode.SortNodes;
        }

        private string BuildTitle()
        {
            return _mode switch
            {
                MiniGameMode.DragPatch => "PATCH WINDOW",
                MiniGameMode.ConnectCircuit => "CONNECT WINDOW",
                _ => "SORT WINDOW"
            };
        }

        private string BuildInstruction()
        {
            return _mode switch
            {
                MiniGameMode.DragPatch => "Drag patch goo from the supply pad into the leak.",
                MiniGameMode.ConnectCircuit => $"Connect matching outputs: {_clicks}/3.",
                _ => $"Click unstable nodes in order: {_clicks}/{requiredClicks}."
            };
        }

        private void EnsureCursorForTask()
        {
            if (!_cursorCaptured)
            {
                _previousCursorLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                _cursorCaptured = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreCursorIfNeeded()
        {
            if (!_cursorCaptured)
            {
                return;
            }

            Cursor.lockState = _previousCursorLockMode;
            Cursor.visible = _previousCursorVisible;
            _cursorCaptured = false;
        }

        private void CompleteTask()
        {
            if (_completedFromUi || _localParticipant == null)
            {
                return;
            }

            _completedFromUi = true;
            _localParticipant.RequestCompleteActiveTaskFromLocalUi();
        }

        private void CancelTask(string reason)
        {
            if (_localParticipant == null)
            {
                return;
            }

            _localParticipant.RequestCancelActiveTaskFromLocalUi(reason);
            RestoreCursorIfNeeded();
        }
    }
}
