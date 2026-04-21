// File: Assets/_Project/UI/Boot/BootConnectionOverlay.cs
using HueDoneIt.Core.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Boot
{
    public sealed class BootConnectionOverlay : MonoBehaviour
    {
        private enum ScreenState
        {
            Main,
            CreateLobby,
            JoinLobby,
            Customization,
            Settings
        }

        [SerializeField] private Vector2 panelSize = new(520f, 560f);

        private BootNetworkButtons _buttons;
        private ScreenState _state;
        private string _address;
        private string _portString;
        private int _bodyColorIndex;

        private static readonly Color[] BodyPalette =
        {
            new(1f, 0.42f, 0.55f),
            new(0.32f, 0.67f, 1f),
            new(0.27f, 0.95f, 0.62f),
            new(0.94f, 0.83f, 0.28f),
            new(0.82f, 0.42f, 1f),
            new(0.96f, 0.54f, 0.24f)
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (SceneManager.GetActiveScene().name != "Boot" || FindFirstObjectByType<BootConnectionOverlay>() != null)
            {
                return;
            }

            GameObject go = new(nameof(BootConnectionOverlay));
            go.AddComponent<BootConnectionOverlay>();
        }

        private void Awake()
        {
            _buttons = FindFirstObjectByType<BootNetworkButtons>();
            _address = BootNetworkButtons.GetConfiguredAddress();
            _portString = BootNetworkButtons.GetConfiguredPort().ToString();
            _state = ScreenState.Main;
            _bodyColorIndex = 0;
            RuntimeGameSettings.Apply();
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != "Boot")
            {
                return;
            }

            Rect rect = new((Screen.width - panelSize.x) * 0.5f, (Screen.height - panelSize.y) * 0.5f, panelSize.x, panelSize.y);
            GUILayout.BeginArea(rect, "Hue Done It - Beta Frontend", GUI.skin.window);

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
                case ScreenState.Customization:
                    DrawCustomization();
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
            GUILayout.Space(8f);
            GUILayout.Label("Production Slice Frontend", GUI.skin.box);
            if (GUILayout.Button("Create Lobby", GUILayout.Height(42f))) _state = ScreenState.CreateLobby;
            if (GUILayout.Button("Join Lobby", GUILayout.Height(42f))) _state = ScreenState.JoinLobby;
            if (GUILayout.Button("Character Customization", GUILayout.Height(42f))) _state = ScreenState.Customization;
            if (GUILayout.Button("Settings", GUILayout.Height(42f))) _state = ScreenState.Settings;
            if (GUILayout.Button("Quit", GUILayout.Height(36f))) Application.Quit();
        }

        private void DrawCreateLobby()
        {
            GUILayout.Label("Create Lobby", GUI.skin.box);
            DrawConnectionFields();

            GUILayout.Space(8f);
            GUILayout.Label($"Connected Players: {GetConnectedPlayers()}");
            GUILayout.Label($"CPU Opponents: {BootSessionConfig.RequestedCpuCount}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("- CPU", GUILayout.Height(30f)))
            {
                BootSessionConfig.RequestedCpuCount = Mathf.Max(0, BootSessionConfig.RequestedCpuCount - 1);
            }

            if (GUILayout.Button("+ CPU", GUILayout.Height(30f)))
            {
                BootSessionConfig.RequestedCpuCount = Mathf.Min(7, BootSessionConfig.RequestedCpuCount + 1);
            }

            GUILayout.EndHorizontal();

            bool hosting = _buttons != null && _buttons.IsHost;
            if (!hosting && GUILayout.Button("Host Lobby", GUILayout.Height(40f)))
            {
                _buttons?.StartHost();
            }

            GUI.enabled = hosting;
            if (GUILayout.Button("Start Match in Gameplay_Undertint", GUILayout.Height(46f)))
            {
                _buttons?.StartMatchFromLobby();
            }

            GUI.enabled = true;
            if (GUILayout.Button("Back", GUILayout.Height(32f))) _state = ScreenState.Main;
        }

        private void DrawJoinLobby()
        {
            GUILayout.Label("Join Lobby", GUI.skin.box);
            DrawConnectionFields();
            if (GUILayout.Button("Join as Client", GUILayout.Height(42f)))
            {
                _buttons?.StartClient();
            }

            if (GUILayout.Button("Back", GUILayout.Height(32f))) _state = ScreenState.Main;
        }

        private void DrawCustomization()
        {
            GUILayout.Label("Character Customization (placeholder)", GUI.skin.box);
            GUILayout.Label($"Hat Option: {BootSessionConfig.SelectedHat + 1}");
            BootSessionConfig.SelectedHat = Mathf.RoundToInt(GUILayout.HorizontalSlider(BootSessionConfig.SelectedHat, 0, 5));

            GUILayout.Label($"Outfit Option: {BootSessionConfig.SelectedOutfit + 1}");
            BootSessionConfig.SelectedOutfit = Mathf.RoundToInt(GUILayout.HorizontalSlider(BootSessionConfig.SelectedOutfit, 0, 5));

            GUILayout.Label("Body Color");
            _bodyColorIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(_bodyColorIndex, 0, BodyPalette.Length - 1));
            BootSessionConfig.SelectedBodyColor = BodyPalette[_bodyColorIndex];
            Color prev = GUI.color;
            GUI.color = BodyPalette[_bodyColorIndex];
            GUILayout.Box("Preview", GUILayout.Height(30f));
            GUI.color = prev;

            if (GUILayout.Button("Save Customization", GUILayout.Height(36f)))
            {
                BootSessionConfig.Save();
            }

            if (GUILayout.Button("Back", GUILayout.Height(32f))) _state = ScreenState.Main;
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

            GUILayout.Label($"Keys: Move[{RuntimeInputBindings.Left}/{RuntimeInputBindings.Right}/{RuntimeInputBindings.Forward}/{RuntimeInputBindings.Back}] Jump[{RuntimeInputBindings.Jump}] Burst[{RuntimeInputBindings.Burst}]");
            if (GUILayout.Button("Reset Controls To WASD + Space + Shift", GUILayout.Height(30f)))
            {
                RuntimeInputBindings.SetDefaults();
            }

            GUILayout.Label($"Window Mode: {RuntimeGameSettings.WindowMode}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Windowed")) RuntimeGameSettings.WindowMode = FullScreenMode.Windowed;
            if (GUILayout.Button("Fullscreen Window")) RuntimeGameSettings.WindowMode = FullScreenMode.FullScreenWindow;
            if (GUILayout.Button("Exclusive")) RuntimeGameSettings.WindowMode = FullScreenMode.ExclusiveFullScreen;
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Apply + Save", GUILayout.Height(36f)))
            {
                RuntimeGameSettings.Apply();
                RuntimeGameSettings.Save();
            }

            if (GUILayout.Button("Back", GUILayout.Height(32f))) _state = ScreenState.Main;
        }

        private void DrawConnectionFields()
        {
            GUILayout.Label("Server Address");
            _address = GUILayout.TextField(_address ?? string.Empty, 64);
            GUILayout.Label("Port");
            _portString = GUILayout.TextField(_portString ?? "7777", 8);
        }

        private int GetConnectedPlayers()
        {
            if (_buttons == null || !_buttons.IsNetworkActive)
            {
                return 0;
            }

            return Unity.Netcode.NetworkManager.Singleton != null
                ? Unity.Netcode.NetworkManager.Singleton.ConnectedClientsList.Count
                : 0;
        }

        private void ApplyConnectionFields()
        {
            BootNetworkButtons.SetConfiguredAddress(_address);
            if (ushort.TryParse(_portString, out ushort parsed))
            {
                BootNetworkButtons.SetConfiguredPort(parsed);
            }
        }
    }
}
