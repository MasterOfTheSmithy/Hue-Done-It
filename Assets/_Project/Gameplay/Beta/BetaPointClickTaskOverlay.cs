// File: Assets/_Project/Gameplay/Beta/BetaPointClickTaskOverlay.cs
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(1200)]
    public sealed class BetaPointClickTaskOverlay : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = true;
        [SerializeField, Min(1)] private int requiredClicks = 4;
        [SerializeField] private Vector2 panelSize = new(360f, 240f);

        private PlayerRepairTaskParticipant _localParticipant;
        private NetworkRepairTask _currentTask;
        private int _taskHash;
        private int _clicks;
        private readonly Rect[] _targets = new Rect[6];

        private void Update()
        {
            ResolveLocalParticipant();

            NetworkRepairTask activeTask = _localParticipant != null ? _localParticipant.ActiveTask : null;
            if (activeTask != _currentTask)
            {
                _currentTask = activeTask;
                _clicks = 0;
                _taskHash = activeTask != null ? activeTask.GetInstanceID() : 0;
                RebuildTargets();
            }
        }

        private void OnGUI()
        {
            if (!showOverlay || _localParticipant == null || _currentTask == null)
            {
                return;
            }

            if (!_localParticipant.HasActiveTask || _currentTask.CurrentState != RepairTaskState.InProgress)
            {
                return;
            }

            Rect panel = new Rect(24f, Screen.height - panelSize.y - 24f, panelSize.x, panelSize.y);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, panel.height - 20f));
            GUILayout.Label("POINT SORT MINIGAME");
            GUILayout.Label(_currentTask.DisplayName);
            GUILayout.Label($"Click unstable nodes: {_clicks}/{requiredClicks}");
            GUILayout.Label("Leaving the task radius or pressing cancel drops active progress.");
            GUILayout.EndArea();

            Event evt = Event.current;
            for (int i = 0; i < requiredClicks && i < _targets.Length; i++)
            {
                Rect target = OffsetRect(_targets[i], panel.position);
                bool done = i < _clicks;
                Color previous = GUI.color;
                GUI.color = done ? new Color(0.25f, 1f, 0.45f, 0.85f) : new Color(1f, 0.85f, 0.2f, 0.95f);
                GUI.Box(target, done ? "OK" : "SORT");
                GUI.color = previous;

                if (!done && evt.type == EventType.MouseDown && evt.button == 0 && target.Contains(evt.mousePosition))
                {
                    _clicks++;
                    evt.Use();

                    if (_clicks >= requiredClicks)
                    {
                        _localParticipant.RequestCompleteActiveTaskFromLocalUi();
                    }

                    break;
                }
            }

            if (GUI.Button(new Rect(panel.x + panel.width - 92f, panel.y + panel.height - 38f, 74f, 26f), "Cancel"))
            {
                _localParticipant.RequestCancelActiveTaskFromLocalUi("Task UI cancelled");
            }
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
    }
}
