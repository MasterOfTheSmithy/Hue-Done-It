// File: Assets/_Project/UI/Boot/BootConnectionOverlay.cs
using HueDoneIt.Core.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Boot
{
    // This is the visible frontend menu shown in Boot.
    // It keeps Boot as a pure 2D menu scene and sends all live networked play into Lobby.
    public sealed class BootConnectionOverlay : MonoBehaviour
    {
        private enum ScreenState
        {
            Main,
            CreateLobby,
            JoinLobby,
            Settings
        }

        [SerializeField] private Vector2 panelSize = new Vector2(640f, 560f);

        // This is the bridge to network start/stop actions and scene transition.
        private BootNetworkButtons _buttons;

        // This controls which frontend page is currently visible.
        private ScreenState _state = ScreenState.Main;

        // These are editable network fields in the Boot menu.
        private string _address;
        private string _portString;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // This keeps the overlay available when Boot is loaded directly.
            if (SceneManager.GetActiveScene().name != "Boot" || FindFirstObjectByType<BootConnectionOverlay>() != null)
            {
                return;
            }

            GameObject go = new GameObject(nameof(BootConnectionOverlay));
            go.AddComponent<BootConnectionOverlay>();
        }

        private void Awake()
        {
            _buttons = FindFirstObjectByType<BootNetworkButtons>();
            _address = BootNetworkButtons.GetConfiguredAddress();
            _portString = BootNetworkButtons.GetConfiguredPort().ToString();
            RuntimeGameSettings.Apply();
            TryDisableLegacyBootCanvas();
            EnsureBootCursorState();
        }

        private void Update()
        {
            if (_buttons == null)
            {
                _buttons = FindFirstObjectByType<BootNetworkButtons>();
            }

            EnsureBootCursorState();
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != "Boot")
            {
                return;
            }

            Rect rect = new Rect(
                (Screen.width - panelSize.x) * 0.5f,
                (Screen.height - panelSize.y) * 0.5f,
                panelSize.x,
                panelSize.y);

            GUILayout.BeginArea(rect, "Hue Done It - Main Menu", GUI.skin.window);
            GUILayout.Space(6f);

            switch (_state)
            {
                case ScreenState.Main:
                    DrawMain();
                    break;
                case ScreenState.CreateLobby:
                    DrawCreateLobby();
                    break;
                case ScreenState.JoinLobby:
                    DrawJoinLobby();
                    break;
                case ScreenState.Settings:
                    DrawSettings();
                    break;
            }

            GUILayout.EndArea();
            ApplyConnectionFields();
        }

        private void DrawMain()
        {
            GUILayout.Label("Main Menu", GUI.skin.box);
            GUILayout.Label("Boot is menu-only. Character customization and map voting happen in Lobby.");

            if (GUILayout.Button("Create Lobby", GUILayout.Height(48f)))
            {
                _state = ScreenState.CreateLobby;
            }

            if (GUILayout.Button("Join Lobby", GUILayout.Height(48f)))
            {
                _state = ScreenState.JoinLobby;
            }

            if (GUILayout.Button("Settings", GUILayout.Height(48f)))
            {
                _state = ScreenState.Settings;
            }

            if (GUILayout.Button("Quit", GUILayout.Height(42f)))
            {
                Application.Quit();
            }
        }

        private void DrawCreateLobby()
        {
            GUILayout.Label("Create Lobby", GUI.skin.box);
            DrawConnectionFields();
            GUILayout.Label("Create Lobby enters the 3D Lobby scene. Match control, map voting, CPUs, and customization are in-world there.");

            if (GUILayout.Button("Host Lobby", GUILayout.Height(48f)))
            {
                _buttons?.StartHostLobby();
            }

            if (GUILayout.Button("Back", GUILayout.Height(34f)))
            {
                _state = ScreenState.Main;
            }
        }

        private void DrawJoinLobby()
        {
            GUILayout.Label("Join Lobby", GUI.skin.box);
            DrawConnectionFields();
            GUILayout.Label("Join enters the 3D Lobby scene and then connects as a client.");

            if (GUILayout.Button("Join as Client", GUILayout.Height(48f)))
            {
                _buttons?.StartClient();
            }

            if (GUILayout.Button("Back", GUILayout.Height(34f)))
            {
                _state = ScreenState.Main;
            }
        }

        private void DrawSettings()
        {
            GUILayout.Label("Settings", GUI.skin.box);

            GUILayout.Label($"Master Volume: {RuntimeGameSettings.MasterVolume:0.00}");
            RuntimeGameSettings.MasterVolume = GUILayout.HorizontalSlider(RuntimeGameSettings.MasterVolume, 0f, 1f);

            GUILayout.Label($"Music Volume: {RuntimeGameSettings.MusicVolume:0.00}");
            RuntimeGameSettings.MusicVolume = GUILayout.HorizontalSlider(RuntimeGameSettings.MusicVolume, 0f, 1f);

            GUILayout.Label($"SFX Volume: {RuntimeGameSettings.SfxVolume:0.00}");
            RuntimeGameSettings.SfxVolume = GUILayout.HorizontalSlider(RuntimeGameSettings.SfxVolume, 0f, 1f);

            GUILayout.Label($"Look Sensitivity: {RuntimeGameSettings.LookSensitivity:0.00}");
            RuntimeGameSettings.LookSensitivity = GUILayout.HorizontalSlider(RuntimeGameSettings.LookSensitivity, 0.02f, 1f);

            GUILayout.Label($"Window Mode: {RuntimeGameSettings.WindowMode}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Windowed")) RuntimeGameSettings.WindowMode = FullScreenMode.Windowed;
            if (GUILayout.Button("Fullscreen Window")) RuntimeGameSettings.WindowMode = FullScreenMode.FullScreenWindow;
            if (GUILayout.Button("Exclusive")) RuntimeGameSettings.WindowMode = FullScreenMode.ExclusiveFullScreen;
            GUILayout.EndHorizontal();

            GUILayout.Label($"Current Keys: Move[{RuntimeInputBindings.Left},{RuntimeInputBindings.Right},{RuntimeInputBindings.Forward},{RuntimeInputBindings.Back}] Jump[{RuntimeInputBindings.Jump}] Burst[{RuntimeInputBindings.Burst}]");
            if (GUILayout.Button("Reset Controls To WASD + Space + Shift", GUILayout.Height(34f)))
            {
                RuntimeInputBindings.SetDefaults();
            }

            if (GUILayout.Button("Apply And Save", GUILayout.Height(40f)))
            {
                RuntimeGameSettings.Apply();
                RuntimeGameSettings.Save();
            }

            if (GUILayout.Button("Back", GUILayout.Height(34f)))
            {
                _state = ScreenState.Main;
            }
        }

        private void DrawConnectionFields()
        {
            GUILayout.Label("Server Address");
            _address = GUILayout.TextField(_address ?? string.Empty, 64);
            GUILayout.Label("Port");
            _portString = GUILayout.TextField(_portString ?? "7777", 8);
        }

        private void ApplyConnectionFields()
        {
            BootNetworkButtons.SetConfiguredAddress(_address);
            if (ushort.TryParse(_portString, out ushort parsedPort))
            {
                BootNetworkButtons.SetConfiguredPort(parsedPort);
            }
        }

        private static void EnsureBootCursorState()
        {
            if (SceneManager.GetActiveScene().name != "Boot")
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!Mathf.Approximately(Time.timeScale, 1f))
            {
                Time.timeScale = 1f;
            }
        }

        private static void TryDisableLegacyBootCanvas()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null)
                {
                    continue;
                }

                if (canvas.gameObject.name == "BootCanvas")
                {
                    canvas.gameObject.SetActive(false);
                }
            }
        }
    }
}
