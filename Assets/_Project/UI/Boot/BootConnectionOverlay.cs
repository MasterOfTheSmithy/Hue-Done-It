// File: Assets/_Project/UI/Boot/BootConnectionOverlay.cs
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.UI.Boot
{
    public sealed class BootConnectionOverlay : MonoBehaviour
    {
        [SerializeField] private Vector2 panelSize = new(300f, 118f);
        [SerializeField] private Vector2 panelMargin = new(16f, 16f);

        private string _address;
        private string _portString;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.name != "Boot")
            {
                return;
            }

            if (FindFirstObjectByType<BootConnectionOverlay>() != null)
            {
                return;
            }

            GameObject go = new(nameof(BootConnectionOverlay));
            DontDestroyOnLoad(go);
            go.AddComponent<BootConnectionOverlay>();
        }

        private void Awake()
        {
            _address = BootNetworkButtons.GetConfiguredAddress();
            _portString = BootNetworkButtons.GetConfiguredPort().ToString();
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name != "Boot")
            {
                enabled = false;
                return;
            }
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != "Boot")
            {
                return;
            }

            Rect rect = new(
                Screen.width - panelSize.x - panelMargin.x,
                panelMargin.y,
                panelSize.x,
                panelSize.y);

            GUILayout.BeginArea(rect, "Connection", GUI.skin.window);
            GUILayout.Label("Server Address");
            _address = GUILayout.TextField(_address ?? string.Empty, 64);

            GUILayout.Space(4f);
            GUILayout.Label("Port");
            _portString = GUILayout.TextField(_portString ?? "7777", 8);

            GUILayout.Space(6f);
            GUILayout.Label("Host shares this address/port. Client uses it to connect.");
            GUILayout.EndArea();

            Apply();
        }

        private void Apply()
        {
            BootNetworkButtons.SetConfiguredAddress(_address);

            if (ushort.TryParse(_portString, out ushort parsed))
            {
                BootNetworkButtons.SetConfiguredPort(parsed);
            }
        }
    }
}
