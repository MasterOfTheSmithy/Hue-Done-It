// File: Assets/_Project/Gameplay/Lobby/LobbySceneInstaller.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Lobby
{
    // This installer keeps Lobby as a dedicated 3D pre-match environment.
    // It creates safe walkable geometry, spawn anchors, and in-world interactable consoles.
    [DefaultExecutionOrder(-900)]
    public sealed class LobbySceneInstaller : MonoBehaviour
    {
        private const string LobbySceneName = "Lobby";
        private const string RuntimeRootName = "_LobbyRuntime";
        private const string SpawnPrefix = "LobbySpawn_";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInLobby()
        {
            if (SceneManager.GetActiveScene().name != LobbySceneName)
            {
                return;
            }

            if (FindFirstObjectByType<LobbySceneInstaller>() != null)
            {
                return;
            }

            new GameObject(nameof(LobbySceneInstaller)).AddComponent<LobbySceneInstaller>();
        }

        private void Start()
        {
            if (SceneManager.GetActiveScene().name != LobbySceneName)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            GameObject root = GameObject.Find(RuntimeRootName);
            if (root == null)
            {
                root = new GameObject(RuntimeRootName);
            }

            EnsureLobbyGeometry(root.transform);
            EnsureLobbySpawnPoints(root.transform);

            TryInstallServerLobbyRuntime(root.transform);
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name != LobbySceneName)
            {
                return;
            }

            GameObject root = GameObject.Find(RuntimeRootName);
            if (root == null)
            {
                return;
            }

            TryInstallServerLobbyRuntime(root.transform);
        }

        private void TryInstallServerLobbyRuntime(Transform root)
        {
            if (_serverInstalled || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            EnsureNetworkLobbyState(root);
            EnsureLobbyInteractables(root);
            EnsureLobbyHudController(root);
            RepositionAllAvatarsToLobbySpawns();
            _serverInstalled = true;
        }

        private static void EnsureLobbyHudController(Transform root)
        {
            UI.Lobby.LobbyHudController hud = FindFirstObjectByType<UI.Lobby.LobbyHudController>();
            if (hud != null)
            {
                return;
            }

            GameObject go = new("LobbyHudController");
            go.transform.SetParent(root, false);
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<UI.Lobby.LobbyHudController>();
            networkObject.Spawn(true);
        }

        public static List<Transform> CollectLobbySpawnPoints()
        {
            List<Transform> spawns = new();
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform transformRef in transforms)
                {
                    if (transformRef != null && transformRef.name.StartsWith(SpawnPrefix))
                    {
                        spawns.Add(transformRef);
                    }
                }
            }

            spawns.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return spawns;
        }

        private static void EnsureNetworkLobbyState(Transform root)
        {
            NetworkLobbyState state = FindFirstObjectByType<NetworkLobbyState>();
            if (state != null)
            {
                return;
            }

            GameObject stateObject = new("LobbyState_Main");
            stateObject.transform.SetParent(root, false);
            NetworkObject networkObject = stateObject.AddComponent<NetworkObject>();
            stateObject.AddComponent<NetworkLobbyState>();
            networkObject.Spawn(true);
        }

        private static void EnsureLobbyInteractables(Transform root)
        {
            EnsureConsole(root, "LobbyConsole_MatchControl", new Vector3(-3f, 1f, 0f), typeof(LobbyMatchControlInteractable));
            EnsureConsole(root, "LobbyConsole_Customization", new Vector3(3f, 1f, 0f), typeof(LobbyCustomizationInteractable));
        }

        private static void EnsureConsole(Transform root, string objectName, Vector3 position, System.Type interactableType)
        {
            GameObject console = GameObject.Find(objectName);
            if (console == null)
            {
                console = GameObject.CreatePrimitive(PrimitiveType.Cube);
                console.name = objectName;
                console.transform.SetParent(root, false);
            }

            console.transform.position = position;
            console.transform.localScale = new Vector3(1.3f, 2f, 1.3f);

            Renderer renderer = console.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.color = interactableType == typeof(LobbyMatchControlInteractable)
                    ? new Color(0.2f, 0.55f, 0.95f)
                    : new Color(0.84f, 0.45f, 0.25f);
            }

            NetworkObject networkObject = console.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = console.AddComponent<NetworkObject>();
            }

            if (console.GetComponent(interactableType) == null)
            {
                console.AddComponent(interactableType);
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !networkObject.IsSpawned)
            {
                networkObject.Spawn(true);
            }
        }

        private static void EnsureLobbyGeometry(Transform root)
        {
            EnsurePrimitive(root, "LobbyFloor", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), new Color(0.09f, 0.11f, 0.15f));
            EnsurePrimitive(root, "LobbyWall_N", PrimitiveType.Cube, new Vector3(0f, 2f, 15.5f), new Vector3(31f, 4f, 1f), new Color(0.16f, 0.20f, 0.24f));
            EnsurePrimitive(root, "LobbyWall_S", PrimitiveType.Cube, new Vector3(0f, 2f, -15.5f), new Vector3(31f, 4f, 1f), new Color(0.16f, 0.20f, 0.24f));
            EnsurePrimitive(root, "LobbyWall_E", PrimitiveType.Cube, new Vector3(15.5f, 2f, 0f), new Vector3(1f, 4f, 31f), new Color(0.16f, 0.20f, 0.24f));
            EnsurePrimitive(root, "LobbyWall_W", PrimitiveType.Cube, new Vector3(-15.5f, 2f, 0f), new Vector3(1f, 4f, 31f), new Color(0.16f, 0.20f, 0.24f));
        }

        private static void EnsureLobbySpawnPoints(Transform root)
        {
            Vector3[] points =
            {
                new(-9f, 0.75f, -9f),
                new(-9f, 0.75f, 9f),
                new(9f, 0.75f, -9f),
                new(9f, 0.75f, 9f),
                new(0f, 0.75f, -10f),
                new(0f, 0.75f, 10f),
                new(-10f, 0.75f, 0f),
                new(10f, 0.75f, 0f)
            };

            for (int i = 0; i < points.Length; i++)
            {
                string name = SpawnPrefix + (i + 1).ToString("00");
                GameObject marker = GameObject.Find(name);
                if (marker == null)
                {
                    marker = new GameObject(name);
                    marker.transform.SetParent(root, false);
                }

                Vector3 lookDirection = (Vector3.zero - points[i]).normalized;
                marker.transform.SetPositionAndRotation(points[i], Quaternion.LookRotation(lookDirection, Vector3.up));
            }
        }

        private static void RepositionAllAvatarsToLobbySpawns()
        {
            List<Transform> spawns = CollectLobbySpawnPoints();
            if (spawns.Count == 0)
            {
                return;
            }

            NetworkPlayerAuthoritativeMover[] movers = FindObjectsByType<NetworkPlayerAuthoritativeMover>(FindObjectsSortMode.None);
            for (int i = 0; i < movers.Length; i++)
            {
                if (movers[i] == null || !movers[i].IsSpawned)
                {
                    continue;
                }

                Transform spawn = spawns[i % spawns.Count];
                movers[i].ServerTeleportTo(spawn.position, spawn.rotation.eulerAngles.y);
            }
        }

        private static void EnsurePrimitive(Transform root, string name, PrimitiveType primitiveType, Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
                go.transform.SetParent(root, false);
            }

            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            go.transform.localScale = scale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.color = color;
            }
        }
    }
}
        private bool _serverInstalled;
