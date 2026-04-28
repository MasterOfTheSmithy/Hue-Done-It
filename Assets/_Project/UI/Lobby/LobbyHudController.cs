// File: Assets/_Project/UI/Lobby/LobbyHudController.cs
using HueDoneIt.Core.Bootstrap;
using HueDoneIt.Gameplay.Beta;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Lobby;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HueDoneIt.UI.Lobby
{
    // Minimal lobby HUD + menu controller.
    // The gameplay HUD is richer; the lobby only shows blob readability metrics the player needs before match start.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LobbyHudController : NetworkBehaviour
    {
        private const string LobbySceneName = "Lobby";

        private static readonly Color[] Palette =
        {
            new Color(1f, 0.42f, 0.55f),
            new Color(0.32f, 0.67f, 1f),
            new Color(0.27f, 0.95f, 0.62f),
            new Color(0.94f, 0.83f, 0.28f),
            new Color(0.82f, 0.42f, 1f),
            new Color(0.96f, 0.54f, 0.24f)
        };

        private bool _showMatchPanel;
        private bool _showCustomizationPanel;
        private int _colorIndex;

        private Canvas _statusCanvas;
        private RectTransform _statusPanel;
        private Image _opacityFill;
        private Image _diffusionFill;
        private Text _opacityValueText;
        private Text _diffusionValueText;

        private NetworkPlayerAvatar _localAvatar;
        private PlayerFloodZoneTracker _localFlood;

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
                return;
            }

            GameObject go = new GameObject(nameof(LobbyHudController));
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
                float distance = Vector3.Distance(
                    new Vector3(Palette[i].r, Palette[i].g, Palette[i].b),
                    new Vector3(configured.r, configured.g, configured.b));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    _colorIndex = i;
                }
            }

            if (IsClient)
            {
                EnsureStatusHudBuilt();
            }
        }

        private void Update()
        {
            if (!IsClient || SceneManager.GetActiveScene().name != LobbySceneName)
            {
                return;
            }

            EnsureStatusHudBuilt();
            ResolveLocalPlayerReferences();
            RefreshLobbyStatusHud();
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

        private void EnsureStatusHudBuilt()
        {
            if (_statusCanvas != null)
            {
                return;
            }

            GameObject canvasGo = new GameObject("LobbyRuntimeStatusCanvas");
            canvasGo.transform.SetParent(transform, false);

            _statusCanvas = canvasGo.AddComponent<Canvas>();
            _statusCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _statusCanvas.sortingOrder = 300;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            _statusPanel = CreatePanel(canvasGo.transform, "LobbyStatusPanel", new Vector2(28f, -28f), new Vector2(270f, 110f));
            CreateLabel(_statusPanel, "Header", "BLOB STATUS", new Vector2(14f, -10f), 22, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.97f, 0.97f, 1f, 0.95f));

            CreateMetricRow(_statusPanel, "Opacity", new Vector2(14f, -42f), new Color(0.29f, 0.87f, 0.69f, 1f), out _opacityFill, out _opacityValueText);
            CreateMetricRow(_statusPanel, "Diffusion", new Vector2(14f, -74f), new Color(0.34f, 0.7f, 1f, 1f), out _diffusionFill, out _diffusionValueText);
        }

        private void ResolveLocalPlayerReferences()
        {
            if (_localAvatar != null && _localAvatar.IsSpawned && _localAvatar.IsOwner)
            {
                return;
            }

            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (avatar != null && avatar.IsOwner)
                {
                    _localAvatar = avatar;
                    _localFlood = avatar.GetComponent<PlayerFloodZoneTracker>();
                    return;
                }
            }

            _localAvatar = null;
            _localFlood = null;
        }

        private void RefreshLobbyStatusHud()
        {
            if (_statusPanel == null)
            {
                return;
            }

            bool visible = _localAvatar != null;
            _statusPanel.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            float opacity01 = Mathf.Clamp01(_localAvatar.Opacity01);
            float diffusion01 = _localFlood != null ? Mathf.Clamp01(_localFlood.Saturation01) : 0f;

            SetBar(_opacityFill.rectTransform, opacity01);
            SetBar(_diffusionFill.rectTransform, diffusion01);
            _opacityValueText.text = $"{Mathf.RoundToInt(opacity01 * 100f)}%";
            _diffusionValueText.text = $"{Mathf.RoundToInt(diffusion01 * 100f)}%";
        }

        private void DrawMatchPanel()
        {
            Rect rect = new Rect((Screen.width - 560f) * 0.5f, 80f, 560f, 430f);
            GUILayout.BeginArea(rect, "Lobby Match Control", GUI.skin.window);

            NetworkLobbyState state = FindFirstObjectByType<NetworkLobbyState>();
            string selectedMap = state != null ? state.SelectedMapScene : BootSessionConfig.SelectedMapScene;
            int cpuCount = state != null ? state.TargetCpuCount : BootSessionConfig.RequestedCpuCount;
            int undertintVotes = state != null ? state.UndertintVotes : 0;
            int undertintAnnexVotes = state != null ? state.UndertintAnnexVotes : 0;
            int undertintOverflowVotes = state != null ? state.UndertintOverflowVotes : 0;

            GUILayout.Label("Map Vote");
            GUILayout.Label($"Selected Map: {selectedMap}");

            if (GUILayout.Button($"Undertint Core ({undertintVotes})", GUILayout.Height(36f)))
            {
                BootSessionConfig.SelectedMapScene = BetaGameplaySceneCatalog.MainMap;
                BootSessionConfig.Save();
                RequestVoteMapServerRpc(BetaGameplaySceneCatalog.MainMap);
            }

            if (GUILayout.Button($"Undertint Annex ({undertintAnnexVotes})", GUILayout.Height(32f)))
            {
                BootSessionConfig.SelectedMapScene = BetaGameplaySceneCatalog.AnnexMap;
                BootSessionConfig.Save();
                RequestVoteMapServerRpc(BetaGameplaySceneCatalog.AnnexMap);
            }

            if (GUILayout.Button($"Undertint Overflow ({undertintOverflowVotes})", GUILayout.Height(32f)))
            {
                BootSessionConfig.SelectedMapScene = BetaGameplaySceneCatalog.OverflowMap;
                BootSessionConfig.Save();
                RequestVoteMapServerRpc(BetaGameplaySceneCatalog.OverflowMap);
            }

            GUILayout.Space(8f);
            GUILayout.Label($"CPU Opponents: {cpuCount}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove CPU", GUILayout.Height(34f)))
            {
                int next = Mathf.Max(0, cpuCount - 1);
                BootSessionConfig.RequestedCpuCount = next;
                BootSessionConfig.Save();
                RequestSetCpuCountServerRpc(next);
            }

            if (GUILayout.Button("Add CPU", GUILayout.Height(34f)))
            {
                int next = Mathf.Min(7, cpuCount + 1);
                BootSessionConfig.RequestedCpuCount = next;
                BootSessionConfig.Save();
                RequestSetCpuCountServerRpc(next);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("All players can vote on map. Host starts the selected map when ready.");
            GUI.enabled = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            if (GUILayout.Button("Start Match", GUILayout.Height(44f)))
            {
                RequestStartMatchServerRpc();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Close", GUILayout.Height(30f)))
            {
                ClosePanels();
            }

            GUILayout.EndArea();
        }

        private void DrawCustomizationPanel()
        {
            Rect rect = new Rect((Screen.width - 560f) * 0.5f, 80f, 560f, 390f);
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

            GUILayout.Label("Use the lobby mirror to verify how your blob looks before the match starts.");

            if (GUILayout.Button("Apply", GUILayout.Height(34f)))
            {
                BootSessionConfig.Save();
                RequestApplyCustomizationServerRpc((Color32)BootSessionConfig.SelectedBodyColor, BootSessionConfig.SelectedHat, BootSessionConfig.SelectedOutfit);
            }

            if (GUILayout.Button("Close", GUILayout.Height(30f)))
            {
                ClosePanels();
            }

            GUILayout.EndArea();
        }

        private void ClosePanels()
        {
            _showMatchPanel = false;
            _showCustomizationPanel = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestVoteMapServerRpc(string mapName, ServerRpcParams serverRpcParams = default)
        {
            NetworkLobbyState state = FindFirstObjectByType<NetworkLobbyState>();
            if (state == null)
            {
                return;
            }

            state.ServerVoteForMap(serverRpcParams.Receive.SenderClientId, mapName);
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

        [ServerRpc(RequireOwnership = false)]
        private void RequestApplyCustomizationServerRpc(Color32 bodyColor, int hatIndex, int outfitIndex, ServerRpcParams serverRpcParams = default)
        {
            if (NetworkManager == null)
            {
                return;
            }

            ulong senderClientId = serverRpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) || client.PlayerObject == null)
            {
                return;
            }

            if (client.PlayerObject.TryGetComponent(out NetworkPlayerAvatar avatar))
            {
                avatar.ServerApplyCustomization(bodyColor, hatIndex, outfitIndex);
            }

            if (client.PlayerObject.TryGetComponent(out PlayerColorProfile colorProfile))
            {
                colorProfile.ServerSetPlayerColor(bodyColor);
            }
        }

        private static RectTransform CreatePanel(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.05f, 0.06f, 0.1f, 0.82f);
            return rect;
        }

        private static void CreateMetricRow(Transform parent, string label, Vector2 anchoredPosition, Color fillColor, out Image fillImage, out Text valueText)
        {
            CreateLabel(parent, label + "Label", label.ToUpperInvariant(), anchoredPosition, 16, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.95f, 0.95f, 1f, 0.92f));

            GameObject backgroundGo = new GameObject(label + "BarBg", typeof(Image));
            backgroundGo.transform.SetParent(parent, false);
            RectTransform bgRect = backgroundGo.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 1f);
            bgRect.anchorMax = new Vector2(0f, 1f);
            bgRect.pivot = new Vector2(0f, 1f);
            bgRect.anchoredPosition = anchoredPosition + new Vector2(84f, -2f);
            bgRect.sizeDelta = new Vector2(126f, 18f);
            backgroundGo.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.24f, 0.94f);

            GameObject fillGo = new GameObject(label + "BarFill", typeof(Image));
            fillGo.transform.SetParent(backgroundGo.transform, false);
            fillImage = fillGo.GetComponent<Image>();
            fillImage.color = fillColor;
            RectTransform fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(126f, 0f);

            valueText = CreateLabel(parent, label + "Value", "100%", anchoredPosition + new Vector2(220f, 0f), 15, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.95f, 0.95f, 1f, 0.95f));
        }

        private static Text CreateLabel(Transform parent, string name, string text, Vector2 anchoredPosition, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.color = color;
            label.text = text;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(240f, 22f);
            return label;
        }

        private static void SetBar(RectTransform fillRect, float normalized)
        {
            if (fillRect == null)
            {
                return;
            }

            fillRect.sizeDelta = new Vector2(126f * Mathf.Clamp01(normalized), fillRect.sizeDelta.y);
        }
    }
}
