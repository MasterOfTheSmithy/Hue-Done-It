// File: Assets/_Project/UI/Gameplay/GameplayPauseMenu.cs
using HueDoneIt.Core.Bootstrap;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using HueDoneIt.Gameplay.Beta;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Gameplay
{
    // This pause menu is runtime-installed in Gameplay_Undertint.
    // It is intentionally lightweight and does not depend on authored prefabs.
    public sealed class GameplayPauseMenu : MonoBehaviour
    {
        private bool _isOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!BetaGameplaySceneCatalog.IsProductionGameplayScene(SceneManager.GetActiveScene().name))
            {
                return;
            }

            if (FindFirstObjectByType<GameplayPauseMenu>() != null)
            {
                return;
            }

            new GameObject(nameof(GameplayPauseMenu)).AddComponent<GameplayPauseMenu>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _isOpen = !_isOpen;
                ApplyCursorState();
            }
        }

        private void OnGUI()
        {
            if (!_isOpen || !BetaGameplaySceneCatalog.IsProductionGameplayScene(SceneManager.GetActiveScene().name))
            {
                return;
            }

            Rect rect = new Rect((Screen.width - 380f) * 0.5f, (Screen.height - 380f) * 0.5f, 380f, 380f);
            GUILayout.BeginArea(rect, "Pause", GUI.skin.window);

            if (GUILayout.Button("Resume", GUILayout.Height(40f)))
            {
                _isOpen = false;
                ApplyCursorState();
            }

            GUILayout.Space(8f);
            GUILayout.Label($"Master Volume: {RuntimeGameSettings.MasterVolume:0.00}");
            RuntimeGameSettings.MasterVolume = GUILayout.HorizontalSlider(RuntimeGameSettings.MasterVolume, 0f, 1f);

            GUILayout.Label($"Music Volume: {RuntimeGameSettings.MusicVolume:0.00}");
            RuntimeGameSettings.MusicVolume = GUILayout.HorizontalSlider(RuntimeGameSettings.MusicVolume, 0f, 1f);

            GUILayout.Label($"SFX Volume: {RuntimeGameSettings.SfxVolume:0.00}");
            RuntimeGameSettings.SfxVolume = GUILayout.HorizontalSlider(RuntimeGameSettings.SfxVolume, 0f, 1f);

            GUILayout.Label($"Look Sensitivity: {RuntimeGameSettings.LookSensitivity:0.00}");
            RuntimeGameSettings.LookSensitivity = GUILayout.HorizontalSlider(RuntimeGameSettings.LookSensitivity, 0.02f, 1f);

            if (GUILayout.Button("Apply Settings", GUILayout.Height(32f)))
            {
                RuntimeGameSettings.Apply();
                RuntimeGameSettings.Save();
            }

            if (GUILayout.Button("Leave Match And Return To Boot", GUILayout.Height(42f)))
            {
                // Network shutdown is explicit to avoid stale NGO state when returning to Boot.
                if (NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            }

            GUILayout.EndArea();
        }

        private void ApplyCursorState()
        {
            Cursor.lockState = _isOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _isOpen;
        }
    }
}
