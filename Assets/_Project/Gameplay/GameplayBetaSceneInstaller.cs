// File: Assets/_Project/Gameplay/GameplayBetaSceneInstaller.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using HueDoneIt.Core.Bootstrap;
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
    [DefaultExecutionOrder(-1000)]
    public sealed class GameplayBetaSceneInstaller : MonoBehaviour
    {
        private const string RuntimeRootName = "_BetaArenaRuntime";
        private const string GameplaySceneName = "Gameplay_Undertint";

        [SerializeField] private bool verboseLogging = true;
        [SerializeField] private bool preserveSceneAuthoredLayout = true;

        private void Awake()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.name, GameplaySceneName, StringComparison.Ordinal))
            {
                // If this component is accidentally present in another scene, do nothing there.
                // Scene setup must keep this installer gameplay-only.
                enabled = false;
                return;
            }

            if (verboseLogging)
            {
                Debug.Log($"GameplayBetaSceneInstaller.Awake scene='{activeScene.name}' object='{name}'");
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

            if (!string.Equals(activeScene.name, GameplaySceneName, StringComparison.Ordinal))
            {
                return;
            }

            if (verboseLogging)
            {
                Debug.Log($"GameplayBetaSceneInstaller.Start scene='{activeScene.name}'");
            }

            GameObject runtimeRoot = EnsureRuntimeRoot();
            bool sceneHasAuthoredLayout = HasAuthoredGameplayLayout();

            // Keep authored scene geometry and spawn anchors stable when they already exist.
            // This prevents runtime repositioning from moving spawns away from valid walkable space.
            if (!preserveSceneAuthoredLayout || !sceneHasAuthoredLayout)
            {
                EnsureArenaLayout(runtimeRoot.transform);
                EnsureSpawnPoints(runtimeRoot.transform);
                EnsureVisualPumpAnchor(runtimeRoot.transform);
                EnsureVisualFloodMarkers(runtimeRoot.transform);
            }
            else if (verboseLogging)
            {
                Debug.Log("GameplayBetaSceneInstaller: preserving authored gameplay layout and spawn anchors.");
            }

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
            EnsureSoloCpuOpponent(runtimeRoot.transform);
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

        private static bool HasAuthoredGameplayLayout()
        {
            // We consider the scene authored when at least one spawn point and the primary gameplay systems exist.
            bool hasSpawn = GameObject.Find("SpawnPoint_01") != null;
            bool hasRound = FindFirstObjectByType<NetworkRoundState>() != null;
            bool hasPump = FindFirstObjectByType<PumpRepairTask>() != null;
            bool hasFlood = FindFirstObjectByType<FloodSequenceController>() != null;
            return hasSpawn && hasRound && hasPump && hasFlood;
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
            // Undertint beta map layout: 6 enclosed spaceship rooms with hallways and an ocean deck perimeter.
            EnsureGround(root, "Undertint_OceanDeck", new Vector3(0f, -1.2f, 0f), new Vector3(56f, 1f, 42f), new Color(0.05f, 0.18f, 0.30f));
            EnsureGround(root, "Undertint_ShipFloor", new Vector3(0f, -0.5f, 0f), new Vector3(46f, 1f, 32f), new Color(0.11f, 0.12f, 0.15f));

            EnsureBlock(root, "ShipHull_N", new Vector3(0f, 2f, 16.25f), new Vector3(46.5f, 4f, 0.5f), new Color(0.18f, 0.20f, 0.23f));
            EnsureBlock(root, "ShipHull_S", new Vector3(0f, 2f, -16.25f), new Vector3(46.5f, 4f, 0.5f), new Color(0.18f, 0.20f, 0.23f));
            EnsureBlock(root, "ShipHull_E", new Vector3(23.25f, 2f, 0f), new Vector3(0.5f, 4f, 32.5f), new Color(0.18f, 0.20f, 0.23f));
            EnsureBlock(root, "ShipHull_W", new Vector3(-23.25f, 2f, 0f), new Vector3(0.5f, 4f, 32.5f), new Color(0.18f, 0.20f, 0.23f));

            // Six enclosed rooms arranged as 3 columns x 2 rows.
            EnsureRoom(root, "Room_01_Engine", new Vector3(-14f, 0f, 8f), new Color(0.20f, 0.24f, 0.30f));
            EnsureRoom(root, "Room_02_Pump", new Vector3(0f, 0f, 8f), new Color(0.22f, 0.22f, 0.30f));
            EnsureRoom(root, "Room_03_Navigation", new Vector3(14f, 0f, 8f), new Color(0.24f, 0.20f, 0.28f));
            EnsureRoom(root, "Room_04_Cargo", new Vector3(-14f, 0f, -8f), new Color(0.20f, 0.28f, 0.22f));
            EnsureRoom(root, "Room_05_Reactor", new Vector3(0f, 0f, -8f), new Color(0.30f, 0.23f, 0.20f));
            EnsureRoom(root, "Room_06_Lab", new Vector3(14f, 0f, -8f), new Color(0.24f, 0.25f, 0.20f));

            // Floodable connecting hallways.
            EnsureBlock(root, "Hall_NorthRow", new Vector3(0f, 0.1f, 8f), new Vector3(26f, 0.2f, 3f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Hall_SouthRow", new Vector3(0f, 0.1f, -8f), new Vector3(26f, 0.2f, 3f), new Color(0.17f, 0.20f, 0.25f));
            EnsureBlock(root, "Hall_Center", new Vector3(0f, 0.1f, 0f), new Vector3(6f, 0.2f, 11f), new Color(0.16f, 0.19f, 0.23f));
            EnsureBlock(root, "Hall_WestConnector", new Vector3(-14f, 0.1f, 0f), new Vector3(6f, 0.2f, 11f), new Color(0.16f, 0.19f, 0.23f));
            EnsureBlock(root, "Hall_EastConnector", new Vector3(14f, 0.1f, 0f), new Vector3(6f, 0.2f, 11f), new Color(0.16f, 0.19f, 0.23f));
        }

        private static void EnsureRoom(Transform root, string roomName, Vector3 center, Color roomColor)
        {
            EnsureBlock(root, roomName + "_Floor", center + new Vector3(0f, 0.1f, 0f), new Vector3(10f, 0.2f, 8f), roomColor);
            EnsureBlock(root, roomName + "_NorthWall", center + new Vector3(0f, 2f, 4f), new Vector3(10f, 4f, 0.35f), roomColor * 0.9f);
            EnsureBlock(root, roomName + "_SouthWall", center + new Vector3(0f, 2f, -4f), new Vector3(10f, 4f, 0.35f), roomColor * 0.9f);
            EnsureBlock(root, roomName + "_WestWall", center + new Vector3(-5f, 2f, 0f), new Vector3(0.35f, 4f, 8f), roomColor * 0.85f);
            EnsureBlock(root, roomName + "_EastWall", center + new Vector3(5f, 2f, 0f), new Vector3(0.35f, 4f, 8f), roomColor * 0.85f);
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
            EnsureStainReceiver(pump);
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
            EnsureStainReceiver(pump.gameObject);
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

        private static void EnsureSoloCpuOpponent(Transform root)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            // CPU count is authored from Boot lobby settings and clamped here for safety.
            int targetCpuCount = Mathf.Clamp(BootSessionConfig.RequestedCpuCount, 0, 7);
            var agents = FindObjectsByType<Players.SimpleCpuOpponentAgent>(FindObjectsSortMode.None);
            int existingCount = agents != null ? agents.Length : 0;

            if (existingCount >= targetCpuCount)
            {
                return;
            }

            GameObject playerPrefab = NetworkManager.Singleton.NetworkConfig != null
                ? NetworkManager.Singleton.NetworkConfig.PlayerPrefab
                : null;

            if (playerPrefab == null)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: cannot spawn CPU because NetworkConfig.PlayerPrefab is missing.");
                return;
            }

            for (int i = existingCount; i < targetCpuCount; i++)
            {
                // Each CPU is a full spawned player prefab with a server-side AI agent attached.
                // This avoids hijacking the local player and keeps participants separate.
                Vector3 spawnPos = new Vector3(2f + (i * 1.4f), 1f, -2f - (i * 0.8f));
                GameObject bot = Instantiate(playerPrefab, spawnPos, Quaternion.identity, root);
                bot.name = $"CPU_Opponent_{i + 1:00}";

                if (bot.GetComponent<Players.SimpleCpuOpponentAgent>() == null)
                {
                    bot.AddComponent<Players.SimpleCpuOpponentAgent>();
                }

                NetworkObject networkObject = bot.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    networkObject = bot.AddComponent<NetworkObject>();
                }

                if (!networkObject.IsSpawned)
                {
                    networkObject.Spawn(true);
                }
            }
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
            EnsureStainReceiver(ground);
        }

        private static GameObject EnsureBlock(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject block = FindOrCreate(name, PrimitiveType.Cube, parent);
            block.transform.position = position;
            block.transform.rotation = Quaternion.identity;
            block.transform.localScale = scale;
            ApplyMaterial(block, color, false);
            EnsureStainReceiver(block);
            return block;
        }

        private static void EnsureCapsule(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject capsule = FindOrCreate(name, PrimitiveType.Capsule, parent);
            capsule.transform.position = position;
            capsule.transform.rotation = Quaternion.identity;
            capsule.transform.localScale = scale;
            ApplyMaterial(capsule, color, false);
            EnsureStainReceiver(capsule);
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

        private static void EnsureStainReceiver(GameObject target)
        {
            if (target == null || target.GetComponent<Collider>() == null)
            {
                return;
            }

            GetOrAddComponent<StainReceiver>(target);
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

            NetworkObject networkObject = gameObject.GetComponent<NetworkObject>();
            if (networkObject != null && !networkObject.IsSpawned)
            {
                networkObject.Spawn(destroyWithScene: true);
            }
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
            Light directional = FindFirstObjectByType<Light>();
            if (directional != null && directional.type == LightType.Directional)
            {
                directional.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                directional.intensity = 1.35f;
            }
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
