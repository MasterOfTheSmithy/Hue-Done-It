// File: Assets/_Project/Gameplay/GameplayBetaSceneInstaller.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay
{
    [DefaultExecutionOrder(-1000)]
    public sealed class GameplayBetaSceneInstaller : MonoBehaviour
    {
        private const string RuntimeRootName = "_BetaArenaRuntime";
        private const string GameplaySceneName = "Gameplay_Undertint";

        [SerializeField] private bool verboseLogging = true;

        private void Awake()
        {
            if (verboseLogging)
            {
                Debug.Log($"GameplayBetaSceneInstaller.Awake scene='{SceneManager.GetActiveScene().name}' object='{name}'");
            }
        }

        private void Start()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                Debug.LogError("GameplayBetaSceneInstaller: active scene is invalid.");
                return;
            }

            if (verboseLogging)
            {
                Debug.Log($"GameplayBetaSceneInstaller.Start scene='{activeScene.name}'");
            }

            if (!string.Equals(activeScene.name, GameplaySceneName, StringComparison.Ordinal))
            {
                Debug.LogWarning($"GameplayBetaSceneInstaller: expected scene '{GameplaySceneName}' but active scene is '{activeScene.name}'.");
                return;
            }

            GameObject runtimeRoot = EnsureRuntimeRoot();
            EnsureArenaLayout(runtimeRoot.transform);
            EnsureSpawnPoints(runtimeRoot.transform);
            EnsureVisualPumpAnchor(runtimeRoot.transform);
            EnsureVisualFloodMarkers(runtimeRoot.transform);
            ConfigureSceneLighting();

            Debug.Log("GameplayBetaSceneInstaller: primitive arena pass complete.");

            if (!IsServerRuntime())
            {
                Debug.Log("GameplayBetaSceneInstaller: not server runtime, skipping authoritative gameplay object creation.");
                return;
            }

            EnsureAuthoritativeGameplaySystems(runtimeRoot.transform);
            EnsurePump(runtimeRoot.transform);
            EnsureFloodZones(runtimeRoot.transform);
            EnsureFloodController(runtimeRoot.transform);
            EnsureOptionalMatchSimulationRunner(runtimeRoot.transform);
            LogMissingCriticalSystems();

            Debug.Log("GameplayBetaSceneInstaller: authoritative gameplay install complete.");
        }

        private static bool IsServerRuntime()
        {
            return NetworkManager.Singleton != null &&
                   NetworkManager.Singleton.IsListening &&
                   NetworkManager.Singleton.IsServer;
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

            EnsureBlock(root, "Wall_01", new Vector3(0f, 2f, 15.25f), new Vector3(30.5f, 4f, 0.5f), new Color(0.18f, 0.20f, 0.23f));
            EnsureBlock(root, "Wall_02", new Vector3(0f, 2f, -15.25f), new Vector3(30.5f, 4f, 0.5f), new Color(0.18f, 0.20f, 0.23f));
            EnsureBlock(root, "Wall_03", new Vector3(15.25f, 2f, 0f), new Vector3(0.5f, 4f, 30.5f), new Color(0.18f, 0.20f, 0.23f));
            EnsureBlock(root, "Wall_04", new Vector3(-15.25f, 2f, 0f), new Vector3(0.5f, 4f, 30.5f), new Color(0.18f, 0.20f, 0.23f));

            EnsureBlock(root, "Platform_01", new Vector3(0f, 0.3f, 0f), new Vector3(9f, 0.6f, 9f), new Color(0.20f, 0.23f, 0.30f));
            EnsureBlock(root, "Platform_02", new Vector3(0f, 0.1f, 10f), new Vector3(22f, 0.2f, 3f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Platform_03", new Vector3(0f, 0.1f, -10f), new Vector3(22f, 0.2f, 3f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Platform_04", new Vector3(10f, 0.1f, 0f), new Vector3(3f, 0.2f, 22f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Platform_05", new Vector3(-10f, 0.1f, 0f), new Vector3(3f, 0.2f, 22f), new Color(0.17f, 0.20f, 0.25f));

            EnsureBlock(root, "Platform_06", new Vector3(0f, 2.1f, 6f), new Vector3(6f, 0.4f, 4f), new Color(0.22f, 0.22f, 0.29f));
            EnsureBlock(root, "Platform_07", new Vector3(0f, 2.1f, -6f), new Vector3(6f, 0.4f, 4f), new Color(0.22f, 0.22f, 0.29f));
            EnsureBlock(root, "Platform_08", new Vector3(-6f, 1.0f, 6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));
            EnsureBlock(root, "Platform_09", new Vector3(6f, 1.0f, 6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));
            EnsureBlock(root, "Platform_10", new Vector3(-6f, 1.0f, -6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));
            EnsureBlock(root, "Platform_11", new Vector3(6f, 1.0f, -6f), new Vector3(2f, 2f, 4f), new Color(0.26f, 0.26f, 0.33f));

            EnsureBlock(root, "Cover_01", new Vector3(-3.5f, 1f, -2f), new Vector3(1f, 2f, 1f), new Color(0.28f, 0.34f, 0.45f));
            EnsureBlock(root, "Cover_02", new Vector3(3.5f, 1f, -2f), new Vector3(1f, 2f, 1f), new Color(0.45f, 0.31f, 0.27f));
            EnsureBlock(root, "Cover_03", new Vector3(-3.5f, 1f, 2f), new Vector3(1f, 2f, 1f), new Color(0.27f, 0.45f, 0.34f));
            EnsureBlock(root, "Cover_04", new Vector3(3.5f, 1f, 2f), new Vector3(1f, 2f, 1f), new Color(0.45f, 0.27f, 0.41f));
            EnsureBlock(root, "Cover_05", new Vector3(-8f, 1f, 0f), new Vector3(1.5f, 2f, 1.5f), new Color(0.24f, 0.33f, 0.50f));
            EnsureBlock(root, "Cover_06", new Vector3(8f, 1f, 0f), new Vector3(1.5f, 2f, 1.5f), new Color(0.50f, 0.33f, 0.24f));

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

                Vector3 lookDirection = (Vector3.zero - points[i]).WithY(0f);
                Quaternion rotation = lookDirection.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                    : Quaternion.identity;

                spawn.transform.SetPositionAndRotation(points[i], rotation);
                spawn.transform.localScale = new Vector3(0.45f, 0.2f, 0.45f);
                ApplyMaterial(spawn, new Color(0.2f, 0.85f, 0.3f), false);

                Collider spawnCollider = spawn.GetComponent<Collider>();
                if (spawnCollider != null)
                {
                    spawnCollider.isTrigger = true;
                }
            }
        }

        private static void EnsureVisualPumpAnchor(Transform root)
        {
            GameObject pump = FindOrCreate("Pump_Main", PrimitiveType.Cube, root);
            pump.transform.position = new Vector3(0f, 1f, 0f);
            pump.transform.rotation = Quaternion.identity;
            pump.transform.localScale = new Vector3(1.6f, 2f, 1.6f);
            ApplyMaterial(pump, new Color(0.95f, 0.85f, 0.15f, 0.8f), false);
        }

        private static void EnsureVisualFloodMarkers(Transform root)
        {
            EnsureMarker(root, "FloodZone_Main", new Vector3(0f, 0.6f, 0f), new Vector3(24f, 1.2f, 24f), new Color(0.10f, 0.35f, 0.95f, 0.22f));
            EnsureMarker(root, "FloodZone_LowArea", new Vector3(0f, -0.2f, -12f), new Vector3(10f, 1.2f, 6f), new Color(0.95f, 0.10f, 0.12f, 0.22f));
        }

        private static void EnsureAuthoritativeGameplaySystems(Transform root)
        {
            EnsureNetworkSystem<NetworkRoundState>(root, "RoundState_Main");
            EnsureNetworkSystem<EliminationManager>(root, "EliminationManager_Main");
            EnsureNetworkSystem<BodyReportManager>(root, "BodyReportManager_Main");
        }

        private static void EnsurePump(Transform root)
        {
            PumpRepairTask pump = FindFirstObjectByType<PumpRepairTask>();
            if (pump == null)
            {
                GameObject pumpObject = CreateAuthoritativePrimitive(root, "Pump_Main", PrimitiveType.Cube, new Vector3(0f, 1f, 0f), new Vector3(1.6f, 2f, 1.6f));
                pump = GetOrAddComponent<PumpRepairTask>(pumpObject);

                SetPrivateField(typeof(NetworkRepairTask), pump, "taskId", "pump-main");
                SetPrivateField(typeof(NetworkRepairTask), pump, "displayName", "Pump Main");
                SetPrivateField(typeof(NetworkRepairTask), pump, "interactPrompt", "Repair Pump");
                SetPrivateField(typeof(NetworkRepairTask), pump, "taskDurationSeconds", 2.25f);
                SetPrivateField(typeof(PumpRepairTask), pump, "maxFailures", 3);
                SetPrivateField(typeof(PumpRepairTask), pump, "confirmationWindowStartNormalized", 0.58f);
                SetPrivateField(typeof(PumpRepairTask), pump, "confirmationWindowEndNormalized", 0.82f);

                TrySpawnNetworkObject(pumpObject);
            }

            pump.gameObject.name = "Pump_Main";
            pump.transform.position = new Vector3(0f, 1f, 0f);

            BoxCollider colliderRef = pump.GetComponent<BoxCollider>();
            if (colliderRef == null)
            {
                colliderRef = pump.gameObject.AddComponent<BoxCollider>();
            }

            colliderRef.isTrigger = false;
            colliderRef.center = Vector3.zero;
            colliderRef.size = new Vector3(1.6f, 2f, 1.6f);
        }

        private static void EnsureFloodZones(Transform root)
        {
            List<FloodZone> zones = new List<FloodZone>(FindObjectsByType<FloodZone>(FindObjectsSortMode.None));

            FloodZone mainZone = FindFloodZoneByName("FloodZone_Main");
            FloodZone lowZone = FindFloodZoneByName("FloodZone_LowArea");

            if (mainZone == null && zones.Count > 0)
            {
                mainZone = zones[0];
            }

            if (lowZone == null)
            {
                foreach (FloodZone zone in zones)
                {
                    if (zone != null && zone != mainZone)
                    {
                        lowZone = zone;
                        break;
                    }
                }
            }

            if (mainZone == null)
            {
                GameObject zoneObject = CreateAuthoritativePrimitive(root, "FloodZone_Main", PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(24f, 1.2f, 24f));
                mainZone = GetOrAddComponent<FloodZone>(zoneObject);
                SetPrivateField(typeof(FloodZone), mainZone, "zoneId", "Flood_Main");
                SetPrivateField(typeof(FloodZone), mainZone, "initialState", FloodZoneState.Wet);
                TrySpawnNetworkObject(zoneObject);
            }

            if (lowZone == null)
            {
                GameObject zoneObject = CreateAuthoritativePrimitive(root, "FloodZone_LowArea", PrimitiveType.Cube, new Vector3(0f, -0.2f, -12f), new Vector3(10f, 1.2f, 6f));
                lowZone = GetOrAddComponent<FloodZone>(zoneObject);
                SetPrivateField(typeof(FloodZone), lowZone, "zoneId", "Flood_LowArea");
                SetPrivateField(typeof(FloodZone), lowZone, "initialState", FloodZoneState.Submerged);
                TrySpawnNetworkObject(zoneObject);
            }

            ConfigureFloodZone(mainZone, "FloodZone_Main", new Vector3(0f, 0.6f, 0f), new Vector3(24f, 1.2f, 24f), new Color(0.10f, 0.35f, 0.95f, 0.22f));
            ConfigureFloodZone(lowZone, "FloodZone_LowArea", new Vector3(0f, -0.2f, -12f), new Vector3(10f, 1.2f, 6f), new Color(0.95f, 0.10f, 0.12f, 0.22f));
        }

        private static void ConfigureFloodZone(FloodZone zone, string objectName, Vector3 position, Vector3 scale, Color color)
        {
            if (zone == null)
            {
                return;
            }

            zone.gameObject.name = objectName;
            zone.transform.position = position;
            zone.transform.rotation = Quaternion.identity;
            zone.transform.localScale = scale;

            BoxCollider colliderRef = zone.GetComponent<BoxCollider>();
            if (colliderRef == null)
            {
                colliderRef = zone.gameObject.AddComponent<BoxCollider>();
            }

            colliderRef.isTrigger = true;
            colliderRef.center = Vector3.zero;
            colliderRef.size = Vector3.one;

            ApplyMaterial(zone.gameObject, color, true);
        }

        private static void EnsureFloodController(Transform root)
        {
            FloodSequenceController controller = FindFirstObjectByType<FloodSequenceController>();
            if (controller == null)
            {
                GameObject controllerObject = CreateAuthoritativeNetworkObject(root, "FloodController_Main", Vector3.zero);
                controller = GetOrAddComponent<FloodSequenceController>(controllerObject);
                TrySpawnNetworkObject(controllerObject);
            }

            ConfigureFloodSequence(controller);
        }

        private static void ConfigureFloodSequence(FloodSequenceController controller)
        {
            if (controller == null)
            {
                return;
            }

            FloodZone mainZone = FindFloodZoneByName("FloodZone_Main");
            FloodZone lowZone = FindFloodZoneByName("FloodZone_LowArea");
            if (mainZone == null || lowZone == null)
            {
                return;
            }

            Type controllerType = typeof(FloodSequenceController);
            FieldInfo sequencesField = controllerType.GetField("sequences", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lockedFloodingDelayField = controllerType.GetField("lockedFloodingDelaySeconds", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lockedSubmergeDelayField = controllerType.GetField("lockedSubmergeDelaySeconds", BindingFlags.Instance | BindingFlags.NonPublic);

            lockedFloodingDelayField?.SetValue(controller, 4f);
            lockedSubmergeDelayField?.SetValue(controller, 6f);

            if (sequencesField == null)
            {
                return;
            }

            var sequences = new List<FloodSequenceController.ZoneSequence>
            {
                new FloodSequenceController.ZoneSequence
                {
                    zone = mainZone,
                    initialDelaySeconds = 8f,
                    loop = false,
                    states = new List<FloodSequenceController.StateDuration>
                    {
                        new FloodSequenceController.StateDuration { state = FloodZoneState.Wet, durationSeconds = 12f },
                        new FloodSequenceController.StateDuration { state = FloodZoneState.Flooding, durationSeconds = 18f },
                        new FloodSequenceController.StateDuration { state = FloodZoneState.Submerged, durationSeconds = 999f }
                    }
                },
                new FloodSequenceController.ZoneSequence
                {
                    zone = lowZone,
                    initialDelaySeconds = 0f,
                    loop = false,
                    states = new List<FloodSequenceController.StateDuration>
                    {
                        new FloodSequenceController.StateDuration { state = FloodZoneState.Submerged, durationSeconds = 999f }
                    }
                }
            };

            sequencesField.SetValue(controller, sequences);
        }

        private static void EnsureOptionalMatchSimulationRunner(Transform root)
        {
            if (!HasCommandLineArg("-simulateMatch"))
            {
                return;
            }

            NetworkMatchSimulationRunner runner = FindFirstObjectByType<NetworkMatchSimulationRunner>();
            if (runner != null)
            {
                return;
            }

            GameObject runnerObject = CreateAuthoritativeNetworkObject(root, "MatchSimulationRunner_Main", Vector3.zero);
            GetOrAddComponent<NetworkMatchSimulationRunner>(runnerObject);
            TrySpawnNetworkObject(runnerObject);
        }

        private static void LogMissingCriticalSystems()
        {
            ValidateRequiredSystem<NetworkRoundState>("NetworkRoundState");
            ValidateRequiredSystem<EliminationManager>("EliminationManager");
            ValidateRequiredSystem<BodyReportManager>("BodyReportManager");
            ValidateRequiredSystem<PumpRepairTask>("PumpRepairTask");
            ValidateRequiredSystem<FloodSequenceController>("FloodSequenceController");

            FloodZone[] zones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            if (zones == null || zones.Length < 2)
            {
                Debug.LogError("GameplayBetaSceneInstaller: Expected at least 2 FloodZone objects after runtime installation.");
            }
        }

        private static void ValidateRequiredSystem<T>(string label) where T : UnityEngine.Object
        {
            if (FindFirstObjectByType<T>() == null)
            {
                Debug.LogError($"GameplayBetaSceneInstaller: Required gameplay system missing after install: {label}");
            }
        }

        private static T EnsureNetworkSystem<T>(Transform root, string objectName) where T : Component
        {
            T existing = FindFirstObjectByType<T>();
            if (existing != null)
            {
                return existing;
            }

            GameObject go = CreateAuthoritativeNetworkObject(root, objectName, Vector3.zero);
            T component = GetOrAddComponent<T>(go);
            TrySpawnNetworkObject(go);
            return component;
        }

        private static GameObject CreateAuthoritativePrimitive(Transform root, string name, PrimitiveType primitiveType, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
            }

            if (go.transform.parent != root)
            {
                go.transform.SetParent(root, true);
            }

            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = scale;

            if (go.GetComponent<NetworkObject>() == null)
            {
                go.AddComponent<NetworkObject>();
            }

            return go;
        }

        private static GameObject CreateAuthoritativeNetworkObject(Transform root, string name, Vector3 position)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = new GameObject(name);
            }

            if (go.transform.parent != root)
            {
                go.transform.SetParent(root, true);
            }

            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            if (go.GetComponent<NetworkObject>() == null)
            {
                go.AddComponent<NetworkObject>();
            }

            return go;
        }

        private static void EnsureGround(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject ground = FindOrCreate(name, PrimitiveType.Cube, parent);
            ground.transform.position = position;
            ground.transform.rotation = Quaternion.identity;
            ground.transform.localScale = scale;
            ApplyMaterial(ground, color, false);
        }

        private static GameObject EnsureBlock(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject block = FindOrCreate(name, PrimitiveType.Cube, parent);
            block.transform.position = position;
            block.transform.rotation = Quaternion.identity;
            block.transform.localScale = scale;
            ApplyMaterial(block, color, false);
            return block;
        }

        private static void EnsureCapsule(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject capsule = FindOrCreate(name, PrimitiveType.Capsule, parent);
            capsule.transform.position = position;
            capsule.transform.rotation = Quaternion.identity;
            capsule.transform.localScale = scale;
            ApplyMaterial(capsule, color, false);
        }

        private static void EnsureMarker(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject marker = FindOrCreate(name, PrimitiveType.Cube, parent);
            marker.transform.position = position;
            marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = scale;
            ApplyMaterial(marker, color, true);

            Collider colliderRef = marker.GetComponent<Collider>();
            if (colliderRef != null)
            {
                colliderRef.isTrigger = true;
            }
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

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component == null)
            {
                component = target.AddComponent<T>();
            }

            return component;
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

        private static void SetPrivateField(Type type, object target, string fieldName, object value)
        {
            if (target == null || type == null)
            {
                return;
            }

            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }

        private static void TrySpawnNetworkObject(GameObject gameObject)
        {
            if (gameObject == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }
        }

            NetworkObject networkObject = gameObject.GetComponent<NetworkObject>();
            if (networkObject != null && !networkObject.IsSpawned)
            {
                networkObject.Spawn(destroyWithScene: true);
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
        }

        private static GameObject EnsureBlock(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject block = FindOrCreate(name, PrimitiveType.Cube, parent);
            block.transform.position = position;
            block.transform.rotation = Quaternion.identity;
            block.transform.localScale = scale;
            ApplyMaterial(block, color, transparent: false);
            return block;
        }

        private static void ApplyMaterial(GameObject target, Color color, bool transparent)
        {
            if (target == null)
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
            }

            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return;
            }

            Material material = new Material(shader);
            material.color = color;

            if (transparent && material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
        }

        private static void ConfigureSceneLighting()
        {
            FloodZone[] zones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            foreach (FloodZone zone in zones)
            {
                directional.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                directional.intensity = 1.35f;
            }

            return null;
        }

        private static bool HasCommandLineArg(string arg)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], arg, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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