// File: Assets/_Project/UI/Gameplay/BetaGameplayEmergencyOverlay.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Inventory;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Roles;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Gameplay
{
    // Last-resort HUD that uses IMGUI so beta testers are never blind if the authored/UGUI HUD fails to bind.
    public sealed class BetaGameplayEmergencyOverlay : MonoBehaviour
    {
        private const string GameplaySceneName = "Gameplay_Undertint";

        [SerializeField] private bool visible = true;
        [SerializeField] private Rect panelRect = new Rect(16f, 108f, 560f, 220f);

        private NetworkRoundState _roundState;
        private NetworkPlayerAvatar _localAvatar;
        private PlayerLifeState _localLife;
        private PlayerKillInputController _localRole;
        private PlayerInventoryState _localInventory;
        private PlayerStaminaState _localStamina;
        private PlayerFloodZoneTracker _localFlood;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
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
            if (!scene.IsValid() || scene.name != GameplaySceneName)
            {
                return;
            }

            if (FindFirstObjectByType<BetaGameplayEmergencyOverlay>() != null)
            {
                return;
            }

            GameObject overlayObject = new GameObject(nameof(BetaGameplayEmergencyOverlay));
            overlayObject.AddComponent<BetaGameplayEmergencyOverlay>();
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name != GameplaySceneName)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
            }

            BindIfNeeded();
        }

        private void OnGUI()
        {
            if (!visible || SceneManager.GetActiveScene().name != GameplaySceneName)
            {
                return;
            }

            BindIfNeeded();

            string phase = _roundState != null ? _roundState.CurrentPhase.ToString() : "NO ROUND STATE";
            string time = _roundState != null ? FormatTime(_roundState.RoundTimeRemaining) : "--:--";
            string objective = _roundState != null && !string.IsNullOrWhiteSpace(_roundState.CurrentObjective)
                ? _roundState.CurrentObjective
                : "Restore ship systems, avoid flood, report bodies, identify Bleach.";
            string role = _localRole != null ? _localRole.CurrentRole.ToString() : PlayerRole.Color.ToString();
            string life = _localLife != null ? _localLife.CurrentLifeState.ToString() : "Binding";
            int stability = _localStamina != null ? Mathf.RoundToInt(_localStamina.Normalized * 100f) : 100;
            int diffusion = _localFlood != null ? Mathf.RoundToInt(_localFlood.Saturation01 * 100f) : 0;
            string player = _localAvatar != null && !string.IsNullOrWhiteSpace(_localAvatar.PlayerLabel)
                ? _localAvatar.PlayerLabel
                : "Local Player";
            string inventory = _localInventory != null ? _localInventory.BuildInventorySummary() : "Inventory unavailable";
            string spectator = _localLife != null && !_localLife.IsAlive
                ? "SPECTATOR: Tab / [ / ] cycle players, F free camera, WASD fly."
                : "Controls: WASD move, Mouse look, Space jump, E interact, R report, Esc pause.";

            GUI.Box(panelRect,
                $"BETA HUD FALLBACK  [F1 hide/show]\n" +
                $"{player} // {life} // Role: {role}\n" +
                $"Round: {phase} // Ship explodes in: {time}\n" +
                $"Stability: {stability}% // Diffusion: {diffusion}%\n" +
                $"Objective: {objective}\n" +
                $"{inventory}\n" +
                spectator);
        }

        private void BindIfNeeded()
        {
            if (_roundState == null)
            {
                _roundState = FindFirstObjectByType<NetworkRoundState>();
            }

            if (_localAvatar != null && _localAvatar.IsSpawned)
            {
                return;
            }

            _localAvatar = null;
            _localLife = null;
            _localRole = null;
            _localInventory = null;
            _localStamina = null;
            _localFlood = null;

            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (avatar == null || !avatar.IsSpawned || !avatar.IsOwner || avatar.TryGetComponent(out SimpleCpuOpponentAgent _))
                {
                    continue;
                }

                _localAvatar = avatar;
                _localLife = avatar.GetComponent<PlayerLifeState>();
                _localRole = avatar.GetComponent<PlayerKillInputController>();
                _localInventory = avatar.GetComponent<PlayerInventoryState>();
                _localStamina = avatar.GetComponent<PlayerStaminaState>();
                _localFlood = avatar.GetComponent<PlayerFloodZoneTracker>();
                break;
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
