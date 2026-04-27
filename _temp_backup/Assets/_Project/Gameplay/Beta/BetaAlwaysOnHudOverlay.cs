// File: Assets/_Project/Gameplay/Beta/BetaAlwaysOnHudOverlay.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Inventory;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Hard fallback gameplay HUD. This keeps critical beta information visible even when the authored UGUI HUD
    /// is not bound yet, has been disabled, or is missing from the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaAlwaysOnHudOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F2;
        [SerializeField, Range(0.45f, 1.25f)] private float scale = 0.82f;

        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private Texture2D _panelTexture;
        private float _nextResolveTime;

        private NetworkRoundState _roundState;
        private NetworkObject _localPlayer;
        private PlayerStaminaState _staminaState;
        private PlayerFloodZoneTracker _floodTracker;
        private PlayerInventoryState _inventoryState;
        private PlayerRepairTaskParticipant _taskParticipant;
        private PlayerLifeState _lifeState;

        private void Update()
        {
            if (BetaInputBridge.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }

            if (Time.unscaledTime >= _nextResolveTime)
            {
                _nextResolveTime = Time.unscaledTime + 0.5f;
                ResolveReferences();
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            EnsureStyles();

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            Rect rect = new Rect(18f, 18f, 430f, 262f);
            GUI.Box(rect, GUIContent.none, _panelStyle);

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 10f, rect.width - 28f, rect.height - 20f));
            GUILayout.Label("HUE DONE IT // BETA HUD", _titleStyle);
            GUILayout.Space(4f);

            DrawBar("STABILITY", GetStability01(), "stamina/control");
            DrawBar("DIFFUSION", GetDiffusion01(), "water exposure");
            DrawLine("OBJECTIVE", GetObjectiveText());
            DrawLine("SHIP EXPLOSION", FormatTime(GetExplosionCountdown()));
            DrawLine("INVENTORY", GetInventoryText());
            DrawLine("TASK", GetTaskText());
            DrawLine("STATE", GetLifeStateText());

            GUILayout.Space(4f);
            GUILayout.Label("F2 toggle // Dead: Tab or ] next, [ previous, F freecam", _labelStyle);
            GUILayout.EndArea();

            GUI.matrix = oldMatrix;
        }

        private void ResolveReferences()
        {
            _roundState = _roundState != null ? _roundState : FindObjectOfType<NetworkRoundState>();

            NetworkManager manager = NetworkManager.Singleton;
            _localPlayer = null;
            if (manager != null && manager.LocalClient != null)
            {
                _localPlayer = manager.LocalClient.PlayerObject;
            }

            if (_localPlayer == null)
            {
                _staminaState = null;
                _floodTracker = null;
                _inventoryState = null;
                _taskParticipant = null;
                _lifeState = null;
                return;
            }

            _localPlayer.TryGetComponent(out _staminaState);
            _localPlayer.TryGetComponent(out _floodTracker);
            _localPlayer.TryGetComponent(out _inventoryState);
            _localPlayer.TryGetComponent(out _taskParticipant);
            _localPlayer.TryGetComponent(out _lifeState);
        }

        private void DrawLine(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _labelStyle, GUILayout.Width(126f));
            GUILayout.Label(string.IsNullOrWhiteSpace(value) ? "..." : value, _labelStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawBar(string label, float normalized, string hint)
        {
            normalized = Mathf.Clamp01(normalized);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _labelStyle, GUILayout.Width(126f));

            Rect barRect = GUILayoutUtility.GetRect(190f, 13f);
            GUI.Box(barRect, GUIContent.none);
            Rect fill = new Rect(barRect.x + 2f, barRect.y + 2f, Mathf.Max(0f, (barRect.width - 4f) * normalized), barRect.height - 4f);
            Color old = GUI.color;
            GUI.color = Color.Lerp(new Color(1f, 0.16f, 0.12f, 1f), new Color(0.15f, 1f, 0.75f, 1f), normalized);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = old;

            GUILayout.Label(Mathf.RoundToInt(normalized * 100f) + "% " + hint, _labelStyle);
            GUILayout.EndHorizontal();
        }

        private float GetStability01()
        {
            return _staminaState != null ? _staminaState.Normalized : 0f;
        }

        private float GetDiffusion01()
        {
            return _floodTracker != null ? _floodTracker.Saturation01 : 0f;
        }

        private string GetObjectiveText()
        {
            if (_roundState == null)
            {
                return "Waiting for round state";
            }

            string objective = _roundState.CurrentObjective;
            return string.IsNullOrWhiteSpace(objective) ? _roundState.RoundMessage : objective;
        }

        private float GetExplosionCountdown()
        {
            return _roundState != null ? _roundState.RoundTimeRemaining : 0f;
        }

        private string GetInventoryText()
        {
            return _inventoryState != null ? _inventoryState.BuildInventorySummary() : "Inventory not bound";
        }

        private string GetTaskText()
        {
            if (_taskParticipant == null)
            {
                return "No task participant";
            }

            if (!_taskParticipant.HasActiveTask)
            {
                return "No active task";
            }

            return _taskParticipant.IsWithinActiveTaskRange
                ? "Active, hold radius"
                : "Leaving radius - progress will reset";
        }

        private string GetLifeStateText()
        {
            if (_lifeState == null)
            {
                return "Life state not bound";
            }

            return _lifeState.CurrentLifeState + (_lifeState.IsAlive ? string.Empty : " // Spectating enabled");
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int whole = Mathf.CeilToInt(seconds);
            return (whole / 60).ToString("00") + ":" + (whole % 60).ToString("00");
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _panelTexture.SetPixel(0, 0, new Color(0.02f, 0.025f, 0.035f, 0.84f));
            _panelTexture.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _panelTexture;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.64f, 1f, 0.92f, 1f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
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
