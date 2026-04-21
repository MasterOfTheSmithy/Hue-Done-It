// File: Assets/_Project/UI/Boot/BootNetworkButtons.cs
using System;
using System.Reflection;
using HueDoneIt.Core.Bootstrap;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Boot
{
    public sealed class BootNetworkButtons : MonoBehaviour
    {
        private const string AddressPrefsKey = "HueDoneIt.Network.Address";
        private const string PortPrefsKey = "HueDoneIt.Network.Port";

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private string gameplaySceneName = "Gameplay_Undertint";
        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private string defaultAddress = "127.0.0.1";

        public bool IsNetworkActive => TryResolveNetworkManager(out NetworkManager manager) && manager.IsListening;
        public bool IsHost => TryResolveNetworkManager(out NetworkManager manager) && manager.IsHost;

        public void StartHost()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager))
            {
                return;
            }

            if (manager.IsListening)
            {
                LoadGameplaySceneIfHost(manager);
                return;
            }

            ConfigureTransportForHost(manager);
            bool started = manager.StartHost();
            Debug.Log($"NetworkManager.StartHost() returned: {started}");
            if (started)
            {
                RuntimeGameSettings.Apply();
            }
        }

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
            Debug.Log($"NetworkManager.StartClient() returned: {started}");
        }

        public void StartMatchFromLobby()
        {
            if (!TryResolveNetworkManager(out NetworkManager manager) || !manager.IsHost)
            {
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

        private void LoadGameplaySceneIfHost(NetworkManager manager)
        {
            if (!manager.NetworkConfig.EnableSceneManagement)
            {
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
