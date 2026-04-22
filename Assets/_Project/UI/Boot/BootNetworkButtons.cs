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
    // It also ensures the newer frontend overlay exists even if RuntimeInitialize methods are stripped or reordered.
    public sealed class BootNetworkButtons : MonoBehaviour
    {
        private const string AddressPrefsKey = "HueDoneIt.Network.Address";
        private const string PortPrefsKey = "HueDoneIt.Network.Port";

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private string gameplaySceneName = "Gameplay_Undertint";
        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private string defaultAddress = "127.0.0.1";

        // This value is read by the boot frontend to show current status.
        public bool IsNetworkActive => TryResolveNetworkManager(out NetworkManager manager) && manager.IsListening;

        // This value is read by the boot frontend to decide if Start Match is allowed.
        public bool IsHost => TryResolveNetworkManager(out NetworkManager manager) && manager.IsHost;

        private void Awake()
        {
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
        }

        // Legacy Host button path.
        // This keeps old Boot button wiring playable by immediately moving host into gameplay.
        public void StartHost()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                return;
            }

            if (!manager.IsListening)
            {
                ConfigureTransportForHost(manager);
                bool started = manager.StartHost();
                Debug.Log($"BootNetworkButtons.StartHost started={started}");
                if (!started)
                {
                    return;
                }
            }

            // Legacy behavior is immediate scene transition so Host is never a dead-end path.
            LoadGameplaySceneIfHost(manager);
        }

        // New frontend path.
        // This hosts the network session but intentionally stays in Boot so lobby UI remains visible.
        public void StartHostLobby()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                return;
            }

            if (manager.IsListening)
            {
                return;
            }

            ConfigureTransportForHost(manager);
            bool started = manager.StartHost();
            Debug.Log($"BootNetworkButtons.StartHostLobby started={started}");

            // Keep Boot cursor free so the lobby Start Match button stays clickable.
            EnsureBootCursorUnlocked();
        }

        // Legacy client button and new frontend join both use this.
        public void StartClient()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                return;
            }

            if (manager.IsListening)
            {
                return;
            }

            ConfigureTransportForClient(manager);
            bool started = manager.StartClient();
            Debug.Log($"BootNetworkButtons.StartClient started={started}");
        }

        // New frontend Start Match button uses this.
        // It requires host authority and intentionally transitions to gameplay.
        public void StartMatchFromLobby()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager) || !manager.IsHost)
            {
                Debug.LogWarning("BootNetworkButtons.StartMatchFromLobby ignored because local peer is not host.");
                return;
            }

            LoadGameplaySceneIfHost(manager);
        }

        public void Shutdown()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                return;
            }

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

        private void LoadGameplaySceneIfHost(NetworkManager manager)
        {
            if (!manager.IsHost)
            {
                return;
            }

            if (!manager.NetworkConfig.EnableSceneManagement)
            {
                // Fallback for projects where NGO scene management was disabled.
                SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
                return;
            }

            manager.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        }

        private bool TryResolveNetworkManager(out NetworkManager manager)
        {
            manager = networkManager != null ? networkManager : NetworkManager.Singleton;
            if (manager != null)
            {
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
    }
}
