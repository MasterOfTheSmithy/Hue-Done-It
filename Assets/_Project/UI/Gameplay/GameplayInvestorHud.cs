// File: Assets/_Project/UI/Gameplay/GameplayInvestorHud.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using HueDoneIt.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HueDoneIt.UI.Gameplay
{
    public sealed class GameplayInvestorHud : MonoBehaviour
    {
        private Text _objectiveText;
        private Text _roleText;
        private Text _floodText;
        private Text _abilityText;
        private Text _roundText;
        private Text _controlsText;
        private Text _statusText;
        private Image _crosshair;

        private PlayerKillInputController _localKillController;
        private PlayerFloodZoneTracker _localFloodTracker;
        private PlayerLifeState _localLifeState;
        private PumpRepairTask _pumpRepairTask;
        private NetworkRoundState _roundState;

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
            if (!scene.IsValid())
            {
                return;
            }

            if (Object.FindFirstObjectByType<GameplayInvestorHud>() != null)
            {
                return;
            }

            if (Object.FindFirstObjectByType<NetworkRoundState>() == null && Object.FindFirstObjectByType<PumpRepairTask>() == null)
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
        }

        private void Update()
        {
            BindIfNeeded();
            Refresh();
        }

        private void BuildHud()
        {
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject topLeft = CreatePanel("InvestorHud_TopLeft", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(460f, 220f));
            _objectiveText = CreateText(topLeft.transform, font, 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -14f), new Vector2(-14f, -86f));
            _roundText = CreateText(topLeft.transform, font, 15, TextAnchor.UpperLeft, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 78f), new Vector2(-14f, 14f));

            GameObject topRight = CreatePanel("InvestorHud_TopRight", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(460f, 220f));
            _roleText = CreateText(topRight.transform, font, 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -14f), new Vector2(-14f, -86f));
            _abilityText = CreateText(topRight.transform, font, 15, TextAnchor.UpperLeft, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 78f), new Vector2(-14f, 14f));

            GameObject bottomLeft = CreatePanel("InvestorHud_BottomLeft", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(460f, 150f));
            _floodText = CreateText(bottomLeft.transform, font, 16, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -14f), new Vector2(-14f, -14f));

            GameObject bottomRight = CreatePanel("InvestorHud_BottomRight", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(460f, 170f));
            _controlsText = CreateText(bottomRight.transform, font, 14, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -14f), new Vector2(-14f, -14f));
            _controlsText.text = "Controls\nWASD move\nMouse look\nSpace jump\nE interact / confirm pump\nF bleach inject\nQ bleach secondary\nEsc unlock cursor\nLeft click relock cursor";

            GameObject bottomCenter = CreatePanel("InvestorHud_BottomCenter", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(620f, 92f));
            _statusText = CreateText(bottomCenter.transform, font, 18, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 12f), new Vector2(-12f, -12f));

            _crosshair = CreateCrosshair();
        }

        private void BindIfNeeded()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }

            if (_pumpRepairTask == null)
            {
                _pumpRepairTask = FindFirstObjectByType<PumpRepairTask>();
            }

            if (_localKillController == null || _localFloodTracker == null || _localLifeState == null)
            {
                NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
                foreach (NetworkPlayerAvatar avatar in avatars)
                {
                    if (!avatar.IsOwner || !avatar.IsClient)
                    {
                        continue;
                    }

                    _localKillController = avatar.GetComponent<PlayerKillInputController>();
                    _localFloodTracker = avatar.GetComponent<PlayerFloodZoneTracker>();
                    _localLifeState = avatar.GetComponent<PlayerLifeState>();
                    break;
                }
            }
        }

        private void Refresh()
        {
            if (_objectiveText == null)
            {
                return;
            }

            if (_roundState == null)
            {
                _objectiveText.text = "Waiting for round state";
                _roundText.text = string.Empty;
                _roleText.text = string.Empty;
                _floodText.text = string.Empty;
                _abilityText.text = string.Empty;
                _statusText.text = string.Empty;
                SetCrosshairVisible(false);
                return;
            }

            _objectiveText.text = BuildObjectiveText();
            _roundText.text = BuildRoundText();
            _roleText.text = BuildRoleText();
            _abilityText.text = BuildAbilityText();
            _floodText.text = BuildFloodText();
            _statusText.text = BuildStatusText();
            SetCrosshairVisible(_roundState.CurrentPhase is RoundPhase.FreeRoam or RoundPhase.Reported);
        }

        private string BuildObjectiveText()
        {
            if (_localLifeState != null && !_localLifeState.IsAlive)
            {
                return $"Status\nYou are out of the round. Cause: {_localLifeState.LastStateReason}";
            }

            if (_roundState.CurrentPhase == RoundPhase.Intro)
            {
                return "Objective\nRound starting. Reach the pump, survive the flood, and identify bleach before the valve locks.";
            }

            if (_roundState.CurrentPhase == RoundPhase.Reported)
            {
                return "Objective\nBody reported. Action is paused briefly. Reassess and prepare to move when the report timer ends.";
            }

            if (_roundState.CurrentPhase == RoundPhase.Resolved || _roundState.CurrentPhase == RoundPhase.PostRound)
            {
                return $"Result\n{_roundState.RoundMessage}";
            }

            if (_pumpRepairTask == null)
            {
                return "Objective\nFind the pump and stabilize the flood release.";
            }

            if (_pumpRepairTask.IsCompleted)
            {
                return "Objective\nPump repaired. Flood release succeeded.";
            }

            if (_pumpRepairTask.IsLocked)
            {
                return "Objective\nPump locked. The flood cannot be released this round.";
            }

            return $"Objective\nRepair the pump. Attempts remaining: {_pumpRepairTask.AttemptsRemaining}. Press E again in the blue timing window while repairing.";
        }

        private string BuildRoundText()
        {
            string phase = _roundState.CurrentPhase.ToString();
            string phaseTimer = _roundState.PhaseTimeRemaining > 0f ? $"\nPhase Timer: {_roundState.PhaseTimeRemaining:0.0}s" : string.Empty;
            string roundTimer = _roundState.RoundTimeRemaining > 0f ? $"\nRound Timer: {_roundState.RoundTimeRemaining:0.0}s" : string.Empty;
            string message = string.IsNullOrWhiteSpace(_roundState.RoundMessage) ? string.Empty : $"\n{_roundState.RoundMessage}";
            return $"Round\nPhase: {phase}{phaseTimer}{roundTimer}{message}";
        }

        private string BuildRoleText()
        {
            if (_localKillController == null)
            {
                return "Role\nBinding local player";
            }

            string secondary = _localKillController.CurrentSecondaryAbility == BleachSecondaryAbility.None
                ? "None"
                : _localKillController.CurrentSecondaryAbility.ToString();

            string brief = _localKillController.CurrentRole switch
            {
                PlayerRole.Bleach => "Blend in, isolate targets, sabotage the pump, and let the flood win if needed.",
                PlayerRole.Color => "Reach the pump, survive the flood, and identify the bleach impostor.",
                _ => "Role assignment pending."
            };

            return $"Role\n{_localKillController.CurrentRole}\nSecondary: {secondary}\n{brief}";
        }

        private string BuildAbilityText()
        {
            if (_localKillController == null)
            {
                return "Abilities\nUnavailable";
            }

            if (_localKillController.CurrentRole != PlayerRole.Bleach)
            {
                return "Abilities\nColor role: repair the pump, report bodies, and avoid flood saturation.\nFlood-safe movement matters more than aggression.";
            }

            string primary = _localKillController.IsPrimaryWindupActive
                ? "Inject: winding up"
                : $"Inject: {_localKillController.GetPrimaryCooldownRemaining():0.0}s cooldown";

            string secondary = _localKillController.CurrentSecondaryAbility switch
            {
                BleachSecondaryAbility.Mimic => $"Mimic: {_localKillController.GetSecondaryCooldownRemaining():0.0}s cooldown",
                BleachSecondaryAbility.Corrupt => $"Corrupt: {_localKillController.GetSecondaryCooldownRemaining():0.0}s cooldown",
                BleachSecondaryAbility.Overload => _localKillController.HasUsedOverload
                    ? "Overload: spent"
                    : "Overload: ready",
                _ => "Secondary: none"
            };

            return $"Abilities\n{primary}\n{secondary}";
        }

        private string BuildFloodText()
        {
            if (_localFloodTracker == null)
            {
                return "Flood\nBinding local tracker";
            }

            string zoneName = _localFloodTracker.CurrentZone == null ? "Dry ground" : _localFloodTracker.CurrentZone.ZoneId;
            string warning = _localFloodTracker.IsCritical ? "\nStatus: CRITICAL" : string.Empty;
            return $"Flood\nZone: {zoneName}\nState: {_localFloodTracker.CurrentZoneState}\nSaturation: {_localFloodTracker.Saturation01 * 100f:0}%{warning}";
        }

        private string BuildStatusText()
        {
            if (_localLifeState != null && !_localLifeState.IsAlive)
            {
                return "Eliminated. Wait for the next round.";
            }

            return _roundState.CurrentPhase switch
            {
                RoundPhase.Intro => "Round intro. Roles are hidden. Get ready.",
                RoundPhase.Reported => "Body reported. Movement and actions are paused.",
                RoundPhase.Resolved => _roundState.Winner == RoundWinner.Color ? "Colors won the round." : "Bleach won the round.",
                RoundPhase.PostRound => "Resetting for the next round.",
                _ when _pumpRepairTask != null && _pumpRepairTask.IsLocked => "Pump locked. Flood pressure will keep rising.",
                _ when _pumpRepairTask != null && _pumpRepairTask.IsCompleted => "Pump secured. Survive until the round resolves.",
                _ => "Stay mobile. Watch the flood. Trust nobody."
            };
        }

        private GameObject CreatePanel(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMax.x, anchorMin.y == 0f ? 0f : 1f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;

            Image image = panel.GetComponent<Image>();
            image.color = new Color(0.04f, 0.05f, 0.08f, 0.84f);
            return panel;
        }

        private Text CreateText(Transform parent, Font font, int fontSize, TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject textObject = new("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            return text;
        }

        private Image CreateCrosshair()
        {
            GameObject crosshairObject = new("InvestorHud_Crosshair", typeof(RectTransform), typeof(Image));
            crosshairObject.transform.SetParent(transform, false);

            RectTransform rect = crosshairObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(8f, 8f);
            rect.anchoredPosition = new Vector2(0f, 0f);

            Image image = crosshairObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.95f);
            return image;
        }

        private void SetCrosshairVisible(bool visible)
        {
            if (_crosshair != null)
            {
                _crosshair.enabled = visible;
            }
        }
    }
}
