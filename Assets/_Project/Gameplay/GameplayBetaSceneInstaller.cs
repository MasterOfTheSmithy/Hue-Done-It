using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay
{
    public sealed class GameplayBetaSceneInstaller : MonoBehaviour
    {
        private const string RuntimeRootName = "_BetaArenaRuntime";
        private const string GameplaySceneName = "Gameplay_Undertint";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.name != GameplaySceneName)
            {
                return;
            }

            if (FindFirstObjectByType<GameplayBetaSceneInstaller>() != null)
            {
                return;
            }

            GameObject installerObject = new(nameof(GameplayBetaSceneInstaller));
            installerObject.hideFlags = HideFlags.DontSave;
            installerObject.AddComponent<GameplayBetaSceneInstaller>();
        }

        private void Start()
        {
            if (SceneManager.GetActiveScene().name != GameplaySceneName)
            {
                return;
            }

            GameObject root = EnsureRuntimeRoot();
            EnsureArenaLayout(root.transform);
            EnsureSpawnPoints(root.transform);
            EnsureNetworkGameplaySystems(root.transform);
            EnsurePump(root.transform);
            EnsureFloodZones(root.transform);
            EnsureFloodController(root.transform);
            ConfigureSceneLighting();
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

        private static void EnsureArenaLayout(Transform root)
        {
            EnsureGround(root, "Platform_Arena", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), new Color(0.10f, 0.11f, 0.14f));

            // Boundary walls.
            EnsureBlock(root, "Wall_North", new Vector3(0f, 2f, 15.25f), new Vector3(30.5f, 4f, 0.5f), new Color(0.18f, 0.2f, 0.23f));
            EnsureBlock(root, "Wall_South", new Vector3(0f, 2f, -15.25f), new Vector3(30.5f, 4f, 0.5f), new Color(0.18f, 0.2f, 0.23f));
            EnsureBlock(root, "Wall_East", new Vector3(15.25f, 2f, 0f), new Vector3(0.5f, 4f, 30.5f), new Color(0.18f, 0.2f, 0.23f));
            EnsureBlock(root, "Wall_West", new Vector3(-15.25f, 2f, 0f), new Vector3(0.5f, 4f, 30.5f), new Color(0.18f, 0.2f, 0.23f));

            // Central arena and lanes.
            EnsureBlock(root, "Platform_Center", new Vector3(0f, 0.3f, 0f), new Vector3(9f, 0.6f, 9f), new Color(0.20f, 0.23f, 0.30f));
            EnsureBlock(root, "Platform_Lane_North", new Vector3(0f, 0.1f, 10f), new Vector3(22f, 0.2f, 3f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Platform_Lane_South", new Vector3(0f, 0.1f, -10f), new Vector3(22f, 0.2f, 3f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Platform_Lane_East", new Vector3(10f, 0.1f, 0f), new Vector3(3f, 0.2f, 22f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Platform_Lane_West", new Vector3(-10f, 0.1f, 0f), new Vector3(3f, 0.2f, 22f), new Color(0.17f, 0.20f, 0.25f));

            // Elevation and traversal routes.
            EnsureBlock(root, "Platform_High_North", new Vector3(0f, 2.1f, 6f), new Vector3(6f, 0.4f, 4f), new Color(0.22f, 0.22f, 0.29f));
            EnsureBlock(root, "Platform_High_South", new Vector3(0f, 2.1f, -6f), new Vector3(6f, 0.4f, 4f), new Color(0.22f, 0.22f, 0.29f));
            EnsureBlock(root, "Platform_Ramp_NW", new Vector3(-6f, 1.0f, 6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));
            EnsureBlock(root, "Platform_Ramp_NE", new Vector3(6f, 1.0f, 6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));
            EnsureBlock(root, "Platform_Ramp_SW", new Vector3(-6f, 1.0f, -6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));
            EnsureBlock(root, "Platform_Ramp_SE", new Vector3(6f, 1.0f, -6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));
            EnsureBlock(root, "Platform_JumpGap_A", new Vector3(-1.8f, 1.2f, 0f), new Vector3(1.5f, 0.3f, 3f), new Color(0.25f, 0.25f, 0.33f));
            EnsureBlock(root, "Platform_JumpGap_B", new Vector3(1.8f, 1.2f, 0f), new Vector3(1.5f, 0.3f, 3f), new Color(0.25f, 0.25f, 0.33f));

            // Cover and line-of-sight blockers.
            EnsureBlock(root, "Cover_01", new Vector3(-3.5f, 1f, -2f), new Vector3(1f, 2f, 1f), new Color(0.28f, 0.34f, 0.45f));
            EnsureBlock(root, "Cover_02", new Vector3(3.5f, 1f, -2f), new Vector3(1f, 2f, 1f), new Color(0.45f, 0.31f, 0.27f));
            EnsureBlock(root, "Cover_03", new Vector3(-3.5f, 1f, 2f), new Vector3(1f, 2f, 1f), new Color(0.27f, 0.45f, 0.34f));
            EnsureBlock(root, "Cover_04", new Vector3(3.5f, 1f, 2f), new Vector3(1f, 2f, 1f), new Color(0.45f, 0.27f, 0.41f));
            EnsureBlock(root, "Cover_05", new Vector3(-8f, 1f, 0f), new Vector3(1.5f, 2f, 1.5f), new Color(0.24f, 0.33f, 0.50f));
            EnsureBlock(root, "Cover_06", new Vector3(8f, 1f, 0f), new Vector3(1.5f, 2f, 1.5f), new Color(0.50f, 0.33f, 0.24f));

            // Alternate routes and secrets.
            EnsureBlock(root, "Tunnel_01", new Vector3(-12f, 0.8f, 8f), new Vector3(5f, 1.6f, 2f), new Color(0.55f, 0.25f, 0.72f));
            EnsureBlock(root, "Tunnel_02", new Vector3(12f, 0.8f, -8f), new Vector3(5f, 1.6f, 2f), new Color(0.55f, 0.25f, 0.72f));
            EnsureCapsule(root, "EasterEgg_01", new Vector3(0f, 0.8f, 12f), new Vector3(1f, 1.6f, 1f), new Color(1f, 0.25f, 0.78f));
        }

        private static void EnsureSpawnPoints(Transform root)
        {
            Vector3[] points =
            {
                new(-11f, 0.6f, -11f),
                new(-11f, 0.6f, 11f),
                new(11f, 0.6f, -11f),
                new(11f, 0.6f, 11f),
                new(0f, 0.6f, -13f),
                new(0f, 0.6f, 13f),
                new(-13f, 0.6f, 0f),
                new(13f, 0.6f, 0f)
            };

            for (int i = 0; i < points.Length; i++)
            {
                string name = $"SpawnPoint_{i + 1:00}";
                GameObject spawn = FindOrCreate(name, PrimitiveType.Cube, root);
                spawn.transform.SetPositionAndRotation(points[i], Quaternion.LookRotation((Vector3.zero - points[i]).WithY(0f).normalized, Vector3.up));
                spawn.transform.localScale = new Vector3(0.45f, 0.2f, 0.45f);
                ApplyMaterial(spawn, new Color(0.2f, 0.85f, 0.3f), transparent: false);

                Collider spawnCollider = spawn.GetComponent<Collider>();
                if (spawnCollider != null)
                {
                    spawnCollider.isTrigger = true;
                }
            }
        }

        private static void EnsureNetworkGameplaySystems(Transform root)
        {
            EnsureNetworkManagerObject<NetworkRoundState>(root, "RoundState_Main");
            EnsureNetworkManagerObject<EliminationManager>(root, "EliminationManager_Main");
            EnsureNetworkManagerObject<BodyReportManager>(root, "BodyReportManager_Main");
        }

        private static void EnsurePump(Transform root)
        {
            PumpRepairTask pump = FindFirstObjectByType<PumpRepairTask>();
            if (pump == null)
            {
                GameObject pumpObject = FindOrCreate("Pump_Main", PrimitiveType.Cube, root);
                pumpObject.transform.position = new Vector3(0f, 1f, 0f);
                pumpObject.transform.localScale = new Vector3(1.6f, 2f, 1.6f);
                if (pumpObject.GetComponent<NetworkObject>() == null)
                {
                    pumpObject.AddComponent<NetworkObject>();
                }

                pump = pumpObject.GetComponent<PumpRepairTask>();
                if (pump == null)
                {
                    pump = pumpObject.AddComponent<PumpRepairTask>();
                }
                TrySpawnNetworkObject(pumpObject);
            }
            else
            {
                pump.gameObject.name = "Pump_Main";
            }

            pump.transform.position = new Vector3(0f, 1f, 0f);
            ApplyMaterial(pump.gameObject, new Color(0.95f, 0.85f, 0.15f), transparent: false);

            if (pump.GetComponent<Collider>() == null)
            {
                BoxCollider colliderRef = pump.gameObject.AddComponent<BoxCollider>();
                colliderRef.size = new Vector3(1.6f, 2f, 1.6f);
            }
        }

        private static void EnsureFloodZones(Transform root)
        {
            EnsureFloodZone(root, "FloodZone_Main", "Flood_Main", FloodZoneState.Wet, new Vector3(0f, 0.6f, 0f), new Vector3(24f, 1.2f, 24f), new Color(0.1f, 0.35f, 0.95f, 0.32f));
            EnsureFloodZone(root, "FloodZone_LowArea", "Flood_LowArea", FloodZoneState.Submerged, new Vector3(0f, -0.2f, -12f), new Vector3(10f, 1.2f, 6f), new Color(0.95f, 0.1f, 0.12f, 0.32f));
        }

        private static void EnsureFloodZone(Transform root, string objectName, string zoneId, FloodZoneState initialState, Vector3 position, Vector3 scale, Color color)
        {
            FloodZone zone = FindFloodZoneByName(objectName);
            if (zone == null)
            {
                GameObject zoneObject = FindOrCreate(objectName, PrimitiveType.Cube, root);
                zoneObject.transform.position = position;
                zoneObject.transform.localScale = scale;

                NetworkObject networkObject = zoneObject.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    networkObject = zoneObject.AddComponent<NetworkObject>();
                }

                zone = zoneObject.GetComponent<FloodZone>();
                if (zone == null)
                {
                    zone = zoneObject.AddComponent<FloodZone>();
                }

                ApplyFloodZoneSerializedDefaults(zone, zoneId, initialState);
                TrySpawnNetworkObject(zoneObject);
            }
            else
            {
                zone.gameObject.name = objectName;
            }

            zone.transform.position = position;
            zone.transform.localScale = scale;

            BoxCollider colliderRef = zone.GetComponent<BoxCollider>();
            if (colliderRef == null)
            {
                colliderRef = zone.gameObject.AddComponent<BoxCollider>();
            }

            colliderRef.isTrigger = true;
            colliderRef.size = Vector3.one;
            colliderRef.center = Vector3.zero;

            ApplyMaterial(zone.gameObject, color, transparent: true);
        }

        private static void EnsureFloodController(Transform root)
        {
            FloodSequenceController controller = FindFirstObjectByType<FloodSequenceController>();
            if (controller == null)
            {
                GameObject controllerObject = new("FloodController_Main");
                controllerObject.transform.SetParent(root, false);
                controllerObject.AddComponent<NetworkObject>();
                controller = controllerObject.AddComponent<FloodSequenceController>();
                TrySpawnNetworkObject(controllerObject);
            }
        }

        private static T EnsureNetworkManagerObject<T>(Transform root, string objectName) where T : Component
        {
            T existing = FindFirstObjectByType<T>();
            if (existing != null)
            {
                return existing;
            }

            GameObject go = new(objectName);
            go.transform.SetParent(root, false);
            go.AddComponent<NetworkObject>();
            T component = go.AddComponent<T>();
            TrySpawnNetworkObject(go);
            return component;
        }

        private static void EnsureGround(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject ground = FindOrCreate(name, PrimitiveType.Cube, parent);
            ground.transform.position = position;
            ground.transform.localScale = scale;
            ApplyMaterial(ground, color, transparent: false);
            EnsureStainReceiver(ground);
        }

        private static GameObject EnsureBlock(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject block = FindOrCreate(name, PrimitiveType.Cube, parent);
            block.transform.position = position;
            block.transform.rotation = Quaternion.identity;
            block.transform.localScale = scale;
            ApplyMaterial(block, color, transparent: false);
            EnsureStainReceiver(block);
            return block;
        }

        private static void EnsureCapsule(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject capsule = FindOrCreate(name, PrimitiveType.Capsule, parent);
            capsule.transform.position = position;
            capsule.transform.rotation = Quaternion.identity;
            capsule.transform.localScale = scale;
            ApplyMaterial(capsule, color, transparent: false);
            EnsureStainReceiver(capsule);
        }

        private static void EnsureStainReceiver(GameObject gameObject)
        {
            if (gameObject == null || gameObject.GetComponent<StainReceiver>() != null)
            {
                return;
            }

            gameObject.AddComponent<StainReceiver>();
        }

        private static GameObject FindOrCreate(string name, PrimitiveType primitiveType, Transform parent)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
            }

            if (go.transform.parent != parent)
            {
                go.transform.SetParent(parent, true);
            }

            return go;
        }

        private static FloodZone FindFloodZoneByName(string objectName)
        {
            FloodZone[] zones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            foreach (FloodZone zone in zones)
            {
                if (zone != null && zone.gameObject.name == objectName)
                {
                    return zone;
                }
            }

            return null;
        }

        private static void TrySpawnNetworkObject(GameObject gameObject)
        {
            if (gameObject == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            NetworkObject networkObject = gameObject.GetComponent<NetworkObject>();
            if (networkObject != null && !networkObject.IsSpawned)
            {
                networkObject.Spawn(destroyWithScene: true);
            }
        }

        private static void ApplyFloodZoneSerializedDefaults(FloodZone zone, string zoneId, FloodZoneState initialState)
        {
            if (zone == null)
            {
                return;
            }

            SerializedFloodZoneAccess.SetZoneId(zone, zoneId);
            SerializedFloodZoneAccess.SetInitialState(zone, initialState);
        }

        private static void ApplyMaterial(GameObject target, Color color, bool transparent)
        {
            if (target == null)
            {
                return;
            }

            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;

            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_AlphaClip", 0f);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
        }

        private static void ConfigureSceneLighting()
        {
            Light directional = FindFirstObjectByType<Light>();
            if (directional != null && directional.type == LightType.Directional)
            {
                directional.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                directional.intensity = 1.35f;
            }
        }

        private static class SerializedFloodZoneAccess
        {
            private static readonly System.Reflection.FieldInfo ZoneIdField =
                typeof(FloodZone).GetField("zoneId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            private static readonly System.Reflection.FieldInfo InitialStateField =
                typeof(FloodZone).GetField("initialState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            public static void SetZoneId(FloodZone zone, string zoneId)
            {
                ZoneIdField?.SetValue(zone, zoneId);
            }

            public static void SetInitialState(FloodZone zone, FloodZoneState state)
            {
                InitialStateField?.SetValue(zone, state);
            }
        }
    }

    internal static class Vector3Extensions
    {
        public static Vector3 WithY(this Vector3 value, float y)
        {
            value.y = y;
            return value;
        }
    }
}
