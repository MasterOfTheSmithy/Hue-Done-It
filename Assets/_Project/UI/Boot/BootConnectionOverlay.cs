// File: Assets/_Project/UI/Boot/BootConnectionOverlay.cs
using System.Reflection;
using HueDoneIt.Core.Bootstrap;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Boot
{
    // This is the visible frontend menu shown in the Boot scene.
    // It uses IMGUI so it can be dropped into the existing project without requiring new authored UI prefabs.
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

        [SerializeField] private Vector2 panelSize = new Vector2(640f, 620f);

        // This connects to the existing BootNetworkButtons scene object if present.
        private BootNetworkButtons _buttons;

        // This controls which frontend page is currently visible.
        private ScreenState _state = ScreenState.Main;

        // These are the editable network connection fields shown in the frontend.
        private string _address;
        private string _portString;

        // This drives the placeholder body color slider preview.
        private int _bodyColorIndex;

        // Placeholder body color palette for simple customization.
        private static readonly Color[] BodyPalette =
        {
            new Color(1f, 0.42f, 0.55f),
            new Color(0.32f, 0.67f, 1f),
            new Color(0.27f, 0.95f, 0.62f),
            new Color(0.94f, 0.83f, 0.28f),
            new Color(0.82f, 0.42f, 1f),
            new Color(0.96f, 0.54f, 0.24f)
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Install only in the Boot scene and only once.
            if (SceneManager.GetActiveScene().name != "Boot" || FindFirstObjectByType<BootConnectionOverlay>() != null)
            {
                return;
            }

            GameObject go = new GameObject(nameof(BootConnectionOverlay));
            go.AddComponent<BootConnectionOverlay>();
        }

        private void Awake()
        {
            // Resolve the legacy button bridge if it exists in the scene.
            _buttons = FindFirstObjectByType<BootNetworkButtons>();

            // Load persisted connection values.
            _address = BootNetworkButtons.GetConfiguredAddress();
            _portString = BootNetworkButtons.GetConfiguredPort().ToString();

            // Apply runtime settings so the boot frontend reflects current config.
            RuntimeGameSettings.Apply();

            // Convert the saved body color into a slider index for the placeholder customization screen.
            _bodyColorIndex = FindClosestBodyColorIndex(BootSessionConfig.SelectedBodyColor);

            // Hide the legacy Boot canvas so the user sees only one frontend.
            TryDisableLegacyBootCanvas();

            // Cursor control is handled by the frontend exclusively, so force it in correct state for scene entry and after returning from gameplay.
            ForceBootCursorState();
        }

        private void Update()
        {
            // If scene objects were reloaded, resolve the button bridge again.
            if (_buttons == null)
            {
                _buttons = FindFirstObjectByType<BootNetworkButtons>();
            }

            // Do not render this frontend outside the Boot scene.
            if (SceneManager.GetActiveScene().name != "Boot")
            {
                enabled = false;
            }
            ForceBootCursorState();
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

            GUILayout.BeginArea(rect, "Hue Done It - Beta Frontend", GUI.skin.window);
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

                case ScreenState.Customization:
                    DrawCustomization();
                    break;

                case ScreenState.Settings:
                    DrawSettings();
                    break;
            }

            GUILayout.EndArea();

            // Persist connection values every frame so legacy button handlers use the edited values.
            ApplyConnectionFields();
        }

        private void DrawMain()
        {
            GUILayout.Label("Main Menu", GUI.skin.box);
            GUILayout.Label("Choose a frontend path.");

            if (GUILayout.Button("Create Lobby", GUILayout.Height(48f)))
            {
                _state = ScreenState.CreateLobby;
            }

            if (GUILayout.Button("Join Lobby", GUILayout.Height(48f)))
            {
                _state = ScreenState.JoinLobby;
            }

            if (GUILayout.Button("Character Customization", GUILayout.Height(48f)))
            {
                _state = ScreenState.Customization;
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

            bool networkActive = IsNetworkActive();
            bool isHost = IsHost();
            int connectedPlayers = GetConnectedPlayers();
            int cpuCount = BootSessionConfig.RequestedCpuCount;

            GUILayout.Space(8f);
            GUILayout.Label($"Lobby Network Active: {networkActive}");
            GUILayout.Label($"Local Peer Is Host: {isHost}");
            GUILayout.Label($"Connected Human Players: {connectedPlayers}");
            GUILayout.Label($"CPU Opponents: {cpuCount}");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Remove CPU", GUILayout.Height(34f)))
            {
                BootSessionConfig.RequestedCpuCount = Mathf.Max(0, BootSessionConfig.RequestedCpuCount - 1);
                BootSessionConfig.Save();
            }

            if (GUILayout.Button("Add CPU", GUILayout.Height(34f)))
            {
                BootSessionConfig.RequestedCpuCount = Mathf.Min(7, BootSessionConfig.RequestedCpuCount + 1);
                BootSessionConfig.Save();
            }

            GUILayout.EndHorizontal();

            if (!networkActive)
            {
                if (GUILayout.Button("Host Lobby", GUILayout.Height(42f)))
                {
                    StartHostLobby();
                }
            }
            else
            {
                GUILayout.Label("Host is running. Start Match becomes available for host.");
            }

            GUI.enabled = isHost;

            if (GUILayout.Button("Start Match in Gameplay_Undertint", GUILayout.Height(48f)))
            {
                StartMatchFromLobby();
            }

            GUI.enabled = true;

            if (GUILayout.Button("Back", GUILayout.Height(34f)))
            {
                _state = ScreenState.Main;
            }
        }

        private void DrawJoinLobby()
        {
            GUILayout.Label("Join Lobby", GUI.skin.box);
            DrawConnectionFields();
            GUILayout.Label("Enter host address and port, then join as client.");

            if (GUILayout.Button("Join as Client", GUILayout.Height(48f)))
            {
                _buttons?.StartClient();
            }

            if (GUILayout.Button("Back", GUILayout.Height(34f)))
            {
                _state = ScreenState.Main;
            }
        }

        private void DrawCustomization()
        {
            GUILayout.Label("Character Customization", GUI.skin.box);
            GUILayout.Label("Placeholder options that persist in PlayerPrefs.");

            GUILayout.Label($"Hat Option: {BootSessionConfig.SelectedHat + 1}");
            BootSessionConfig.SelectedHat = Mathf.RoundToInt(GUILayout.HorizontalSlider(BootSessionConfig.SelectedHat, 0, 5));

            GUILayout.Label($"Outfit Option: {BootSessionConfig.SelectedOutfit + 1}");
            BootSessionConfig.SelectedOutfit = Mathf.RoundToInt(GUILayout.HorizontalSlider(BootSessionConfig.SelectedOutfit, 0, 5));

            GUILayout.Label("Body Color");
            _bodyColorIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(_bodyColorIndex, 0, BodyPalette.Length - 1));
            BootSessionConfig.SelectedBodyColor = BodyPalette[_bodyColorIndex];

            Color oldColor = GUI.color;
            GUI.color = BodyPalette[_bodyColorIndex];
            GUILayout.Box("Color Preview", GUILayout.Height(28f));
            GUI.color = oldColor;

            if (GUILayout.Button("Save Customization", GUILayout.Height(40f)))
            {
                BootSessionConfig.Save();
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

            if (GUILayout.Button("Windowed"))
            {
                RuntimeGameSettings.WindowMode = FullScreenMode.Windowed;
            }

            if (GUILayout.Button("Fullscreen Window"))
            {
                RuntimeGameSettings.WindowMode = FullScreenMode.FullScreenWindow;
            }

            if (GUILayout.Button("Exclusive"))
            {
                RuntimeGameSettings.WindowMode = FullScreenMode.ExclusiveFullScreen;
            }

            GUILayout.EndHorizontal();

            GUILayout.Label(
                $"Current Keys: Move[{RuntimeInputBindings.Left},{RuntimeInputBindings.Right},{RuntimeInputBindings.Forward},{RuntimeInputBindings.Back}] " +
                $"Jump[{RuntimeInputBindings.Jump}] Burst[{RuntimeInputBindings.Burst}]");

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

        private int GetConnectedPlayers()
        {
            // This reads live NGO state instead of cached values so the lobby reflects current state.
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return 0;
            }

            return NetworkManager.Singleton.ConnectedClientsList.Count;
        }

        private void ApplyConnectionFields()
        {
            BootNetworkButtons.SetConfiguredAddress(_address);

            if (ushort.TryParse(_portString, out ushort parsedPort))
            {
                BootNetworkButtons.SetConfiguredPort(parsedPort);
            }
        }

        private bool IsNetworkActive()
        {
            // Prefer a button-bridge property if the newer API exists.
            if (TryGetBoolProperty(_buttons, "IsNetworkActive", out bool value))
            {
                return value;
            }

            // Fall back to NGO directly for compatibility with the older button implementation.
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        private bool IsHost()
        {
            // Prefer a button-bridge property if the newer API exists.
            if (TryGetBoolProperty(_buttons, "IsHost", out bool value))
            {
                return value;
            }

            // Fall back to NGO directly for compatibility with the older button implementation.
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        }

        private void StartHostLobby()
        {
            if (_buttons == null)
            {
                return;
            }

            // Use the newer staged-lobby method if present.
            if (TryInvokeParameterlessMethod(_buttons, "StartHostLobby"))
            {
                return;
            }

            // Fall back to the legacy implementation.
            _buttons.StartHost();
        }

        private void StartMatchFromLobby()
        {
            if (_buttons == null)
            {
                return;
            }

            // Use the newer explicit match-start method if present.
            if (TryInvokeParameterlessMethod(_buttons, "StartMatchFromLobby"))
            {
                return;
            }

            // Fall back to direct NGO scene loading if the host is already active.
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsHost)
            {
                return;
            }

            const string gameplaySceneName = "Gameplay_Undertint";

            if (!manager.NetworkConfig.EnableSceneManagement)
            {
                SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
                return;
            }

            manager.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        }

        private static bool TryGetBoolProperty(object target, string propertyName, out bool value)
        {
            value = false;

            if (target == null)
            {
                return false;
            }

            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property == null || property.PropertyType != typeof(bool))
            {
                return false;
            }

            object raw = property.GetValue(target);
            if (raw is not bool cast)
            {
                return false;
            }

            value = cast;
            return true;
        }

        private static bool TryInvokeParameterlessMethod(object target, string methodName)
        {
            if (target == null)
            {
                return false;
            }

            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                System.Type.EmptyTypes,
                null);

            if (method == null)
            {
                return false;
            }

            method.Invoke(target, null);
            return true;
        }

        private static int FindClosestBodyColorIndex(Color selectedColor)
        {
            int bestIndex = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < BodyPalette.Length; i++)
            {
                float distance =
                    Mathf.Abs(BodyPalette[i].r - selectedColor.r) +
                    Mathf.Abs(BodyPalette[i].g - selectedColor.g) +
                    Mathf.Abs(BodyPalette[i].b - selectedColor.b);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
        private static void ForceBootCursorState()
        {
            // Boot is a menu scene, so cursor must always be visible and unlocked.
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // Returning from gameplay can leave time scale paused.
            // Reset it here so the frontend always behaves normally.
            if (!Mathf.Approximately(Time.timeScale, 1f))
            {
                Time.timeScale = 1f;
            }
        }

        private static void TryDisableLegacyBootCanvas()
        {
            // Existing Boot scene ships with old buttons. Disable that canvas to prevent conflicting UX.
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