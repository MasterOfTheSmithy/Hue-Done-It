// File: Assets/_Project/UI/Boot/BootNetworkButtons.cs
using System;
using System.Reflection;
using HueDoneIt.Core.Bootstrap;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Boot
{
    // This component is intentionally kept on a Boot scene object so legacy scene buttons still work.
    // It owns boot-to-lobby network startup so hosting and joining do not leave players in Boot.
    public sealed class BootNetworkButtons : MonoBehaviour
    {
        private const string AddressPrefsKey = "HueDoneIt.Network.Address";
        private const string PortPrefsKey = "HueDoneIt.Network.Port";

        private static BootNetworkButtons _instance;

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private string lobbySceneName = "Lobby";
        [SerializeField] private string fallbackGameplaySceneName = "Gameplay_Undertint";
        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private string defaultAddress = "127.0.0.1";

        // These flags carry the intent across the Boot -> Lobby scene transition.
        // The component is marked DontDestroyOnLoad so the callback survives the load.
        private bool _pendingHostStartAfterLobbyLoad;
        private bool _pendingClientStartAfterLobbyLoad;
        private string _statusMessage = "Ready.";

        // This value is read by the boot frontend to show current status.
        public bool IsNetworkActive => TryResolveNetworkManager(out NetworkManager manager) && manager.IsListening;

        // This value is read by the boot frontend to decide if Start Match is allowed.
        public bool IsHost => TryResolveNetworkManager(out NetworkManager manager) && manager.IsHost;
        public string StatusMessage => _statusMessage;

        private void Awake()
        {
            // Keep one runtime controller instance so scene transition callbacks are deterministic.
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // This makes the visible frontend robust. The old Boot scene still has Host/Client/Shutdown buttons,
            // so we attach the new frontend overlay from here when in Boot.
            if (SceneManager.GetActiveScene().name == "Boot" && FindFirstObjectByType<BootConnectionOverlay>() == null)
            {
                GameObject overlayObject = new GameObject(nameof(BootConnectionOverlay));
                overlayObject.AddComponent<BootConnectionOverlay>();
            }

            // Apply persisted runtime settings on startup so volume and window mode are not stale.
            RuntimeGameSettings.Apply();
            EnsureBootCursorUnlocked();
            if (TryResolveNetworkManager(out NetworkManager manager))
            {
                PreserveNetworkManager(manager);
                RegisterNetworkCallbacks(manager);
                SetStatus($"Ready on {GetConfiguredAddress(defaultAddress)}:{GetConfiguredPort(defaultPort)}.");
            }
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                if (networkManager != null)
                {
                    networkManager.OnClientConnectedCallback -= HandleClientConnected;
                    networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                }

                _instance = null;
            }
        }

        // Legacy Host button path now enters Lobby, not gameplay.
        public void StartHost()
        {
            StartHostLobby();
        }

        // New frontend path.
        // This hosts the network session and moves to the dedicated Lobby scene.
        public void StartHostLobby()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                SetStatus("Cannot host: NetworkManager is missing.");
                return;
            }

            if (SceneManager.GetActiveScene().name == "Boot" && !manager.IsListening)
            {
                if (!Application.CanStreamedLevelBeLoaded(lobbySceneName))
                {
                    SetStatus($"Cannot host: Lobby scene '{lobbySceneName}' is missing from Build Settings.");
                    return;
                }

                PreserveNetworkManager(manager);
                _pendingHostStartAfterLobbyLoad = true;
                SetStatus("Loading lobby before starting host...");
                SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
                return;
            }

            if (!manager.IsListening)
            {
                StartHostNow(manager);
            }

            LoadLobbySceneIfHost(manager);
        }

        // Legacy client button and new frontend join both use this.
        public void StartClient()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                SetStatus("Cannot join: NetworkManager is missing.");
                return;
            }

            if (manager.IsListening)
            {
                SetStatus("Already connected. Shut down before starting a new client session.");
                return;
            }

            if (SceneManager.GetActiveScene().name == "Boot")
            {
                if (!Application.CanStreamedLevelBeLoaded(lobbySceneName))
                {
                    SetStatus($"Cannot join: Lobby scene '{lobbySceneName}' is missing from Build Settings.");
                    return;
                }

                PreserveNetworkManager(manager);
                _pendingClientStartAfterLobbyLoad = true;
                SetStatus($"Loading lobby, then joining {GetConfiguredAddress(defaultAddress)}:{GetConfiguredPort(defaultPort)}...");
                SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
                return;
            }

            ConfigureTransportForClient(manager);
            bool started = manager.StartClient();
            SetStatus(started
                ? $"Joining {GetConfiguredAddress(defaultAddress)}:{GetConfiguredPort(defaultPort)}..."
                : "Join failed: transport or NetworkManager refused StartClient.");
            Debug.Log($"BootNetworkButtons.StartClient started={started}");
            if (!started)
            {
                return;
            }

            // When NGO scene management is disabled, clients must load Lobby locally.
            if (!manager.NetworkConfig.EnableSceneManagement)
            {
                SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                SetStatus($"Scene '{scene.name}' loaded, but NetworkManager was missing.");
                return;
            }

            PreserveNetworkManager(manager);
            RegisterNetworkCallbacks(manager);

            if (_pendingHostStartAfterLobbyLoad && scene.name == lobbySceneName)
            {
                _pendingHostStartAfterLobbyLoad = false;
                StartHostNow(manager);
            }

            if (_pendingClientStartAfterLobbyLoad && scene.name == lobbySceneName)
            {
                _pendingClientStartAfterLobbyLoad = false;
                ConfigureTransportForClient(manager);
                bool started = manager.StartClient();
                SetStatus(started
                    ? $"Joining {GetConfiguredAddress(defaultAddress)}:{GetConfiguredPort(defaultPort)}..."
                    : "Join failed after lobby load: transport or NetworkManager refused StartClient.");
                Debug.Log($"BootNetworkButtons.StartClient (after Lobby load) started={started}");
            }
        }

        private void StartHostNow(NetworkManager manager)
        {
            PreserveNetworkManager(manager);
            ConfigureTransportForHost(manager);
            bool started = manager.StartHost();
            SetStatus(started
                ? $"Hosting lobby on port {GetConfiguredPort(defaultPort)}."
                : "Host failed: transport or NetworkManager refused StartHost.");
            Debug.Log($"BootNetworkButtons.StartHostLobby started={started}");
        }

        // Kept for backwards compatibility with any old button wiring.
        // Start Match should be triggered from lobby interactable UI, but this still works for host-only fallback.
        public void StartMatchFromLobby()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                SetStatus("Cannot start match: NetworkManager is missing.");
                Debug.LogWarning("BootNetworkButtons.StartMatchFromLobby ignored because NetworkManager is missing.");
                return;
            }

            if (!manager.IsHost)
            {
                SetStatus("Only the host can start the match.");
                Debug.LogWarning("BootNetworkButtons.StartMatchFromLobby ignored because local peer is not host.");
                return;
            }

            string selectedMap = string.IsNullOrWhiteSpace(BootSessionConfig.SelectedMapScene)
                ? fallbackGameplaySceneName
                : BootSessionConfig.SelectedMapScene;

            LoadSceneIfHost(manager, selectedMap);
        }

        public void Shutdown()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                SetStatus("Shutdown ignored: NetworkManager is missing.");
                return;
            }

            SetStatus("Network session shut down.");
            manager.Shutdown();
        }

        public static string GetConfiguredAddress(string fallback = "127.0.0.1")
        {
            string value = PlayerPrefs.GetString(AddressPrefsKey, fallback);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static ushort GetConfiguredPort(ushort fallback = 7777)
        {
            int stored = PlayerPrefs.GetInt(PortPrefsKey, fallback);
            return (ushort)Mathf.Clamp(stored, 1, 65535);
        }

        public static void SetConfiguredAddress(string address)
        {
            PlayerPrefs.SetString(AddressPrefsKey, string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim());
        }

        public static void SetConfiguredPort(ushort port)
        {
            PlayerPrefs.SetInt(PortPrefsKey, port);
        }

        private void EnsureBootCursorUnlocked()
        {
            if (SceneManager.GetActiveScene().name != "Boot")
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LoadLobbySceneIfHost(NetworkManager manager)
        {
            if (!manager.IsHost)
            {
                return;
            }

            if (SceneManager.GetActiveScene().name == lobbySceneName)
            {
                SetStatus("Hosting lobby.");
                return;
            }

            PreserveNetworkManager(manager);
            LoadSceneIfHost(manager, lobbySceneName);
        }

        private static void LoadSceneIfHost(NetworkManager manager, string sceneName)
        {
            if (!manager.IsHost)
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"BootNetworkButtons: scene '{sceneName}' is not in Build Settings.");
                return;
            }

            if (!manager.NetworkConfig.EnableSceneManagement)
            {
                Debug.Log($"BootNetworkButtons: loading scene '{sceneName}' locally.");
                SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                return;
            }

            Debug.Log($"BootNetworkButtons: loading scene '{sceneName}' through NGO scene management.");
            manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        private bool TryResolveNetworkManager(out NetworkManager manager)
        {
            manager = networkManager != null ? networkManager : NetworkManager.Singleton;
            if (manager != null)
            {
                networkManager = manager;
                return true;
            }

            Debug.LogError("BootNetworkButtons: NetworkManager reference is missing.");
            return false;
        }

        private void ConfigureTransportForHost(NetworkManager manager)
        {
            string listenAddress = "0.0.0.0";
            string advertisedAddress = GetConfiguredAddress(defaultAddress);
            ushort port = GetConfiguredPort(defaultPort);
            ConfigureUnityTransport(manager, advertisedAddress, port, listenAddress);
        }

        private void ConfigureTransportForClient(NetworkManager manager)
        {
            string address = GetConfiguredAddress(defaultAddress);
            ushort port = GetConfiguredPort(defaultPort);
            ConfigureUnityTransport(manager, address, port, null);
        }

        private void ConfigureUnityTransport(NetworkManager manager, string address, ushort port, string listenAddress)
        {
            if (manager == null)
            {
                return;
            }

            // Reflection keeps this resilient across Unity Transport API signature changes.
            Component transport = manager.GetComponent("UnityTransport");
            if (transport == null)
            {
                Debug.LogWarning("BootNetworkButtons: UnityTransport component not found.");
                return;
            }

            Type transportType = transport.GetType();
            MethodInfo method = transportType.GetMethod("SetConnectionData", new[] { typeof(string), typeof(ushort), typeof(string) });
            if (method != null)
            {
                method.Invoke(transport, new object[] { address, port, string.IsNullOrWhiteSpace(listenAddress) ? null : listenAddress });
                return;
            }

            MethodInfo legacyMethod = transportType.GetMethod("SetConnectionData", new[] { typeof(string), typeof(ushort) });
            if (legacyMethod != null)
            {
                legacyMethod.Invoke(transport, new object[] { address, port });
                return;
            }

            Debug.LogWarning("BootNetworkButtons: UnityTransport.SetConnectionData not found.");
        }

        private void PreserveNetworkManager(NetworkManager manager)
        {
            if (manager == null)
            {
                return;
            }

            DontDestroyOnLoad(manager.gameObject);
            networkManager = manager;
        }

        private void RegisterNetworkCallbacks(NetworkManager manager)
        {
            if (manager == null)
            {
                return;
            }

            manager.OnClientConnectedCallback -= HandleClientConnected;
            manager.OnClientDisconnectCallback -= HandleClientDisconnected;
            manager.OnClientConnectedCallback += HandleClientConnected;
            manager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            SetStatus(IsHost
                ? $"Client {clientId} connected."
                : $"Connected to host as client {clientId}.");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            SetStatus($"Client {clientId} disconnected.");
        }

        private void SetStatus(string message)
        {
            _statusMessage = string.IsNullOrWhiteSpace(message) ? "Ready." : message;
            Debug.Log("BootNetworkButtons: " + _statusMessage);
        }
    }
}
