// File: Assets/_Project/UI/Lobby/LobbyHudController.cs
using HueDoneIt.Core.Bootstrap;
using HueDoneIt.Gameplay.Lobby;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Lobby
{
    // This runtime HUD is opened from in-world lobby interactables.
    // It is intentionally simple IMGUI so the lobby loop works without prefab authoring dependency.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LobbyHudController : NetworkBehaviour
    {
        private const string LobbySceneName = "Lobby";

        private static readonly Color[] Palette =
        {
            new(1f, 0.42f, 0.55f),
            new(0.32f, 0.67f, 1f),
            new(0.27f, 0.95f, 0.62f),
            new(0.94f, 0.83f, 0.28f),
            new(0.82f, 0.42f, 1f),
            new(0.96f, 0.54f, 0.24f)
        };

        private bool _showMatchPanel;
        private bool _showCustomizationPanel;
        private int _colorIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstalled()
        {
            if (SceneManager.GetActiveScene().name != LobbySceneName)
            {
                return;
            }

            if (FindFirstObjectByType<LobbyHudController>() != null)
            {
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                // Non-server peers receive the spawned HUD controller from the server.
                return;
            }

            GameObject go = new(nameof(LobbyHudController));
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<LobbyHudController>();
            networkObject.Spawn(true);
        }

        public static void ShowMatchPanelClient(ulong clientId)
        {
            LobbyHudController controller = FindFirstObjectByType<LobbyHudController>();
            if (controller == null || !controller.IsServer)
            {
                return;
            }

            controller.ShowMatchPanelClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });
        }

        public static void ShowCustomizationPanelClient(ulong clientId)
        {
            LobbyHudController controller = FindFirstObjectByType<LobbyHudController>();
            if (controller == null || !controller.IsServer)
            {
                return;
            }

            controller.ShowCustomizationPanelClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });
        }

        private void Start()
        {
            Color configured = BootSessionConfig.SelectedBodyColor;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                float distance = Vector3.Distance(new Vector3(Palette[i].r, Palette[i].g, Palette[i].b), new Vector3(configured.r, configured.g, configured.b));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    _colorIndex = i;
                }
            }
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != LobbySceneName || !IsClient)
            {
                return;
            }

            if (_showMatchPanel)
            {
                DrawMatchPanel();
            }

            if (_showCustomizationPanel)
            {
                DrawCustomizationPanel();
            }
        }

        [ClientRpc]
        private void ShowMatchPanelClientRpc(ClientRpcParams clientRpcParams = default)
        {
            _showMatchPanel = true;
            _showCustomizationPanel = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [ClientRpc]
        private void ShowCustomizationPanelClientRpc(ClientRpcParams clientRpcParams = default)
        {
            _showCustomizationPanel = true;
            _showMatchPanel = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void DrawMatchPanel()
        {
            Rect rect = new((Screen.width - 520f) * 0.5f, 80f, 520f, 360f);
            GUILayout.BeginArea(rect, "Lobby Match Control", GUI.skin.window);

            GUILayout.Label("Map Selection");
            string currentMap = BootSessionConfig.SelectedMapScene;
            GUILayout.Label($"Current: {currentMap}");

            if (GUILayout.Button("Gameplay_Undertint", GUILayout.Height(36f)))
            {
                BootSessionConfig.SelectedMapScene = "Gameplay_Undertint";
                BootSessionConfig.Save();
                RequestMapSelectServerRpc("Gameplay_Undertint");
            }

            GUILayout.Space(8f);
            GUILayout.Label($"CPU Opponents: {BootSessionConfig.RequestedCpuCount}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove CPU", GUILayout.Height(34f)))
            {
                int next = Mathf.Max(0, BootSessionConfig.RequestedCpuCount - 1);
                BootSessionConfig.RequestedCpuCount = next;
                BootSessionConfig.Save();
                RequestSetCpuCountServerRpc(next);
            }

            if (GUILayout.Button("Add CPU", GUILayout.Height(34f)))
            {
                int next = Mathf.Min(7, BootSessionConfig.RequestedCpuCount + 1);
                BootSessionConfig.RequestedCpuCount = next;
                BootSessionConfig.Save();
                RequestSetCpuCountServerRpc(next);
            }
            GUILayout.EndHorizontal();

            GUI.enabled = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            if (GUILayout.Button("Start Match", GUILayout.Height(44f)))
            {
                RequestStartMatchServerRpc();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Close", GUILayout.Height(30f)))
            {
                _showMatchPanel = false;
            }

            GUILayout.EndArea();
        }

        private void DrawCustomizationPanel()
        {
            Rect rect = new((Screen.width - 520f) * 0.5f, 80f, 520f, 360f);
            GUILayout.BeginArea(rect, "Customization Hub", GUI.skin.window);

            GUILayout.Label("Body Color");
            _colorIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(_colorIndex, 0, Palette.Length - 1));
            BootSessionConfig.SelectedBodyColor = Palette[_colorIndex];

            Color old = GUI.color;
            GUI.color = Palette[_colorIndex];
            GUILayout.Box("Color Preview", GUILayout.Height(28f));
            GUI.color = old;

            GUILayout.Label($"Hat: {BootSessionConfig.SelectedHat + 1}");
            BootSessionConfig.SelectedHat = Mathf.RoundToInt(GUILayout.HorizontalSlider(BootSessionConfig.SelectedHat, 0, 5));

            GUILayout.Label($"Outfit: {BootSessionConfig.SelectedOutfit + 1}");
            BootSessionConfig.SelectedOutfit = Mathf.RoundToInt(GUILayout.HorizontalSlider(BootSessionConfig.SelectedOutfit, 0, 5));

            if (GUILayout.Button("Save", GUILayout.Height(34f)))
            {
                BootSessionConfig.Save();
            }

            if (GUILayout.Button("Close", GUILayout.Height(30f)))
            {
                _showCustomizationPanel = false;
            }

            GUILayout.EndArea();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestMapSelectServerRpc(string mapName)
        {
            NetworkLobbyState state = FindFirstObjectByType<NetworkLobbyState>();
            if (state == null)
            {
                return;
            }

            state.ServerSelectMap(mapName);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetCpuCountServerRpc(int cpuCount)
        {
            NetworkLobbyState state = FindFirstObjectByType<NetworkLobbyState>();
            if (state == null)
            {
                return;
            }

            state.ServerSetCpuCount(cpuCount);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestStartMatchServerRpc()
        {
            NetworkLobbyState state = FindFirstObjectByType<NetworkLobbyState>();
            if (state == null)
            {
                return;
            }

            state.ServerStartMatch();
        }
    }
}
