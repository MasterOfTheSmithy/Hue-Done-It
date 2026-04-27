// File: Assets/_Project/Gameplay/GameplayBetaSceneInstaller.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using HueDoneIt.Core.Bootstrap;
using HueDoneIt.Flood;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Director;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Inventory;
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Gameplay.Round;
using HueDoneIt.Gameplay.Sabotage;
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

            // The investor slice must always have a playable fallback map. Authored scene
            // content may exist without the primitive floor/walls after merge/import churn, so
            // repair the entire runtime deck/hall shell every load. FindOrCreate keeps this
            // idempotent and gives clients the same map even before server-only systems spawn.
            EnsureArenaLayout(runtimeRoot.transform);
            EnsureSpawnPoints(runtimeRoot.transform);
            EnsureVisualPumpAnchor(runtimeRoot.transform);
            EnsureVisualFloodMarkers(runtimeRoot.transform);

            if (preserveSceneAuthoredLayout && sceneHasAuthoredLayout && verboseLogging)
            {
                Debug.Log("GameplayBetaSceneInstaller: authored gameplay objects detected; repaired runtime fallback deck/map without skipping core collision.");
            }

            EnsurePaintableSceneGeometry(runtimeRoot.transform);
            ConfigureSceneLighting();
            EnsureAtmosphereLights(runtimeRoot.transform);

            Debug.Log("GameplayBetaSceneInstaller: primitive arena pass complete.");

            if (!IsServerRuntime())
            {
                Debug.Log("GameplayBetaSceneInstaller: not server runtime, skipping authoritative gameplay object creation.");
                return;
            }

            EnsureRoundEventDirector(runtimeRoot.transform);
            EnsureAuthoritativeGameplaySystems(runtimeRoot.transform);
            EnsurePump(runtimeRoot.transform);
            EnsureTaskStations(runtimeRoot.transform);
            EnsureFloodZones(runtimeRoot.transform);
            EnsureGravityPlayZones(runtimeRoot.transform);
            EnsureEnvironmentalHazards(runtimeRoot.transform);
            EnsureAdvancedTaskSet(runtimeRoot.transform);
            EnsureDecontaminationStations(runtimeRoot.transform);
            EnsureFloodgateStations(runtimeRoot.transform);
            EnsureEmergencySealStations(runtimeRoot.transform);
            EnsureSafeRoomBeacons(runtimeRoot.transform);
            EnsurePaintScannerStations(runtimeRoot.transform);
            EnsureVitalsStations(runtimeRoot.transform);
            EnsureEmergencyMeetingConsole(runtimeRoot.transform);
            EnsureVotingPodiums(runtimeRoot.transform);
            EnsureCrewRallyStations(runtimeRoot.transform);
            EnsureBulkheadLockStations(runtimeRoot.transform);
            EnsureCalloutBeacons(runtimeRoot.transform);
            EnsureSlimeLaunchPads(runtimeRoot.transform);
            EnsureBleachVentNetwork(runtimeRoot.transform);
            EnsureSecurityCameraStations(runtimeRoot.transform);
            EnsureAlarmTripwires(runtimeRoot.transform);
            EnsureInkWellStations(runtimeRoot.transform);
            EnsureFalseEvidenceStations(runtimeRoot.transform);
            EnsureSabotageConsoles(runtimeRoot.transform);
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

        private static void EnsureMandatoryWalkableDeck(Transform root)
        {
            // These slabs are deliberately repaired even when the rest of the scene is authored.
            // Without this guard, preserving authored gameplay objects can skip deck generation and
            // leave players spawning into an empty scene with no collision floor.
            EnsureGround(root, "Undertint_OceanDeck", new Vector3(0f, -1.2f, 0f), new Vector3(56f, 1f, 42f), new Color(0.05f, 0.18f, 0.30f));
            EnsureGround(root, "Undertint_SafetyDeck_Collision", new Vector3(0f, -0.12f, 0f), new Vector3(48f, 0.16f, 34f), new Color(0.075f, 0.08f, 0.095f));
            EnsureGround(root, "Undertint_ShipFloor", new Vector3(0f, -0.5f, 0f), new Vector3(46f, 1f, 32f), new Color(0.11f, 0.12f, 0.15f));
            EnsureDeckTileGrid(root);
            EnsureDeckCurbs(root);
        }

        private static void EnsureDeckTileGrid(Transform root)
        {
            const int columns = 6;
            const int rows = 4;
            const float totalWidth = 43.2f;
            const float totalDepth = 29.6f;
            const float gap = 0.14f;
            float tileWidth = (totalWidth / columns) - gap;
            float tileDepth = (totalDepth / rows) - gap;
            Vector3 start = new Vector3(-totalWidth * 0.5f + tileWidth * 0.5f, -0.035f, -totalDepth * 0.5f + tileDepth * 0.5f);

            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    string tileName = $"Undertint_DeckTile_{x:00}_{z:00}";
                    Vector3 position = start + new Vector3(x * (tileWidth + gap), 0f, z * (tileDepth + gap));
                    float value = ((x + z) & 1) == 0 ? 0.13f : 0.105f;
                    EnsureGround(root, tileName, position, new Vector3(tileWidth, 0.07f, tileDepth), new Color(value, value + 0.012f, value + 0.026f));
                }
            }
        }

        private static void EnsureDeckCurbs(Transform root)
        {
            Color curb = new Color(0.20f, 0.22f, 0.26f);
            EnsureBlock(root, "Undertint_DeckCurb_N", new Vector3(0f, 0.28f, 15.9f), new Vector3(46f, 0.55f, 0.28f), curb);
            EnsureBlock(root, "Undertint_DeckCurb_S", new Vector3(0f, 0.28f, -15.9f), new Vector3(46f, 0.55f, 0.28f), curb);
            EnsureBlock(root, "Undertint_DeckCurb_E", new Vector3(22.9f, 0.28f, 0f), new Vector3(0.28f, 0.55f, 32f), curb);
            EnsureBlock(root, "Undertint_DeckCurb_W", new Vector3(-22.9f, 0.28f, 0f), new Vector3(0.28f, 0.55f, 32f), curb);
        }

        private static void EnsureArenaLayout(Transform root)
        {
            // Undertint beta map layout: 6 enclosed spaceship rooms with hallways and an ocean deck perimeter.
            EnsureMandatoryWalkableDeck(root);

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

            // Older generated passes built every room as a sealed collision box. That made CPU routing
            // look broken and caused several task stations to be unreachable. Disable those legacy slabs
            // before installing segmented walls with centered door gaps.
            DisableLegacySolidWall(roomName + "_NorthWall");
            DisableLegacySolidWall(roomName + "_SouthWall");
            DisableLegacySolidWall(roomName + "_WestWall");
            DisableLegacySolidWall(roomName + "_EastWall");

            Color northSouth = roomColor * 0.9f;
            Color eastWest = roomColor * 0.85f;
            EnsureSegmentedXWall(root, roomName + "_NorthDoorWall", center + new Vector3(0f, 2f, 4f), 10f, 0.35f, 2.8f, northSouth);
            EnsureSegmentedXWall(root, roomName + "_SouthDoorWall", center + new Vector3(0f, 2f, -4f), 10f, 0.35f, 2.8f, northSouth);
            EnsureSegmentedZWall(root, roomName + "_WestDoorWall", center + new Vector3(-5f, 2f, 0f), 8f, 0.35f, 2.6f, eastWest);
            EnsureSegmentedZWall(root, roomName + "_EastDoorWall", center + new Vector3(5f, 2f, 0f), 8f, 0.35f, 2.6f, eastWest);
        }

        private static void DisableLegacySolidWall(string objectName)
        {
            GameObject legacy = GameObject.Find(objectName);
            if (legacy == null)
            {
                return;
            }

            Collider collider = legacy.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            Renderer renderer = legacy.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static void EnsureSegmentedXWall(Transform root, string baseName, Vector3 center, float totalWidth, float depth, float doorwayWidth, Color color)
        {
            float segmentWidth = Mathf.Max(0.25f, (totalWidth - doorwayWidth) * 0.5f);
            float offset = (doorwayWidth * 0.5f) + (segmentWidth * 0.5f);
            EnsureBlock(root, baseName + "_L", center + new Vector3(-offset, 0f, 0f), new Vector3(segmentWidth, 4f, depth), color);
            EnsureBlock(root, baseName + "_R", center + new Vector3(offset, 0f, 0f), new Vector3(segmentWidth, 4f, depth), color);
        }

        private static void EnsureSegmentedZWall(Transform root, string baseName, Vector3 center, float totalDepth, float width, float doorwayWidth, Color color)
        {
            float segmentDepth = Mathf.Max(0.25f, (totalDepth - doorwayWidth) * 0.5f);
            float offset = (doorwayWidth * 0.5f) + (segmentDepth * 0.5f);
            EnsureBlock(root, baseName + "_A", center + new Vector3(0f, 0f, -offset), new Vector3(width, 4f, segmentDepth), color);
            EnsureBlock(root, baseName + "_B", center + new Vector3(0f, 0f, offset), new Vector3(width, 4f, segmentDepth), color);
        }

        private static void EnsureSpawnPoints(Transform root)
        {
            Vector3[] points =
            {
                // Spawn pads sit outside the active flood volumes and away from task interactables.
                // Earlier pads overlapped FloodZone_Main/FloodZone_LowArea, which could start players
                // already wet/submerged depending on flood sequence timing.
                new(-19f, 0.75f, -13.2f),
                new(-19f, 0.75f, 13.2f),
                new(19f, 0.75f, -13.2f),
                new(19f, 0.75f, 13.2f),
                new(-20f, 0.75f, 0f),
                new(20f, 0.75f, 0f),
                new(0f, 0.75f, -15f),
                new(0f, 0.75f, 15f)
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
            EnsureMarker(root, "FloodZone_Main", new Vector3(0f, 0.6f, 0f), new Vector3(16f, 1.2f, 10f), new Color(0.10f, 0.35f, 0.95f, 0.22f));
            EnsureMarker(root, "FloodZone_LowArea", new Vector3(0f, -0.2f, -12f), new Vector3(8f, 1.2f, 4f), new Color(0.95f, 0.10f, 0.12f, 0.22f));
        }
        private static void EnsureRoundEventDirector(Transform root)
        {
            EnsureNetworkSystem<NetworkRoundEventDirector>(root, "RoundEventDirector_Main");
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


        private static void EnsureTaskStations(Transform root)
        {
            // These are placeholder maintenance stations distributed across Undertint to create a full task loop.
            // All tasks are server-authoritative because they derive from NetworkRepairTask.
            TaskStationSpec[] specs =
            {
                new("task-valve-01", "Turn Valve A", "Turn Valve", 1.8f, new Vector3(-15f,1f,9f), ShipRepairTask.DifficultyTier.Easy, 0),
                new("task-valve-02", "Turn Valve B", "Turn Valve", 1.9f, new Vector3(-12f,1f,-9f), ShipRepairTask.DifficultyTier.Easy, 0),
                new("task-breaker-01", "Flip Breaker 1", "Flip Breaker", 2.0f, new Vector3(-1.5f,1f,9.5f), ShipRepairTask.DifficultyTier.Easy, 0),
                new("task-breaker-02", "Flip Breaker 2", "Flip Breaker", 2.0f, new Vector3(1.5f,1f,-9.5f), ShipRepairTask.DifficultyTier.Easy, 0),
                new("task-cable-01", "Reconnect Cable Alpha", "Reconnect Cable", 2.3f, new Vector3(13.5f,1f,9f), ShipRepairTask.DifficultyTier.Easy, 0),
                new("task-latch-01", "Tighten Panel Latch", "Tighten Latch", 2.2f, new Vector3(14f,1f,-9f), ShipRepairTask.DifficultyTier.Easy, 0),
                new("task-spark-01", "Spark Plug Sequence A", "Replace Spark Plugs", 5.5f, new Vector3(-17f,1f,0f), ShipRepairTask.DifficultyTier.Medium, 2),
                new("task-spark-02", "Spark Plug Sequence B", "Replace Spark Plugs", 5.5f, new Vector3(17f,1f,0f), ShipRepairTask.DifficultyTier.Medium, 2),
                new("task-pipe-01", "Align Pipe Pressure", "Align Pressure", 6.0f, new Vector3(-7f,1f,0f), ShipRepairTask.DifficultyTier.Medium, 2),
                new("task-coolant-01", "Reroute Coolant", "Reroute Coolant", 6.2f, new Vector3(7f,1f,0f), ShipRepairTask.DifficultyTier.Medium, 2),
                new("task-circuit-01", "Circuit Matching", "Solve Circuit", 22f, new Vector3(-14f,1f,13f), ShipRepairTask.DifficultyTier.Hard, 2),
                new("task-conduit-01", "Rotating Conduit Path", "Route Conduit", 24f, new Vector3(0f,1f,13f), ShipRepairTask.DifficultyTier.Hard, 2),
                new("task-pressure-01", "Pressure Balancing", "Balance Pressure", 26f, new Vector3(14f,1f,13f), ShipRepairTask.DifficultyTier.Hard, 2),
                new("task-signal-01", "Signal Matching", "Match Signal", 24f, new Vector3(0f,1f,-13f), ShipRepairTask.DifficultyTier.Hard, 2),
            };

            for (int i = 0; i < specs.Length; i++)
            {
                CreateOrUpdateTaskStation(root, specs[i], i);
            }
        }

        private static void CreateOrUpdateTaskStation(Transform root, TaskStationSpec spec, int index)
        {
            string objectName = "TaskStation_" + (index + 1).ToString("00");
            GameObject taskObject = GameObject.Find(objectName);
            if (taskObject == null)
            {
                taskObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, spec.Position, new Vector3(1.2f, 1.5f, 1.2f));
            }

            taskObject.transform.position = spec.Position;
            taskObject.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
            ApplyMaterial(taskObject, spec.Difficulty == ShipRepairTask.DifficultyTier.Hard ? new Color(0.8f,0.25f,0.25f) : (spec.Difficulty == ShipRepairTask.DifficultyTier.Medium ? new Color(0.85f,0.55f,0.2f) : new Color(0.2f,0.8f,0.95f)), false);

            ShipRepairTask task = GetOrAddComponent<ShipRepairTask>(taskObject);
            SetPrivateField(typeof(NetworkRepairTask), task, "taskId", spec.TaskId);
            SetPrivateField(typeof(NetworkRepairTask), task, "displayName", spec.DisplayName);
            SetPrivateField(typeof(NetworkRepairTask), task, "interactPrompt", spec.Prompt);
            SetPrivateField(typeof(NetworkRepairTask), task, "taskDurationSeconds", spec.DurationSeconds);
            SetPrivateField(typeof(ShipRepairTask), task, "difficulty", spec.Difficulty);
            SetPrivateField(typeof(ShipRepairTask), task, "maxFailuresBeforeLock", spec.MaxFailuresBeforeLock);

            BoxCollider collider = taskObject.GetComponent<BoxCollider>() ?? taskObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.size = new Vector3(1.2f, 1.5f, 1.2f);

            EnsureStainReceiver(taskObject);
            TrySpawnNetworkObject(taskObject);
        }

        private readonly struct TaskStationSpec
        {
            public TaskStationSpec(string taskId, string displayName, string prompt, float durationSeconds, Vector3 position, ShipRepairTask.DifficultyTier difficulty, int maxFailuresBeforeLock)
            {
                TaskId = taskId;
                DisplayName = displayName;
                Prompt = prompt;
                DurationSeconds = durationSeconds;
                Position = position;
                Difficulty = difficulty;
                MaxFailuresBeforeLock = maxFailuresBeforeLock;
            }

            public string TaskId { get; }
            public string DisplayName { get; }
            public string Prompt { get; }
            public float DurationSeconds { get; }
            public Vector3 Position { get; }
            public ShipRepairTask.DifficultyTier Difficulty { get; }
            public int MaxFailuresBeforeLock { get; }
        }

        private static void EnsureEnvironmentalHazards(Transform root)
        {
            ConfigureBleachLeakHazard(
                root,
                "BleachLeak_CargoMister",
                "cargo-mister",
                "Cargo Bleach Mister",
                new Vector3(-14f, 1.25f, -8f),
                new Vector3(5.5f, 2.4f, 4.2f),
                6.2f,
                36f,
                new Color(0.92f, 0.96f, 1f, 0.36f));

            ConfigureBleachLeakHazard(
                root,
                "BleachLeak_ReactorVent",
                "reactor-vent",
                "Reactor Bleach Vent",
                new Vector3(14f, 1.35f, -8f),
                new Vector3(4.5f, 2.7f, 4.5f),
                4.8f,
                34f,
                new Color(1f, 0.92f, 0.98f, 0.42f));
        }

        private static NetworkBleachLeakHazard ConfigureBleachLeakHazard(
            Transform root,
            string objectName,
            string hazardId,
            string displayName,
            Vector3 position,
            Vector3 scale,
            float exposureSeconds,
            float suppressionSeconds,
            Color color)
        {
            GameObject hazardObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(hazardObject, color, true);

            Collider collider = hazardObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }

            NetworkBleachLeakHazard hazard = GetOrAddComponent<NetworkBleachLeakHazard>(hazardObject);
            hazard.ConfigureRuntime(hazardId, displayName, exposureSeconds, suppressionSeconds, color, hazardObject.GetComponentInChildren<Renderer>());
            TrySpawnNetworkObject(hazardObject);
            return hazard;
        }

        private static void EnsureDecontaminationStations(Transform root)
        {
            ConfigureDecontaminationStation(root, "Decontamination_AftScrubber", "aft-scrubber", "Aft Decon Scrubber", "Run Decon", new Vector3(-4f, 1f, -14f), new Vector3(1.2f, 1.55f, 1.2f), 30f, 0.42f, 35f, 4f, new Color(0.12f, 0.92f, 1f));
            ConfigureDecontaminationStation(root, "Decontamination_ForeScrubber", "fore-scrubber", "Fore Decon Scrubber", "Run Decon", new Vector3(4f, 1f, 14f), new Vector3(1.2f, 1.55f, 1.2f), 34f, 0.36f, 30f, 4f, new Color(0.5f, 1f, 0.86f));
        }

        private static void ConfigureDecontaminationStation(
            Transform root,
            string objectName,
            string stationId,
            string displayName,
            string prompt,
            Vector3 position,
            Vector3 scale,
            float cooldownSeconds,
            float saturationReduction,
            float staminaRestore,
            float timeBonus,
            Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkDecontaminationStation station = GetOrAddComponent<NetworkDecontaminationStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, prompt, cooldownSeconds, saturationReduction, staminaRestore, timeBonus, stationObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = stationObject.GetComponent<BoxCollider>() ?? stationObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureFloodgateStations(Transform root)
        {
            FloodZone mainZone = FindFloodZoneByName("FloodZone_Main");
            FloodZone lowZone = FindFloodZoneByName("FloodZone_LowArea");
            NetworkBleachLeakHazard cargoLeak = FindHazardByName("BleachLeak_CargoMister");
            NetworkBleachLeakHazard reactorLeak = FindHazardByName("BleachLeak_ReactorVent");

            ConfigureFloodgateStation(root, "Floodgate_AftDrain", "aft-drain-gate", "Aft Floodgate Drain", "Vent Aft Floodgate", new Vector3(-7.5f, 1f, -14f), new Vector3(1.25f, 1.5f, 1.25f), new[] { lowZone }, new[] { cargoLeak }, 46f, 20f, 6f, new Color(0.08f, 0.95f, 0.78f));
            ConfigureFloodgateStation(root, "Floodgate_ForePressure", "fore-pressure-gate", "Fore Pressure Floodgate", "Equalize Fore Gate", new Vector3(7.5f, 1f, 14f), new Vector3(1.25f, 1.5f, 1.25f), new[] { mainZone }, new[] { reactorLeak }, 52f, 18f, 5f, new Color(0.12f, 0.78f, 1f));
        }

        private static void ConfigureFloodgateStation(Transform root, string objectName, string stationId, string displayName, string prompt, Vector3 position, Vector3 scale, FloodZone[] zones, NetworkBleachLeakHazard[] hazards, float cooldownSeconds, float suppressSeconds, float timeBonusSeconds, Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkFloodgateStation station = GetOrAddComponent<NetworkFloodgateStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, prompt, zones, hazards, cooldownSeconds, suppressSeconds, timeBonusSeconds, stationObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = stationObject.GetComponent<BoxCollider>() ?? stationObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureEmergencySealStations(Transform root)
        {
            FloodZone mainZone = FindFloodZoneByName("FloodZone_Main");
            FloodZone lowZone = FindFloodZoneByName("FloodZone_LowArea");
            NetworkBleachLeakHazard cargoLeak = FindHazardByName("BleachLeak_CargoMister");
            NetworkBleachLeakHazard reactorLeak = FindHazardByName("BleachLeak_ReactorVent");

            ConfigureEmergencySealStation(
                root,
                "EmergencySeal_Cargo",
                "cargo-seal",
                "Cargo Seal Station",
                "Purge Cargo Leak",
                new Vector3(-19f, 1f, -8f),
                new Vector3(1.15f, 1.45f, 1.15f),
                lowZone,
                cargoLeak,
                42f,
                38f,
                8f,
                new Color(0.25f, 1f, 0.72f));

            ConfigureEmergencySealStation(
                root,
                "EmergencySeal_Reactor",
                "reactor-seal",
                "Reactor Seal Station",
                "Purge Reactor Vent",
                new Vector3(19f, 1f, -8f),
                new Vector3(1.15f, 1.45f, 1.15f),
                mainZone,
                reactorLeak,
                48f,
                36f,
                10f,
                new Color(0.25f, 0.85f, 1f));
        }

        private static void ConfigureEmergencySealStation(
            Transform root,
            string objectName,
            string stationId,
            string displayName,
            string prompt,
            Vector3 position,
            Vector3 scale,
            FloodZone zone,
            NetworkBleachLeakHazard hazard,
            float cooldownSeconds,
            float suppressSeconds,
            float timeBonusSeconds,
            Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkEmergencySealStation station = GetOrAddComponent<NetworkEmergencySealStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, prompt, zone, hazard, cooldownSeconds, suppressSeconds, timeBonusSeconds, stationObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = stationObject.GetComponent<BoxCollider>() ?? stationObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }


        private static void EnsureSafeRoomBeacons(Transform root)
        {
            ConfigureSafeRoomBeacon(root, "SafeRoom_AftRefuge", "aft-refuge", "Aft Refuge Bubble", "Open Refuge", new Vector3(-20f, 1f, -12f), new Vector3(1.35f, 1.65f, 1.35f), 58f, 18f, 4.75f, 0.038f, 5.0f, 4f, new Color(0.15f, 0.95f, 0.68f));
            ConfigureSafeRoomBeacon(root, "SafeRoom_ForeRefuge", "fore-refuge", "Fore Refuge Bubble", "Open Refuge", new Vector3(20f, 1f, 12f), new Vector3(1.35f, 1.65f, 1.35f), 64f, 16f, 4.25f, 0.034f, 4.6f, 3f, new Color(0.28f, 0.95f, 1f));
        }

        private static void ConfigureSafeRoomBeacon(
            Transform root,
            string objectName,
            string beaconId,
            string displayName,
            string prompt,
            Vector3 position,
            Vector3 scale,
            float cooldownSeconds,
            float activeDurationSeconds,
            float radius,
            float saturationReductionPerSecond,
            float staminaRestorePerSecond,
            float timeBonusSeconds,
            Color color)
        {
            GameObject beaconObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cylinder, position, scale);
            ApplyMaterial(beaconObject, color, false);

            NetworkSafeRoomBeacon beacon = GetOrAddComponent<NetworkSafeRoomBeacon>(beaconObject);
            beacon.ConfigureRuntime(beaconId, displayName, prompt, cooldownSeconds, activeDurationSeconds, radius, saturationReductionPerSecond, staminaRestorePerSecond, timeBonusSeconds, beaconObject.GetComponentInChildren<Renderer>());

            Collider collider = beaconObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(beaconObject);
            TrySpawnNetworkObject(beaconObject);
        }

        private static void EnsurePaintScannerStations(Transform root)
        {
            ConfigurePaintScannerStation(root, "PaintScanner_EngineForensics", "engine-forensics", "Engine Paint Scanner", "Scan Residue", new Vector3(-20f, 1f, 8f), new Vector3(1.25f, 1.55f, 1.25f), 36f, 9.5f, 12f, 2f, new Color(0.9f, 0.45f, 1f));
            ConfigurePaintScannerStation(root, "PaintScanner_LabForensics", "lab-forensics", "Lab Paint Scanner", "Scan Residue", new Vector3(20f, 1f, -8f), new Vector3(1.25f, 1.55f, 1.25f), 42f, 10f, 14f, 2f, new Color(1f, 0.65f, 0.95f));
        }

        private static void ConfigurePaintScannerStation(
            Transform root,
            string objectName,
            string scannerId,
            string displayName,
            string prompt,
            Vector3 position,
            Vector3 scale,
            float cooldownSeconds,
            float scanRadius,
            float quarantineSeconds,
            float timeBonusSeconds,
            Color color)
        {
            GameObject scannerObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(scannerObject, color, false);

            NetworkPaintScannerStation scanner = GetOrAddComponent<NetworkPaintScannerStation>(scannerObject);
            scanner.ConfigureRuntime(scannerId, displayName, prompt, cooldownSeconds, scanRadius, quarantineSeconds, timeBonusSeconds, scannerObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = scannerObject.GetComponent<BoxCollider>() ?? scannerObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(scannerObject);
            TrySpawnNetworkObject(scannerObject);
        }

        private static void EnsureVitalsStations(Transform root)
        {
            ConfigureVitalsStation(root, "VitalsStation_Aft", "aft-vitals", "Aft Vitals Monitor", "Check Vitals", new Vector3(-7f, 1f, -14f), new Vector3(1.25f, 1.55f, 1.25f), 34f, 1.5f, new Color(0.15f, 0.95f, 1f));
            ConfigureVitalsStation(root, "VitalsStation_Fore", "fore-vitals", "Fore Vitals Monitor", "Check Vitals", new Vector3(7f, 1f, 14f), new Vector3(1.25f, 1.55f, 1.25f), 38f, 1.5f, new Color(0.25f, 1f, 0.85f));
        }

        private static void ConfigureVitalsStation(
            Transform root,
            string objectName,
            string stationId,
            string displayName,
            string prompt,
            Vector3 position,
            Vector3 scale,
            float cooldownSeconds,
            float timeBonusSeconds,
            Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkVitalsStation station = GetOrAddComponent<NetworkVitalsStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, prompt, cooldownSeconds, timeBonusSeconds, stationObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = stationObject.GetComponent<BoxCollider>() ?? stationObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureBleachVentNetwork(Transform root)
        {
            NetworkBleachVent westA = ConfigureBleachVentShell(root, "BleachVent_WestAft", "west-aft-vent", "West Aft Vent", new Vector3(-21f, 0.65f, -10f), new Vector3(1.15f, 0.45f, 1.15f), 90f, new Color(0.78f, 0.12f, 1f));
            NetworkBleachVent eastA = ConfigureBleachVentShell(root, "BleachVent_EastFore", "east-fore-vent", "East Fore Vent", new Vector3(21f, 0.65f, 10f), new Vector3(1.15f, 0.45f, 1.15f), -90f, new Color(0.78f, 0.12f, 1f));
            NetworkBleachVent westB = ConfigureBleachVentShell(root, "BleachVent_WestFore", "west-fore-vent", "West Fore Vent", new Vector3(-21f, 0.65f, 10f), new Vector3(1.15f, 0.45f, 1.15f), 90f, new Color(0.65f, 0.10f, 0.95f));
            NetworkBleachVent eastB = ConfigureBleachVentShell(root, "BleachVent_EastAft", "east-aft-vent", "East Aft Vent", new Vector3(21f, 0.65f, -10f), new Vector3(1.15f, 0.45f, 1.15f), -90f, new Color(0.65f, 0.10f, 0.95f));

            ConfigureBleachVentPair(westA, eastA, 16f, 42f, 2f);
            ConfigureBleachVentPair(eastA, westA, 16f, 42f, 2f);
            ConfigureBleachVentPair(westB, eastB, 18f, 46f, 2f);
            ConfigureBleachVentPair(eastB, westB, 18f, 46f, 2f);
        }

        private static NetworkBleachVent ConfigureBleachVentShell(
            Transform root,
            string objectName,
            string ventId,
            string displayName,
            Vector3 position,
            Vector3 scale,
            float yaw,
            Color color)
        {
            GameObject ventObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cylinder, position, scale);
            ventObject.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            ApplyMaterial(ventObject, color, false);

            NetworkBleachVent vent = GetOrAddComponent<NetworkBleachVent>(ventObject);
            vent.ConfigureRuntime(ventId, displayName, "Slip Through Vent", "Seal Vent", 16f, 42f, 2f, null, ventObject.GetComponentInChildren<Renderer>());

            Collider collider = ventObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(ventObject);
            TrySpawnNetworkObject(ventObject);
            return vent;
        }

        private static void ConfigureBleachVentPair(NetworkBleachVent vent, NetworkBleachVent linkedVent, float cooldownSeconds, float sealDurationSeconds, float timeBonusSeconds)
        {
            if (vent == null)
            {
                return;
            }

            vent.ConfigureRuntime(
                vent.VentId,
                vent.DisplayName,
                "Slip Through Vent",
                "Seal Vent",
                cooldownSeconds,
                sealDurationSeconds,
                timeBonusSeconds,
                linkedVent,
                vent.GetComponentInChildren<Renderer>());
        }

        private static void EnsureSecurityCameraStations(Transform root)
        {
            ConfigureSecurityCameraStation(root, "SecurityCamera_Aft", "aft-camera-sweep", "Aft Camera Sweep", "Run Camera Sweep", "Loop Camera Feed", new Vector3(-11f, 1f, -14f), new Vector3(1.15f, 1.55f, 1.15f), 42f, 13f, 2f, 5f, new Color(0.18f, 0.62f, 1f));
            ConfigureSecurityCameraStation(root, "SecurityCamera_Fore", "fore-camera-sweep", "Fore Camera Sweep", "Run Camera Sweep", "Loop Camera Feed", new Vector3(11f, 1f, 14f), new Vector3(1.15f, 1.55f, 1.15f), 46f, 14f, 2f, 5f, new Color(0.32f, 0.85f, 1f));
        }

        private static void ConfigureSecurityCameraStation(Transform root, string objectName, string stationId, string displayName, string sweepPrompt, string jamPrompt, Vector3 position, Vector3 scale, float cooldownSeconds, float scanRadius, float timeBonusSeconds, float timePenaltySeconds, Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cylinder, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkSecurityCameraStation station = GetOrAddComponent<NetworkSecurityCameraStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, sweepPrompt, jamPrompt, cooldownSeconds, scanRadius, timeBonusSeconds, timePenaltySeconds, stationObject.GetComponentInChildren<Renderer>());

            Collider collider = stationObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureAlarmTripwires(Transform root)
        {
            ConfigureAlarmTripwire(root, "AlarmTripwire_EngineHall", "engine-hall-wire", "Engine Hall Tripwire", "Arm Engine Alarm", "Short Engine Alarm", new Vector3(-14f, 0.75f, 0f), new Vector3(2.2f, 0.35f, 0.35f), 4.6f, 70f, 36f, 1.5f, 4f, new Color(1f, 0.9f, 0.15f));
            ConfigureAlarmTripwire(root, "AlarmTripwire_ReactorHall", "reactor-hall-wire", "Reactor Hall Tripwire", "Arm Reactor Alarm", "Short Reactor Alarm", new Vector3(14f, 0.75f, 0f), new Vector3(2.2f, 0.35f, 0.35f), 4.6f, 74f, 38f, 1.5f, 4f, new Color(1f, 0.72f, 0.18f));
            ConfigureAlarmTripwire(root, "AlarmTripwire_Center", "center-cross-wire", "Center Cross Tripwire", "Arm Center Alarm", "Short Center Alarm", new Vector3(0f, 0.75f, 0f), new Vector3(2.5f, 0.35f, 0.35f), 4.25f, 62f, 34f, 1f, 4f, new Color(0.95f, 1f, 0.2f));
        }

        private static void ConfigureAlarmTripwire(Transform root, string objectName, string tripwireId, string displayName, string armPrompt, string jamPrompt, Vector3 position, Vector3 scale, float radius, float armedDuration, float cooldownSeconds, float timeBonusSeconds, float timePenaltySeconds, Color color)
        {
            GameObject tripwireObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(tripwireObject, color, false);

            NetworkAlarmTripwire tripwire = GetOrAddComponent<NetworkAlarmTripwire>(tripwireObject);
            tripwire.ConfigureRuntime(tripwireId, displayName, armPrompt, jamPrompt, radius, armedDuration, cooldownSeconds, timeBonusSeconds, timePenaltySeconds, tripwireObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = tripwireObject.GetComponent<BoxCollider>() ?? tripwireObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(tripwireObject);
            TrySpawnNetworkObject(tripwireObject);
        }

        private static void EnsureInkWellStations(Transform root)
        {
            ConfigureInkWellStation(root, "InkWell_AftMix", "aft-ink-well", "Aft Ink Well", "Reconstitute", "Contaminate Well", new Vector3(-18f, 1f, -14f), new Vector3(1.3f, 1.2f, 1.3f), 30f, 0.28f, 30f, 2.5f, 4f, new Color(0.92f, 0.28f, 1f));
            ConfigureInkWellStation(root, "InkWell_ForeMix", "fore-ink-well", "Fore Ink Well", "Reconstitute", "Contaminate Well", new Vector3(18f, 1f, 14f), new Vector3(1.3f, 1.2f, 1.3f), 34f, 0.24f, 26f, 2.5f, 4f, new Color(0.28f, 0.75f, 1f));
        }

        private static void ConfigureInkWellStation(Transform root, string objectName, string stationId, string displayName, string restorePrompt, string contaminatePrompt, Vector3 position, Vector3 scale, float cooldownSeconds, float saturationReduction, float staminaRestore, float timeBonusSeconds, float timePenaltySeconds, Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Sphere, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkInkWellStation station = GetOrAddComponent<NetworkInkWellStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, restorePrompt, contaminatePrompt, cooldownSeconds, saturationReduction, staminaRestore, timeBonusSeconds, timePenaltySeconds, stationObject.GetComponentInChildren<Renderer>());

            Collider collider = stationObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureFalseEvidenceStations(Transform root)
        {
            ConfigureFalseEvidenceStation(root, "FalseEvidence_AftSmear", "aft-smear-kit", "Aft Residue Smear Kit", "Plant False Residue", new Vector3(-21f, 1f, 5f), new Vector3(1.05f, 1.35f, 1.05f), 78f, 95f, 6f, 1.25f, new Color(0.8f, 0.15f, 1f));
            ConfigureFalseEvidenceStation(root, "FalseEvidence_ForeSmear", "fore-smear-kit", "Fore Residue Smear Kit", "Plant False Residue", new Vector3(21f, 1f, -5f), new Vector3(1.05f, 1.35f, 1.05f), 84f, 100f, 6f, 1.25f, new Color(0.65f, 0.08f, 1f));
        }

        private static void ConfigureFalseEvidenceStation(Transform root, string objectName, string stationId, string displayName, string prompt, Vector3 position, Vector3 scale, float cooldownSeconds, float lifetimeSeconds, float timePenaltySeconds, float spawnOffset, Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkFalseEvidenceStation station = GetOrAddComponent<NetworkFalseEvidenceStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, prompt, cooldownSeconds, lifetimeSeconds, timePenaltySeconds, spawnOffset, stationObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = stationObject.GetComponent<BoxCollider>() ?? stationObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureEmergencyMeetingConsole(Transform root)
        {
            GameObject consoleObject = CreateAuthoritativePrimitive(root, "EmergencyMeetingConsole_Main", PrimitiveType.Cylinder, new Vector3(0f, 1f, 14f), new Vector3(1.45f, 1.05f, 1.45f));
            ApplyMaterial(consoleObject, new Color(1f, 0.22f, 0.22f), false);

            NetworkEmergencyMeetingConsole meetingConsole = GetOrAddComponent<NetworkEmergencyMeetingConsole>(consoleObject);
            meetingConsole.ConfigureRuntime("main-meeting-console", "Emergency Meeting Console", "Call Emergency Meeting", 150f, 6f, consoleObject.GetComponentInChildren<Renderer>());

            Collider collider = consoleObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(consoleObject);
            TrySpawnNetworkObject(consoleObject);
        }

        private static void EnsureVotingPodiums(Transform root)
        {
            ConfigureVotingPodium(root, "VotingPodium_AccuseWest", "accuse-west", "West Accusation Podium", "Vote Nearest Suspect", "Vote Skip", false, new Vector3(-2.5f, 1f, 10.8f), new Vector3(1.15f, 1.45f, 1.15f), 5.8f, new Color(1f, 0.38f, 0.18f));
            ConfigureVotingPodium(root, "VotingPodium_AccuseEast", "accuse-east", "East Accusation Podium", "Vote Nearest Suspect", "Vote Skip", false, new Vector3(2.5f, 1f, 10.8f), new Vector3(1.15f, 1.45f, 1.15f), 5.8f, new Color(1f, 0.38f, 0.18f));
            ConfigureVotingPodium(root, "VotingPodium_Skip", "skip-main", "Skip Vote Podium", "Vote Nearest Suspect", "Vote Skip", true, new Vector3(0f, 1f, 10.8f), new Vector3(1.15f, 1.25f, 1.15f), 5.8f, new Color(0.22f, 0.85f, 1f));
        }

        private static void ConfigureVotingPodium(Transform root, string objectName, string podiumId, string displayName, string accusePrompt, string skipPrompt, bool skipVote, Vector3 position, Vector3 scale, float searchRadius, Color color)
        {
            GameObject podiumObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cylinder, position, scale);
            ApplyMaterial(podiumObject, color, false);

            NetworkVotingPodium podium = GetOrAddComponent<NetworkVotingPodium>(podiumObject);
            podium.ConfigureRuntime(podiumId, displayName, accusePrompt, skipPrompt, skipVote, searchRadius, podiumObject.GetComponentInChildren<Renderer>());

            Collider collider = podiumObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(podiumObject);
            TrySpawnNetworkObject(podiumObject);
        }

        private static void EnsureCrewRallyStations(Transform root)
        {
            ConfigureCrewRallyStation(root, "CrewRally_Aft", "aft-rally", "Aft Crew Rally Beacon", "Start Aft Rally", "Fake Aft Rally", new Vector3(-4f, 1f, -10.8f), new Vector3(1.25f, 1.45f, 1.25f), 72f, 7.0f, 24f, 0.20f, 5f, 6f, new Color(0.25f, 1f, 0.42f));
            ConfigureCrewRallyStation(root, "CrewRally_Fore", "fore-rally", "Fore Crew Rally Beacon", "Start Fore Rally", "Fake Fore Rally", new Vector3(4f, 1f, 10.8f), new Vector3(1.25f, 1.45f, 1.25f), 78f, 7.0f, 22f, 0.18f, 5f, 6f, new Color(0.35f, 1f, 0.72f));
        }

        private static void ConfigureCrewRallyStation(Transform root, string objectName, string stationId, string displayName, string rallyPrompt, string panicPrompt, Vector3 position, Vector3 scale, float cooldownSeconds, float radius, float staminaRestore, float saturationReduction, float timeBonusSeconds, float fakePenaltySeconds, Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Sphere, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkCrewRallyStation station = GetOrAddComponent<NetworkCrewRallyStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, rallyPrompt, panicPrompt, cooldownSeconds, radius, staminaRestore, saturationReduction, timeBonusSeconds, fakePenaltySeconds, stationObject.GetComponentInChildren<Renderer>());

            Collider collider = stationObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureBulkheadLockStations(Transform root)
        {
            FloodZone mainZone = FindFloodZoneByName("FloodZone_Main");
            FloodZone lowZone = FindFloodZoneByName("FloodZone_LowArea");

            ConfigureBulkheadLockStation(root, "BulkheadLock_ForeRoute", "fore-bulkhead", "Fore Bulkhead Lock", "Seal Fore Bulkhead", "Jam Fore Bulkhead", mainZone, new Vector3(0f, 1f, 6.8f), new Vector3(1.1f, 1.55f, 1.1f), 58f, 22f, 4f, 7f, new Color(0.18f, 0.72f, 1f));
            ConfigureBulkheadLockStation(root, "BulkheadLock_AftRoute", "aft-bulkhead", "Aft Bulkhead Lock", "Seal Aft Bulkhead", "Jam Aft Bulkhead", lowZone, new Vector3(0f, 1f, -6.8f), new Vector3(1.1f, 1.55f, 1.1f), 62f, 24f, 4f, 8f, new Color(0.16f, 0.82f, 0.95f));
        }

        private static void ConfigureBulkheadLockStation(Transform root, string objectName, string stationId, string displayName, string lockPrompt, string jamPrompt, FloodZone linkedZone, Vector3 position, Vector3 scale, float cooldownSeconds, float jamSeconds, float timeBonusSeconds, float jamPenaltySeconds, Color color)
        {
            GameObject stationObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(stationObject, color, false);

            NetworkBulkheadLockStation station = GetOrAddComponent<NetworkBulkheadLockStation>(stationObject);
            station.ConfigureRuntime(stationId, displayName, lockPrompt, jamPrompt, linkedZone, cooldownSeconds, jamSeconds, timeBonusSeconds, jamPenaltySeconds, stationObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = stationObject.GetComponent<BoxCollider>() ?? stationObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(stationObject);
            TrySpawnNetworkObject(stationObject);
        }

        private static void EnsureCalloutBeacons(Transform root)
        {
            ConfigureCalloutBeacon(root, "CalloutBeacon_West", "west-callout", "West Callout Beacon", "Broadcast West Callout", "Fake West Callout", new Vector3(-11.5f, 1f, 0f), new Vector3(1.0f, 1.35f, 1.0f), 38f, 4f, new Color(1f, 0.85f, 0.22f));
            ConfigureCalloutBeacon(root, "CalloutBeacon_East", "east-callout", "East Callout Beacon", "Broadcast East Callout", "Fake East Callout", new Vector3(11.5f, 1f, 0f), new Vector3(1.0f, 1.35f, 1.0f), 38f, 4f, new Color(1f, 0.78f, 0.18f));
        }

        private static void ConfigureCalloutBeacon(Transform root, string objectName, string beaconId, string displayName, string calloutPrompt, string fakePrompt, Vector3 position, Vector3 scale, float cooldownSeconds, float fakePenaltySeconds, Color color)
        {
            GameObject beaconObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cylinder, position, scale);
            ApplyMaterial(beaconObject, color, false);

            NetworkCalloutBeacon beacon = GetOrAddComponent<NetworkCalloutBeacon>(beaconObject);
            beacon.ConfigureRuntime(beaconId, displayName, calloutPrompt, fakePrompt, cooldownSeconds, fakePenaltySeconds, beaconObject.GetComponentInChildren<Renderer>());

            Collider collider = beaconObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }

            EnsureStainReceiver(beaconObject);
            TrySpawnNetworkObject(beaconObject);
        }

        private static void EnsureSlimeLaunchPads(Transform root)
        {
            ConfigureSlimeLaunchPad(root, "LaunchPad_WestToCenter", "west-launch", "West Slime Launcher", new Vector3(-21f, 0.55f, 0f), new Vector3(1.9f, 0.35f, 1.9f), new Vector3(1f, 0.1f, 0f), 10.5f, 5.2f, new Color(0.55f, 1f, 0.25f));
            ConfigureSlimeLaunchPad(root, "LaunchPad_EastToCenter", "east-launch", "East Slime Launcher", new Vector3(21f, 0.55f, 0f), new Vector3(1.9f, 0.35f, 1.9f), new Vector3(-1f, 0.1f, 0f), 10.5f, 5.2f, new Color(0.55f, 1f, 0.25f));
            ConfigureSlimeLaunchPad(root, "LaunchPad_AftToFore", "aft-launch", "Aft Slime Launcher", new Vector3(0f, 0.55f, -14f), new Vector3(1.9f, 0.35f, 1.9f), new Vector3(0f, 0.1f, 1f), 11.5f, 5.8f, new Color(1f, 0.82f, 0.2f));
            ConfigureSlimeLaunchPad(root, "LaunchPad_ForeToAft", "fore-launch", "Fore Slime Launcher", new Vector3(0f, 0.55f, 14f), new Vector3(1.9f, 0.35f, 1.9f), new Vector3(0f, 0.1f, -1f), 11.5f, 5.8f, new Color(1f, 0.82f, 0.2f));
        }

        private static void ConfigureSlimeLaunchPad(Transform root, string objectName, string padId, string displayName, Vector3 position, Vector3 scale, Vector3 launchDirection, float launchSpeed, float upwardBoost, Color color)
        {
            GameObject padObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(padObject, color, false);

            NetworkSlimeLaunchPad launchPad = GetOrAddComponent<NetworkSlimeLaunchPad>(padObject);
            launchPad.ConfigureRuntime(padId, displayName, "Bounce Launch", launchDirection, launchSpeed, upwardBoost, 1.3f, padObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = padObject.GetComponent<BoxCollider>() ?? padObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(padObject);
            TrySpawnNetworkObject(padObject);
        }

        private static void EnsureAdvancedTaskSet(Transform root)
        {
            FloodZone mainZone = FindFloodZoneByName("FloodZone_Main");
            FloodZone lowZone = FindFloodZoneByName("FloodZone_LowArea");

            InventoryItemDefinition flowCell = CreateRuntimeItemDefinition(
                "flow-cell",
                "Flow Cell",
                "A compact pump regulator that fits in the three-slot inventory.",
                InventoryItemSize.Small,
                new Color(0.25f, 0.85f, 1f));

            InventoryItemDefinition stabilizerCore = CreateRuntimeItemDefinition(
                "stabilizer-core",
                "Stabilizer Core",
                "A medium ship component used by Undertint flood-control repairs.",
                InventoryItemSize.Medium,
                new Color(0.9f, 0.45f, 1f));

            InventoryItemDefinition oversizedBattery = CreateRuntimeItemDefinition(
                "oversized-ship-battery",
                "Oversized Ship Battery",
                "A large prop item that must be carried physically; it intentionally cannot enter the three-slot inventory.",
                InventoryItemSize.Large,
                new Color(1f, 0.62f, 0.18f));

            InventoryItemDefinition[] runtimeCatalog = { flowCell, stabilizerCore, oversizedBattery };
            ConfigureRuntimeInventoryCatalog(runtimeCatalog);
            EnsureInventoryDemoPickups(root, oversizedBattery);

            ConfigureComponentRoutingTask(
                root,
                "AdvancedTask_FlowCellRoute",
                "flow-cell-route",
                "Route Flow Cell",
                flowCell,
                mainZone,
                new Vector3(-19f, 1f, 12f),
                new Vector3(-2.5f, 1f, -13f));

            ConfigureComponentRoutingTask(
                root,
                "AdvancedTask_StabilizerCoreRoute",
                "stabilizer-core-route",
                "Install Stabilizer Core",
                stabilizerCore,
                lowZone,
                new Vector3(19f, 1f, 12f),
                new Vector3(2.5f, 1f, -13f));

            ConfigureFloodValveTask(root, mainZone, lowZone);
            ConfigureStabilizerTask(root, lowZone);
            ConfigureSignalPatternTask(root, mainZone, lowZone);
            ConfigureChemicalBlendTask(root, mainZone, lowZone);
            ConfigureHullPatchTask(root, mainZone, lowZone);
            ConfigurePowerRelayTask(root, mainZone, lowZone);
            ConfigureCoolantRerouteTask(root, mainZone, lowZone);
            ConfigureOxygenPurgeTask(root, mainZone, lowZone);
        }


        private static void EnsureSabotageConsoles(Transform root)
        {
            FloodZone mainZone = FindFloodZoneByName("FloodZone_Main");
            FloodZone lowZone = FindFloodZoneByName("FloodZone_LowArea");
            FloodZone[] zones = mainZone != null && lowZone != null
                ? new[] { mainZone, lowZone }
                : FindObjectsByType<FloodZone>(FindObjectsSortMode.None);

            ConfigureSabotageConsole(
                root,
                "SabotageConsole_BilgeBypass",
                "bilge-bypass",
                "Bilge Bypass",
                "Overload Bypass",
                new Vector3(-20f, 1f, -1.5f),
                new Vector3(1.25f, 1.65f, 1.25f),
                34f,
                16f,
                zones,
                new Color(0.88f, 0.18f, 0.94f));

            ConfigureSabotageConsole(
                root,
                "SabotageConsole_ReactorFoam",
                "reactor-foam",
                "Reactor Foam Injector",
                "Inject Foam",
                new Vector3(20f, 1f, 1.5f),
                new Vector3(1.25f, 1.65f, 1.25f),
                46f,
                22f,
                zones,
                new Color(0.94f, 0.94f, 1f));
        }

        private static void ConfigureSabotageConsole(
            Transform root,
            string objectName,
            string consoleId,
            string displayName,
            string prompt,
            Vector3 position,
            Vector3 scale,
            float cooldownSeconds,
            float timePenaltySeconds,
            FloodZone[] affectedZones,
            Color color)
        {
            GameObject consoleObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(consoleObject, color, false);

            NetworkSabotageConsole sabotageConsole = GetOrAddComponent<NetworkSabotageConsole>(consoleObject);
            sabotageConsole.ConfigureRuntime(
                consoleId,
                displayName,
                prompt,
                cooldownSeconds,
                timePenaltySeconds,
                affectedZones,
                consoleObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = consoleObject.GetComponent<BoxCollider>() ?? consoleObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(consoleObject);
            TrySpawnNetworkObject(consoleObject);
        }

        private static InventoryItemDefinition CreateRuntimeItemDefinition(string itemId, string displayName, string description, InventoryItemSize size, Color tint)
        {
            InventoryItemDefinition item = ScriptableObject.CreateInstance<InventoryItemDefinition>();
            item.name = itemId;
            SetPrivateField(typeof(InventoryItemDefinition), item, "itemId", itemId);
            SetPrivateField(typeof(InventoryItemDefinition), item, "displayName", displayName);
            SetPrivateField(typeof(InventoryItemDefinition), item, "shortDescription", description);
            SetPrivateField(typeof(InventoryItemDefinition), item, "size", size);
            SetPrivateField(typeof(InventoryItemDefinition), item, "uiTint", tint);
            return item;
        }

        private static void ConfigureRuntimeInventoryCatalog(InventoryItemDefinition[] catalog)
        {
            PlayerInventoryState[] inventories = FindObjectsByType<PlayerInventoryState>(FindObjectsSortMode.None);
            foreach (PlayerInventoryState inventory in inventories)
            {
                if (inventory != null)
                {
                    inventory.ConfigureRuntimeCatalog(catalog);
                }
            }
        }

        private static void EnsureInventoryDemoPickups(Transform root, InventoryItemDefinition oversizedBattery)
        {
            if (oversizedBattery == null)
            {
                return;
            }

            CreateOrUpdateInventoryPickup(
                root,
                "Pickup_OversizedShipBattery",
                oversizedBattery,
                "Inspect",
                new Vector3(18f, 0.95f, -2.5f),
                new Vector3(1.4f, 1.1f, 1.4f),
                oversizedBattery.UiTint);
        }

        private static void CreateOrUpdateInventoryPickup(
            Transform root,
            string objectName,
            InventoryItemDefinition itemDefinition,
            string prompt,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            GameObject pickupObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, scale);
            ApplyMaterial(pickupObject, color, false);

            NetworkInventoryPickup pickup = GetOrAddComponent<NetworkInventoryPickup>(pickupObject);
            pickup.ConfigureRuntime(itemDefinition, prompt, pickupObject.GetComponentInChildren<Renderer>());

            BoxCollider collider = pickupObject.GetComponent<BoxCollider>() ?? pickupObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(pickupObject);
            TrySpawnNetworkObject(pickupObject);
        }

        private static void ConfigureComponentRoutingTask(
            Transform root,
            string objectName,
            string taskId,
            string displayName,
            InventoryItemDefinition component,
            FloodZone rewardZone,
            Vector3 dispenserPosition,
            Vector3 installPosition)
        {
            ComponentRoutingTask task = EnsureAdvancedTaskRoot<ComponentRoutingTask>(root, objectName, taskId, displayName, 2);
            SetPrivateField(typeof(ComponentRoutingTask), task, "requiredComponent", component);
            SetPrivateField(typeof(ComponentRoutingTask), task, "rewardFloodZone", rewardZone);
            SetPrivateField(typeof(ComponentRoutingTask), task, "dispenserStepId", "dispenser");
            SetPrivateField(typeof(ComponentRoutingTask), task, "installStepId", "install");

            CreateOrUpdateTaskStep(root, objectName + "_Dispenser", task, "dispenser", "Collect " + component.DisplayName, dispenserPosition, new Color(0.20f, 0.70f, 1f));
            CreateOrUpdateTaskStep(root, objectName + "_Install", task, "install", "Install " + component.DisplayName, installPosition, new Color(0.95f, 0.45f, 1f));
            task.transform.position = (dispenserPosition + installPosition) * 0.5f;

            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigureFloodValveTask(Transform root, FloodZone mainZone, FloodZone lowZone)
        {
            FloodValveTask task = EnsureAdvancedTaskRoot<FloodValveTask>(root, "AdvancedTask_FloodValve", "flood-valve-sequence", "Flood Release Valves", 3);
            SetPrivateField(typeof(FloodValveTask), task, "valveStepIds", new[] { "valve_a", "valve_c", "valve_b" });
            SetPrivateField(typeof(FloodValveTask), task, "releaseStepId", "release");
            SetPrivateField(typeof(FloodValveTask), task, "controlledZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(FloodValveTask), task, "completeSetsSafe", true);

            CreateOrUpdateTaskStep(root, "FloodValve_A", task, "valve_a", "Set Valve A", new Vector3(-18f, 1f, 4.5f), new Color(0.2f, 0.8f, 1f));
            CreateOrUpdateTaskStep(root, "FloodValve_B", task, "valve_b", "Set Valve B", new Vector3(-14f, 1f, 4.5f), new Color(0.2f, 0.8f, 1f));
            CreateOrUpdateTaskStep(root, "FloodValve_C", task, "valve_c", "Set Valve C", new Vector3(-10f, 1f, 4.5f), new Color(0.2f, 0.8f, 1f));
            CreateOrUpdateTaskStep(root, "FloodValve_Release", task, "release", "Release Pressure", new Vector3(-6f, 1f, 4.5f), new Color(1f, 0.85f, 0.2f));
            task.transform.position = new Vector3(-12f, 1f, 4.5f);

            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigureStabilizerTask(Transform root, FloodZone lowZone)
        {
            StabilizerRealignmentTask task = EnsureAdvancedTaskRoot<StabilizerRealignmentTask>(root, "AdvancedTask_Stabilizer", "stabilizer-realignment", "Stabilizer Realignment", 3);
            SetPrivateField(typeof(StabilizerRealignmentTask), task, "linkedFloodZone", lowZone);

            CreateOrUpdateTaskStep(root, "Stabilizer_Drain", task, "drain", "Drain Chamber", new Vector3(7f, 1f, -12f), new Color(0.25f, 0.95f, 1f));
            CreateOrUpdateTaskStep(root, "Stabilizer_ArmA", task, "arm_a", "Align Arm A", new Vector3(10f, 1f, -12f), new Color(0.85f, 0.35f, 1f));
            CreateOrUpdateTaskStep(root, "Stabilizer_ArmB", task, "arm_b", "Align Arm B", new Vector3(13f, 1f, -12f), new Color(0.85f, 0.35f, 1f));
            CreateOrUpdateTaskStep(root, "Stabilizer_ArmC", task, "arm_c", "Align Arm C", new Vector3(16f, 1f, -12f), new Color(0.85f, 0.35f, 1f));
            CreateOrUpdateTaskStep(root, "Stabilizer_Lock", task, "lock", "Lock Array", new Vector3(19f, 1f, -8f), new Color(1f, 0.82f, 0.24f));
            task.transform.position = new Vector3(13f, 1f, -11.2f);

            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigureSignalPatternTask(Transform root, FloodZone mainZone, FloodZone lowZone)
        {
            SignalPatternTask task = EnsureAdvancedTaskRoot<SignalPatternTask>(root, "AdvancedTask_SignalPattern", "signal-pattern-link", "Signal Pattern Link", 2);
            SetPrivateField(typeof(SignalPatternTask), task, "requiredStepOrder", new[] { "cyan", "amber", "magenta", "white" });
            SetPrivateField(typeof(SignalPatternTask), task, "stabilizedZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(SignalPatternTask), task, "surgeZonesOnFailure", new[] { mainZone, lowZone });
            SetPrivateField(typeof(SignalPatternTask), task, "completeSetsSafe", true);

            CreateOrUpdateTaskStep(root, "SignalPattern_Cyan", task, "cyan", "Transmit Cyan", new Vector3(8f, 1f, 12.8f), new Color(0.1f, 0.95f, 1f));
            CreateOrUpdateTaskStep(root, "SignalPattern_Amber", task, "amber", "Transmit Amber", new Vector3(11f, 1f, 12.8f), new Color(1f, 0.68f, 0.15f));
            CreateOrUpdateTaskStep(root, "SignalPattern_Magenta", task, "magenta", "Transmit Magenta", new Vector3(14f, 1f, 12.8f), new Color(1f, 0.25f, 0.95f));
            CreateOrUpdateTaskStep(root, "SignalPattern_White", task, "white", "Transmit White", new Vector3(17f, 1f, 12.8f), new Color(0.95f, 0.95f, 1f));
            task.transform.position = new Vector3(12.5f, 1f, 12.8f);

            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigureChemicalBlendTask(Transform root, FloodZone mainZone, FloodZone lowZone)
        {
            NetworkBleachLeakHazard cargoLeak = FindHazardByName("BleachLeak_CargoMister");
            NetworkBleachLeakHazard reactorLeak = FindHazardByName("BleachLeak_ReactorVent");

            ChemicalBlendTask task = EnsureAdvancedTaskRoot<ChemicalBlendTask>(root, "AdvancedTask_ChemicalBlend", "chemical-neutralizer-blend", "Chemical Neutralizer Blend", 2);
            SetPrivateField(typeof(ChemicalBlendTask), task, "requiredStepOrder", new[] { "prime", "stabilizer", "neutralizer", "flush" });
            SetPrivateField(typeof(ChemicalBlendTask), task, "stabilizedZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(ChemicalBlendTask), task, "failureZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(ChemicalBlendTask), task, "suppressedHazardsOnComplete", new[] { cargoLeak, reactorLeak });
            SetPrivateField(typeof(ChemicalBlendTask), task, "completionSuppressionSeconds", 46f);

            CreateOrUpdateTaskStep(root, "ChemicalBlend_Prime", task, "prime", "Add Primer", new Vector3(-18f, 1f, -13.2f), new Color(0.65f, 0.95f, 1f));
            CreateOrUpdateTaskStep(root, "ChemicalBlend_Stabilizer", task, "stabilizer", "Add Stabilizer", new Vector3(-15f, 1f, -13.2f), new Color(0.95f, 0.55f, 1f));
            CreateOrUpdateTaskStep(root, "ChemicalBlend_Neutralizer", task, "neutralizer", "Add Neutralizer", new Vector3(-12f, 1f, -13.2f), new Color(0.35f, 1f, 0.45f));
            CreateOrUpdateTaskStep(root, "ChemicalBlend_Flush", task, "flush", "Flush Blend", new Vector3(-9f, 1f, -13.2f), new Color(1f, 0.95f, 0.35f));
            task.transform.position = new Vector3(-13.5f, 1f, -13.2f);

            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigureHullPatchTask(Transform root, FloodZone mainZone, FloodZone lowZone)
        {
            NetworkBleachLeakHazard cargoLeak = FindHazardByName("BleachLeak_CargoMister");

            HullPatchTask task = EnsureAdvancedTaskRoot<HullPatchTask>(root, "AdvancedTask_HullPatch", "hull-seam-patch", "Hull Seam Patch", 2);
            SetPrivateField(typeof(HullPatchTask), task, "requiredStepOrder", new[] { "scrape", "foam", "plate", "seal" });
            SetPrivateField(typeof(HullPatchTask), task, "stabilizedZones", new[] { lowZone });
            SetPrivateField(typeof(HullPatchTask), task, "failureZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(HullPatchTask), task, "suppressedHazardsOnComplete", new[] { cargoLeak });
            SetPrivateField(typeof(HullPatchTask), task, "hazardSuppressionSeconds", 32f);
            SetPrivateField(typeof(HullPatchTask), task, "completeSetsSafe", true);

            CreateOrUpdateTaskStep(root, "HullPatch_Scrape", task, "scrape", "Scrape Seam", new Vector3(-20f, 1f, -4.5f), new Color(0.55f, 0.85f, 1f));
            CreateOrUpdateTaskStep(root, "HullPatch_Foam", task, "foam", "Inject Foam", new Vector3(-17f, 1f, -4.5f), new Color(0.82f, 0.92f, 1f));
            CreateOrUpdateTaskStep(root, "HullPatch_Plate", task, "plate", "Seat Patch Plate", new Vector3(-14f, 1f, -4.5f), new Color(0.85f, 0.85f, 0.95f));
            CreateOrUpdateTaskStep(root, "HullPatch_Seal", task, "seal", "Seal Patch", new Vector3(-11f, 1f, -4.5f), new Color(0.25f, 1f, 0.55f));
            task.transform.position = new Vector3(-15.5f, 1f, -4.5f);
            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigurePowerRelayTask(Transform root, FloodZone mainZone, FloodZone lowZone)
        {
            PowerRelayTask task = EnsureAdvancedTaskRoot<PowerRelayTask>(root, "AdvancedTask_PowerRelay", "power-relay-bank", "Power Relay Bank", 2);
            SetPrivateField(typeof(PowerRelayTask), task, "requiredStepOrder", new[] { "breaker_a", "breaker_c", "breaker_b", "main_bus" });
            SetPrivateField(typeof(PowerRelayTask), task, "stabilizedZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(PowerRelayTask), task, "failureZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(PowerRelayTask), task, "completionTimeBonusSeconds", 9f);
            SetPrivateField(typeof(PowerRelayTask), task, "failureTimePenaltySeconds", 4f);

            CreateOrUpdateTaskStep(root, "PowerRelay_A", task, "breaker_a", "Energize Breaker A", new Vector3(9f, 1f, 4.5f), new Color(1f, 0.72f, 0.22f));
            CreateOrUpdateTaskStep(root, "PowerRelay_B", task, "breaker_b", "Energize Breaker B", new Vector3(12f, 1f, 4.5f), new Color(0.42f, 0.95f, 1f));
            CreateOrUpdateTaskStep(root, "PowerRelay_C", task, "breaker_c", "Energize Breaker C", new Vector3(15f, 1f, 4.5f), new Color(0.95f, 0.35f, 1f));
            CreateOrUpdateTaskStep(root, "PowerRelay_MainBus", task, "main_bus", "Throw Main Bus", new Vector3(18f, 1f, 4.5f), new Color(1f, 1f, 0.42f));
            task.transform.position = new Vector3(13.5f, 1f, 4.5f);
            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigureCoolantRerouteTask(Transform root, FloodZone mainZone, FloodZone lowZone)
        {
            NetworkBleachLeakHazard cargoLeak = FindHazardByName("BleachLeak_CargoMister");
            NetworkBleachLeakHazard reactorLeak = FindHazardByName("BleachLeak_ReactorVent");

            CoolantRerouteTask task = EnsureAdvancedTaskRoot<CoolantRerouteTask>(root, "AdvancedTask_CoolantReroute", "coolant-reroute-loop", "Coolant Reroute Loop", 2);
            SetPrivateField(typeof(CoolantRerouteTask), task, "requiredStepOrder", new[] { "intake", "pump", "chiller", "return" });
            SetPrivateField(typeof(CoolantRerouteTask), task, "cooledZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(CoolantRerouteTask), task, "failureZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(CoolantRerouteTask), task, "suppressedHazardsOnComplete", new[] { cargoLeak, reactorLeak });
            SetPrivateField(typeof(CoolantRerouteTask), task, "hazardSuppressionSeconds", 26f);
            SetPrivateField(typeof(CoolantRerouteTask), task, "completionTimeBonusSeconds", 8f);
            SetPrivateField(typeof(CoolantRerouteTask), task, "failureTimePenaltySeconds", 5f);

            CreateOrUpdateTaskStep(root, "CoolantReroute_Intake", task, "intake", "Open Coolant Intake", new Vector3(-18f, 1f, 4.5f), new Color(0.2f, 0.85f, 1f));
            CreateOrUpdateTaskStep(root, "CoolantReroute_Pump", task, "pump", "Prime Coolant Pump", new Vector3(-15f, 1f, 4.5f), new Color(0.45f, 1f, 0.82f));
            CreateOrUpdateTaskStep(root, "CoolantReroute_Chiller", task, "chiller", "Open Chiller Coil", new Vector3(-12f, 1f, 4.5f), new Color(0.9f, 0.95f, 1f));
            CreateOrUpdateTaskStep(root, "CoolantReroute_Return", task, "return", "Seat Return Line", new Vector3(-9f, 1f, 4.5f), new Color(0.55f, 0.72f, 1f));
            task.transform.position = new Vector3(-13.5f, 1f, 4.5f);
            TrySpawnNetworkObject(task.gameObject);
        }

        private static void ConfigureOxygenPurgeTask(Transform root, FloodZone mainZone, FloodZone lowZone)
        {
            NetworkBleachLeakHazard cargoLeak = FindHazardByName("BleachLeak_CargoMister");
            NetworkBleachLeakHazard reactorLeak = FindHazardByName("BleachLeak_ReactorVent");

            OxygenPurgeTask task = EnsureAdvancedTaskRoot<OxygenPurgeTask>(root, "AdvancedTask_OxygenPurge", "oxygen-purge-loop", "Oxygen Purge Loop", 2);
            SetPrivateField(typeof(OxygenPurgeTask), task, "requiredStepOrder", new[] { "intake", "filter", "bleed", "reseal" });
            SetPrivateField(typeof(OxygenPurgeTask), task, "ventedZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(OxygenPurgeTask), task, "failureZones", new[] { mainZone, lowZone });
            SetPrivateField(typeof(OxygenPurgeTask), task, "suppressedHazardsOnComplete", new[] { cargoLeak, reactorLeak });
            SetPrivateField(typeof(OxygenPurgeTask), task, "hazardSuppressionSeconds", 28f);
            SetPrivateField(typeof(OxygenPurgeTask), task, "completionTimeBonusSeconds", 7f);
            SetPrivateField(typeof(OxygenPurgeTask), task, "failureTimePenaltySeconds", 4f);

            CreateOrUpdateTaskStep(root, "OxygenPurge_Intake", task, "intake", "Open Intake", new Vector3(-5f, 1f, 12.8f), new Color(0.75f, 0.95f, 1f));
            CreateOrUpdateTaskStep(root, "OxygenPurge_Filter", task, "filter", "Swap Filter", new Vector3(-2f, 1f, 12.8f), new Color(0.42f, 1f, 0.75f));
            CreateOrUpdateTaskStep(root, "OxygenPurge_Bleed", task, "bleed", "Bleed Line", new Vector3(1f, 1f, 12.8f), new Color(1f, 0.75f, 0.35f));
            CreateOrUpdateTaskStep(root, "OxygenPurge_Reseal", task, "reseal", "Reseal Intake", new Vector3(4f, 1f, 12.8f), new Color(1f, 0.38f, 0.58f));
            task.transform.position = new Vector3(-0.5f, 1f, 12.8f);
            TrySpawnNetworkObject(task.gameObject);
        }

        private static T EnsureAdvancedTaskRoot<T>(Transform root, string objectName, string taskId, string displayName, int maxFailuresBeforeLock) where T : TaskObjectiveBase
        {
            GameObject taskObject = GameObject.Find(objectName);
            if (taskObject == null)
            {
                taskObject = CreateAuthoritativeNetworkObject(root, objectName, Vector3.zero);
            }

            if (taskObject.transform.parent != root)
            {
                taskObject.transform.SetParent(root, true);
            }

            T task = GetOrAddComponent<T>(taskObject);
            SetPrivateField(typeof(TaskObjectiveBase), task, "taskId", taskId);
            SetPrivateField(typeof(TaskObjectiveBase), task, "displayName", displayName);
            SetPrivateField(typeof(TaskObjectiveBase), task, "maxFailuresBeforeLock", maxFailuresBeforeLock);
            SetPrivateField(typeof(TaskObjectiveBase), task, "interactReleaseTimeoutSeconds", 18f);
            return task;
        }

        private static void CreateOrUpdateTaskStep(
            Transform root,
            string objectName,
            TaskObjectiveBase ownerTask,
            string stepId,
            string prompt,
            Vector3 position,
            Color color)
        {
            GameObject stepObject = CreateAuthoritativePrimitive(root, objectName, PrimitiveType.Cube, position, new Vector3(1.05f, 1.35f, 1.05f));
            ApplyMaterial(stepObject, color, false);

            TaskStepInteractable interactable = GetOrAddComponent<TaskStepInteractable>(stepObject);
            SetPrivateField(typeof(TaskStepInteractable), interactable, "ownerTask", ownerTask);
            SetPrivateField(typeof(TaskStepInteractable), interactable, "stepId", stepId);
            SetPrivateField(typeof(TaskStepInteractable), interactable, "interactPrompt", prompt);

            BoxCollider collider = stepObject.GetComponent<BoxCollider>() ?? stepObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;

            EnsureStainReceiver(stepObject);
            TrySpawnNetworkObject(stepObject);
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
                GameObject zoneObject = CreateAuthoritativePrimitive(root, "FloodZone_Main", PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(16f, 1.2f, 10f));
                mainZone = GetOrAddComponent<FloodZone>(zoneObject);
                SetPrivateField(typeof(FloodZone), mainZone, "zoneId", "Flood_Main");
                SetPrivateField(typeof(FloodZone), mainZone, "initialState", FloodZoneState.Dry);
                TrySpawnNetworkObject(zoneObject);
            }

            if (lowZone == null)
            {
                GameObject zoneObject = CreateAuthoritativePrimitive(root, "FloodZone_LowArea", PrimitiveType.Cube, new Vector3(0f, -0.2f, -12f), new Vector3(8f, 1.2f, 4f));
                lowZone = GetOrAddComponent<FloodZone>(zoneObject);
                SetPrivateField(typeof(FloodZone), lowZone, "zoneId", "Flood_LowArea");
                SetPrivateField(typeof(FloodZone), lowZone, "initialState", FloodZoneState.Dry);
                TrySpawnNetworkObject(zoneObject);
            }

            ConfigureFloodZone(mainZone, "FloodZone_Main", new Vector3(0f, 0.6f, 0f), new Vector3(16f, 1.2f, 10f), new Color(0.10f, 0.35f, 0.95f, 0.22f));
            ConfigureFloodZone(lowZone, "FloodZone_LowArea", new Vector3(0f, -0.2f, -12f), new Vector3(8f, 1.2f, 4f), new Color(0.95f, 0.10f, 0.12f, 0.22f));
            SetPrivateField(typeof(FloodZone), mainZone, "initialState", FloodZoneState.Dry);
            SetPrivateField(typeof(FloodZone), lowZone, "initialState", FloodZoneState.Dry);
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

        private static void EnsureGravityPlayZones(Transform root)
        {
            ConfigureGravityField(
                root,
                "GravityField_LowG_ServiceTube",
                "Low-G Service Tube",
                new Vector3(-14f, 1.8f, -8f),
                new Vector3(7f, 4.2f, 5.5f),
                0.28f,
                true,
                1.35f,
                new Color(0.25f, 0.75f, 1f, 0.18f));

            ConfigureGravityField(
                root,
                "GravityField_ZeroG_ReactorWell",
                "Zero-G Reactor Well",
                new Vector3(14f, 2.1f, -8f),
                new Vector3(6.5f, 4.8f, 6.5f),
                0.04f,
                true,
                1.8f,
                new Color(0.82f, 0.34f, 1f, 0.20f));
        }

        private static void ConfigureGravityField(
            Transform root,
            string objectName,
            string displayName,
            Vector3 position,
            Vector3 scale,
            float gravityMultiplier,
            bool unlimitedWallStick,
            float paintIntensityMultiplier,
            Color color)
        {
            GameObject fieldObject = FindOrCreate(objectName, PrimitiveType.Cube, root);
            fieldObject.transform.position = position;
            fieldObject.transform.rotation = Quaternion.identity;
            fieldObject.transform.localScale = scale;

            ApplyMaterial(fieldObject, color, true);

            Collider collider = fieldObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }

            GravityFieldZone field = GetOrAddComponent<GravityFieldZone>(fieldObject);
            field.ConfigureRuntime(displayName, gravityMultiplier, unlimitedWallStick, paintIntensityMultiplier);
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

            if (FindObjectsByType<GravityFieldZone>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 gravity field zones for the Undertint movement slice.");
            }

            if (FindFirstObjectByType<NetworkRoundEventDirector>() == null)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Round event director missing; dynamic pressure events will not run.");
            }

            if (FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 decontamination stations for recovery counterplay.");
            }

            if (FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 floodgate stations for route pressure counterplay.");
            }

            if (FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 safe room beacons for refuge counterplay.");
            }

            if (FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 paint scanner stations for forensic counterplay.");
            }

            if (FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 vitals stations for deduction counterplay.");
            }

            if (FindObjectsByType<NetworkBleachVent>(FindObjectsSortMode.None).Length < 4)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 4 bleach vents for traversal deception.");
            }

            if (FindFirstObjectByType<NetworkEmergencyMeetingConsole>() == null)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Emergency meeting console missing; crew cannot call public meetings.");
            }

            if (FindObjectsByType<NetworkSlimeLaunchPad>(FindObjectsSortMode.None).Length < 4)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 4 slime launch pads for high-mobility routes.");
            }

            if (FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 security camera stations for map-readable deduction.");
            }

            if (FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None).Length < 3)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 3 alarm tripwires for area-denial deduction.");
            }

            if (FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 ink wells for recovery pacing.");
            }

            if (FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 false evidence stations for Bleach deception tools.");
            }

            if (FindObjectsByType<NetworkVotingPodium>(FindObjectsSortMode.None).Length < 3)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 3 voting podiums for meeting phase accusation/skip flow.");
            }

            if (FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 crew rally stations for group recovery moments.");
            }

            if (FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 bulkhead lock stations for route control.");
            }

            if (FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None).Length < 2)
            {
                Debug.LogWarning("GameplayBetaSceneInstaller: Expected 2 callout beacons for multiplayer communication substitutes.");
            }

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
            EnsureSolidCollider(ground);
            EnsureStainReceiver(ground);
        }

        private static GameObject EnsureBlock(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject block = FindOrCreate(name, PrimitiveType.Cube, parent);
            block.transform.position = position;
            block.transform.rotation = Quaternion.identity;
            block.transform.localScale = scale;
            ApplyMaterial(block, color, false);
            EnsureSolidCollider(block);
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

        private static void EnsureSolidCollider(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            Collider colliderRef = target.GetComponent<Collider>();
            if (colliderRef == null)
            {
                colliderRef = target.AddComponent<BoxCollider>();
            }

            colliderRef.enabled = true;
            colliderRef.isTrigger = false;
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

        private static NetworkBleachLeakHazard FindHazardByName(string objectName)
        {
            NetworkBleachLeakHazard[] hazards = FindObjectsByType<NetworkBleachLeakHazard>(FindObjectsSortMode.None);
            foreach (NetworkBleachLeakHazard hazard in hazards)
            {
                if (hazard != null && hazard.gameObject.name == objectName)
                {
                    return hazard;
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


        private static void EnsurePaintableSceneGeometry(Transform root)
        {
            GameObject[] sceneRoots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < sceneRoots.Length; rootIndex++)
            {
                Renderer[] renderers = sceneRoots[rootIndex].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer || renderer is SkinnedMeshRenderer)
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
        }

        private static void ConfigureSceneLighting()
        {
            Light directional = FindFirstObjectByType<Light>();
            if (directional != null && directional.type == LightType.Directional)
            {
                directional.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                directional.intensity = 1.15f;
                directional.color = new Color(0.78f, 0.86f, 1f);
            }

            RenderSettings.ambientLight = new Color(0.08f, 0.10f, 0.14f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.025f, 0.035f, 0.06f);
            RenderSettings.fogDensity = 0.018f;
        }

        private static void EnsureAtmosphereLights(Transform root)
        {
            EnsurePointLight(root, "Undertint_Light_Cyan_Core", new Vector3(0f, 5.5f, 0f), new Color(0.2f, 0.75f, 1f), 22f, 2.4f);
            EnsurePointLight(root, "Undertint_Light_Magenta_Reactor", new Vector3(14f, 4.2f, -8f), new Color(0.95f, 0.25f, 1f), 13f, 2.1f);
            EnsurePointLight(root, "Undertint_Light_Amber_Engine", new Vector3(-14f, 4.2f, 8f), new Color(1f, 0.52f, 0.18f), 13f, 1.8f);
            EnsurePointLight(root, "Undertint_Light_Red_LowArea", new Vector3(0f, 3.8f, -12f), new Color(1f, 0.15f, 0.12f), 14f, 1.6f);
        }

        private static void EnsurePointLight(Transform root, string objectName, Vector3 position, Color color, float range, float intensity)
        {
            GameObject lightObject = GameObject.Find(objectName);
            if (lightObject == null)
            {
                lightObject = new GameObject(objectName);
            }

            if (lightObject.transform.parent != root)
            {
                lightObject.transform.SetParent(root, true);
            }

            lightObject.transform.position = position;
            lightObject.transform.rotation = Quaternion.identity;

            Light light = lightObject.GetComponent<Light>();
            if (light == null)
            {
                light = lightObject.AddComponent<Light>();
            }

            light.type = LightType.Point;
            light.color = color;
            light.range = Mathf.Max(1f, range);
            light.intensity = Mathf.Max(0f, intensity);
            light.shadows = LightShadows.None;
        }

        private static bool HasCommandLineArg(string arg)
        {
            string[] args = System.Environment.GetCommandLineArgs();
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
