// File: Assets/_Project/Gameplay/Lobby/LobbySceneInstaller.cs
using System.Collections.Generic;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Lobby
{
    // This installer keeps Lobby as a dedicated 3D pre-match environment.
    // It creates walkable geometry, spawn anchors, a mirror wall, and in-world consoles for match control and customization.
    [DefaultExecutionOrder(-900)]
    public sealed class LobbySceneInstaller : MonoBehaviour
    {
        private const string LobbySceneName = "Lobby";
        private const string RuntimeRootName = "_LobbyRuntime";
        private const string SpawnPrefix = "LobbySpawn_";

        private bool _serverInstalled;
        private float _nextLobbyRepositionTime;

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

            GameObject root = EnsureRuntimeRoot();
            EnsureLobbyGeometry(root.transform);
            EnsureLobbySpawnPoints(root.transform);
            EnsureMirrorDisplay(root.transform);
            RuntimePaintInstaller.EnsureInstalledForScene(root.transform, false);
            TryInstallServerLobbyRuntime(root.transform);
            RepositionAllAvatarsToLobbySpawns();
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

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && Time.time >= _nextLobbyRepositionTime)
            {
                _nextLobbyRepositionTime = Time.time + 1.0f;
                RepositionMissingAvatarsToLobbySpawns();
            }
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

        private static GameObject EnsureRuntimeRoot()
        {
            GameObject root = GameObject.Find(RuntimeRootName);
            if (root == null)
            {
                root = new GameObject(RuntimeRootName);
            }

            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return root;
        }


        private static void EnsurePaintableLobbyGeometry(Transform root)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                {
                    continue;
                }

                GameObject target = renderer.gameObject;
                Collider colliderRef = target.GetComponent<Collider>();
                if (colliderRef == null)
                {
                    colliderRef = target.GetComponentInParent<Collider>();
                }

                if (colliderRef == null || colliderRef.isTrigger)
                {
                    continue;
                }

                if (target.GetComponentInParent<NetworkPlayerAvatar>() != null)
                {
                    continue;
                }

                StainReceiver receiver = target.GetComponent<StainReceiver>();
                if (receiver == null)
                {
                    receiver = target.AddComponent<StainReceiver>();
                }

                receiver.ConfigureTargetRenderer(renderer);
            }
        }

        private static void EnsureLobbyHudController(Transform root)
        {
            UI.Lobby.LobbyHudController hud = FindFirstObjectByType<UI.Lobby.LobbyHudController>();
            if (hud != null)
            {
                return;
            }

            GameObject go = new GameObject("LobbyHudController");
            go.transform.SetParent(root, false);
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<UI.Lobby.LobbyHudController>();
            networkObject.Spawn(true);
        }

        public static List<Transform> CollectLobbySpawnPoints()
        {
            List<Transform> spawns = new List<Transform>();
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

            GameObject stateObject = new GameObject("LobbyState_Main");
            stateObject.transform.SetParent(root, false);
            NetworkObject networkObject = stateObject.AddComponent<NetworkObject>();
            stateObject.AddComponent<NetworkLobbyState>();
            networkObject.Spawn(true);
        }

        private static void EnsureLobbyInteractables(Transform root)
        {
            EnsureConsole(root, "LobbyConsole_MatchControl", new Vector3(-5.5f, 1.0f, 6.5f), typeof(LobbyMatchControlInteractable));
            EnsureConsole(root, "LobbyConsole_Customization", new Vector3(5.5f, 1.0f, 6.5f), typeof(LobbyCustomizationInteractable));
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
            if (renderer != null)
            {
                renderer.material.color = interactableType == typeof(LobbyMatchControlInteractable)
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
            EnsurePrimitive(root, "LobbyFloor", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f), new Vector3(38f, 1f, 38f), new Color(0.09f, 0.11f, 0.15f));
            EnsurePrimitive(root, "LobbyCeiling", PrimitiveType.Cube, new Vector3(0f, 4.5f, 0f), new Vector3(38f, 1f, 38f), new Color(0.12f, 0.15f, 0.18f));
            EnsurePrimitive(root, "LobbyWall_N", PrimitiveType.Cube, new Vector3(0f, 2f, 19.5f), new Vector3(39f, 4f, 1f), new Color(0.16f, 0.20f, 0.24f));
            EnsurePrimitive(root, "LobbyWall_S", PrimitiveType.Cube, new Vector3(0f, 2f, -19.5f), new Vector3(39f, 4f, 1f), new Color(0.16f, 0.20f, 0.24f));
            EnsurePrimitive(root, "LobbyWall_E", PrimitiveType.Cube, new Vector3(19.5f, 2f, 0f), new Vector3(1f, 4f, 39f), new Color(0.16f, 0.20f, 0.24f));
            EnsurePrimitive(root, "LobbyWall_W", PrimitiveType.Cube, new Vector3(-19.5f, 2f, 0f), new Vector3(1f, 4f, 39f), new Color(0.16f, 0.20f, 0.24f));
            EnsurePrimitive(root, "LobbyCatwalk", PrimitiveType.Cube, new Vector3(0f, 0.15f, 8f), new Vector3(14f, 0.3f, 4f), new Color(0.2f, 0.23f, 0.27f));
            EnsurePrimitive(root, "LobbyPlatform", PrimitiveType.Cube, new Vector3(0f, 0.35f, -8f), new Vector3(10f, 0.7f, 10f), new Color(0.18f, 0.21f, 0.25f));
        }

        private static void EnsureMirrorDisplay(Transform root)
        {
            GameObject mirrorBack = FindOrCreate("LobbyMirrorBack", PrimitiveType.Cube, root);
            mirrorBack.transform.position = new Vector3(0f, 2.0f, 17.8f);
            mirrorBack.transform.localScale = new Vector3(9f, 4f, 0.25f);
            ApplyMaterial(mirrorBack, new Color(0.05f, 0.06f, 0.08f), false);

            GameObject mirrorSurface = FindOrCreate("LobbyMirrorSurface", PrimitiveType.Quad, root);
            mirrorSurface.transform.position = new Vector3(0f, 2.0f, 17.64f);
            mirrorSurface.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            mirrorSurface.transform.localScale = new Vector3(7f, 3f, 1f);
            if (mirrorSurface.GetComponent<LobbyMirrorSurface>() == null)
            {
                mirrorSurface.AddComponent<LobbyMirrorSurface>();
            }
        }

        private static void EnsureLobbySpawnPoints(Transform root)
        {
            Vector3[] points =
            {
                new Vector3(-12f, 0.75f, -12f),
                new Vector3(-12f, 0.75f, 12f),
                new Vector3(12f, 0.75f, -12f),
                new Vector3(12f, 0.75f, 12f),
                new Vector3(0f, 0.75f, -14f),
                new Vector3(0f, 0.75f, 14f),
                new Vector3(-14f, 0.75f, 0f),
                new Vector3(14f, 0.75f, 0f)
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

        private static void RepositionMissingAvatarsToLobbySpawns()
        {
            List<Transform> spawns = CollectLobbySpawnPoints();
            if (spawns.Count == 0)
            {
                return;
            }

            NetworkPlayerAuthoritativeMover[] movers = Object.FindObjectsByType<NetworkPlayerAuthoritativeMover>(FindObjectsSortMode.None);
            for (int i = 0; i < movers.Length; i++)
            {
                NetworkPlayerAuthoritativeMover mover = movers[i];
                if (mover == null || !mover.IsSpawned)
                {
                    continue;
                }

                if (mover.transform.position.y > -2f)
                {
                    continue;
                }

                Transform spawn = spawns[i % spawns.Count];
                mover.ServerTeleportTo(spawn.position, spawn.rotation.eulerAngles.y);
            }
        }

        private static void RepositionAllAvatarsToLobbySpawns()
        {
            List<Transform> spawns = CollectLobbySpawnPoints();
            if (spawns.Count == 0)
            {
                return;
            }

            NetworkPlayerAuthoritativeMover[] movers = Object.FindObjectsByType<NetworkPlayerAuthoritativeMover>(FindObjectsSortMode.None);
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
            GameObject go = FindOrCreate(name, primitiveType, root);
            go.transform.position = position;
            go.transform.localScale = scale;
            ApplyMaterial(go, color, primitiveType == PrimitiveType.Quad);
        }

        private static GameObject FindOrCreate(string name, PrimitiveType primitiveType, Transform root)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
                go.transform.SetParent(root, false);
            }

            return go;
        }

        private static void ApplyMaterial(GameObject go, Color color, bool disableCollider)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }

            if (disableCollider)
            {
                Collider collider = go.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.Destroy(collider);
                }
            }
        }
    }
}
