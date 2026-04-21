// File: Assets/_Project/UI/Gameplay/GameplayPauseMenu.cs
using HueDoneIt.Core.Bootstrap;
using HueDoneIt.UI.Boot;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Gameplay
{
    public sealed class GameplayPauseMenu : MonoBehaviour
    {
        private bool _isOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (SceneManager.GetActiveScene().name != "Gameplay_Undertint" || FindFirstObjectByType<GameplayPauseMenu>() != null)
            {
                return;
            }

            new GameObject(nameof(GameplayPauseMenu)).AddComponent<GameplayPauseMenu>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                _isOpen = !_isOpen;
                Cursor.lockState = _isOpen ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = _isOpen;
            }
        }

        private void OnGUI()
        {
            if (!_isOpen || SceneManager.GetActiveScene().name != "Gameplay_Undertint")
            {
                return;
            }

            Rect rect = new((Screen.width - 360f) * 0.5f, (Screen.height - 360f) * 0.5f, 360f, 360f);
            GUILayout.BeginArea(rect, "Pause", GUI.skin.window);
            if (GUILayout.Button("Resume", GUILayout.Height(40f)))
            {
                _isOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            GUILayout.Space(8f);
            GUILayout.Label($"Master Volume: {RuntimeGameSettings.MasterVolume:0.00}");
            RuntimeGameSettings.MasterVolume = GUILayout.HorizontalSlider(RuntimeGameSettings.MasterVolume, 0f, 1f);
            GUILayout.Label($"Look Sensitivity: {RuntimeGameSettings.LookSensitivity:0.00}");
            RuntimeGameSettings.LookSensitivity = GUILayout.HorizontalSlider(RuntimeGameSettings.LookSensitivity, 0.02f, 1f);
            if (GUILayout.Button("Apply Settings", GUILayout.Height(32f)))
            {
                RuntimeGameSettings.Apply();
                RuntimeGameSettings.Save();
            }

            if (GUILayout.Button("Leave Match / Return to Boot", GUILayout.Height(40f)))
            {
                BootNetworkButtons buttons = FindFirstObjectByType<BootNetworkButtons>();
                if (buttons != null)
                {
                    buttons.Shutdown();
                }

                SceneManager.LoadScene("Boot");
            }

            GUILayout.EndArea();
        }
    }
}
