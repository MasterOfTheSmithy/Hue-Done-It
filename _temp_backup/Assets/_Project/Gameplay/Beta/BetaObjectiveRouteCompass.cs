// File: Assets/_Project/Gameplay/Beta/BetaObjectiveRouteCompass.cs
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Small fallback navigator that points testers toward the next useful objective, rather than leaving them in a
    /// generated map with no readable route intent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaObjectiveRouteCompass : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F4;
        [SerializeField, Min(0.2f)] private float refreshIntervalSeconds = 0.35f;
        [SerializeField, Min(0.5f)] private float nearObjectiveDistance = 3.25f;

        private GUIStyle _panelStyle;
        private GUIStyle _largeStyle;
        private GUIStyle _smallStyle;
        private Texture2D _panelTexture;
        private float _nextRefreshTime;

        private Transform _localPlayer;
        private NetworkRoundState _roundState;
        private NetworkRepairTask _targetTask;
        private FloodSequenceController _floodController;
        private string _targetLabel = "Finding objective...";
        private float _targetDistance;
        private Vector3 _targetPosition;

        private void Update()
        {
            if (BetaInputBridge.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }

            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            ResolveState();
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            EnsureStyles();
            Rect rect = new Rect(Screen.width * 0.5f - 260f, 18f, 520f, 92f);
            GUI.Box(rect, GUIContent.none, _panelStyle);

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 8f, rect.width - 28f, rect.height - 16f));
            GUILayout.Label(BuildHeader(), _largeStyle);
            GUILayout.Label(BuildDetail(), _smallStyle);
            GUILayout.EndArea();
        }

        private void ResolveState()
        {
            _roundState = _roundState != null ? _roundState : FindObjectOfType<NetworkRoundState>();
            _floodController = _floodController != null ? _floodController : FindObjectOfType<FloodSequenceController>();
            _localPlayer = ResolveLocalPlayer();

            if (_localPlayer == null)
            {
                _targetLabel = "Waiting for local player";
                _targetDistance = 0f;
                _targetTask = null;
                return;
            }

            _targetTask = FindBestTask(_localPlayer.position);
            if (_targetTask != null)
            {
                _targetPosition = _targetTask.transform.position;
                _targetDistance = Vector3.Distance(_localPlayer.position, _targetPosition);
                _targetLabel = _targetTask.DisplayName;
                return;
            }

            _targetPosition = Vector3.zero;
            _targetDistance = Vector3.Distance(_localPlayer.position, _targetPosition);
            _targetLabel = "Return to central hub / meeting route";
        }

        private Transform ResolveLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
            {
                return manager.LocalClient.PlayerObject.transform;
            }

            Camera mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }

        private NetworkRepairTask FindBestTask(Vector3 origin)
        {
            NetworkRepairTask[] tasks = FindObjectsOfType<NetworkRepairTask>();
            NetworkRepairTask best = null;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null || task.IsCompleted || task.CurrentState == RepairTaskState.Locked)
                {
                    continue;
                }

                if (!task.IsTaskEnvironmentSafe())
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, task.transform.position);
                float statePenalty = task.CurrentState == RepairTaskState.InProgress ? 10f : 0f;
                float score = distance + statePenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = task;
                }
            }

            return best;
        }

        private string BuildHeader()
        {
            if (_localPlayer == null)
            {
                return "OBJECTIVE COMPASS // WAITING FOR PLAYER";
            }

            string arrow = BuildArrow();
            return $"{arrow}  {_targetLabel}  ({Mathf.RoundToInt(_targetDistance)}m)";
        }

        private string BuildDetail()
        {
            string round = _roundState != null ? _roundState.CurrentObjective : "Round state not bound";
            string flood = _floodController != null ? _floodController.BuildRoundPressureHint() : "Flood telemetry offline";
            string interaction = _targetDistance <= nearObjectiveDistance ? "IN RANGE: Press E / leave radius resets most task progress." : "Follow arrow. Use landmarks and avoid flashing flood lanes.";
            return round + "\n" + flood + " // " + interaction + " // F4 toggle";
        }

        private string BuildArrow()
        {
            if (_localPlayer == null)
            {
                return "?";
            }

            Vector3 flatDirection = _targetPosition - _localPlayer.position;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude <= 0.25f)
            {
                return "HERE";
            }

            Transform reference = Camera.main != null ? Camera.main.transform : _localPlayer;
            Vector3 forward = reference.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = reference.right;
            right.y = 0f;
            right.Normalize();
            flatDirection.Normalize();

            float forwardDot = Vector3.Dot(forward, flatDirection);
            float rightDot = Vector3.Dot(right, flatDirection);

            if (forwardDot > 0.72f)
            {
                return "FWD";
            }

            if (forwardDot < -0.72f)
            {
                return "BACK";
            }

            return rightDot >= 0f ? "RIGHT" : "LEFT";
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _panelTexture.SetPixel(0, 0, new Color(0.01f, 0.012f, 0.018f, 0.78f));
            _panelTexture.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _panelTexture;

            _largeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.78f, 1f, 0.94f, 1f) }
            };

            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
        }

        private void OnDestroy()
        {
            if (_panelTexture != null)
            {
                Destroy(_panelTexture);
            }
        }
    }
}
