// Assets/_Project/UI/Boot/BootNetworkButtons.cs
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.UI.Boot
{
    public sealed class BootNetworkButtons : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;

        public void StartHost()
        {
            if (networkManager == null)
            {
                Debug.LogError("BootNetworkButtons: NetworkManager reference is missing.");
                return;
            }

            networkManager.StartHost();
        }

        public void StartClient()
        {
            if (networkManager == null)
            {
                Debug.LogError("BootNetworkButtons: NetworkManager reference is missing.");
                return;
            }

            networkManager.StartClient();
        }

        public void Shutdown()
        {
            if (networkManager == null)
            {
                Debug.LogError("BootNetworkButtons: NetworkManager reference is missing.");
                return;
            }

            networkManager.Shutdown();
        }
    }
}