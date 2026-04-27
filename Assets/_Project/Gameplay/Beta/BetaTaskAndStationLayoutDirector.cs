// File: Assets/_Project/Gameplay/Beta/BetaTaskAndStationLayoutDirector.cs
using System;
using System.Collections;
using System.Collections.Generic;
using HueDoneIt.Gameplay.Environment;
using HueDoneIt.Gameplay.Sabotage;
using HueDoneIt.Tasks;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Runtime beta layout pass. The generated slice has enough systems now, but too many spawn/task/station
    /// positions are arbitrary. This pass moves interactables into readable zones around the clean map layer.
    /// It intentionally runs on every peer so static/networked interactable visuals line up for playtesting.
    /// </summary>
    [DefaultExecutionOrder(-760)]
    [DisallowMultipleComponent]
    public sealed class BetaTaskAndStationLayoutDirector : MonoBehaviour
    {
        private const string RootName = "__BetaTaskAndStationLayout";
        private const float SurfaceY = 0.78f;
        private const float PadY = 0.075f;

        private static readonly Vector3[] SpawnSlots =
        {
            new(-4.5f, SurfaceY, -4.5f),
            new(4.5f, SurfaceY, -4.5f),
            new(-4.5f, SurfaceY, 4.5f),
            new(4.5f, SurfaceY, 4.5f),
            new(0f, SurfaceY, -6.25f),
            new(0f, SurfaceY, 6.25f),
            new(-6.25f, SurfaceY, 0f),
            new(6.25f, SurfaceY, 0f),
            new(-7.2f, SurfaceY, -7.2f),
            new(7.2f, SurfaceY, 7.2f)
        };

        private static readonly Vector3[] RepairTaskSlots =
        {
            new(0f, SurfaceY, 31f),
            new(-31f, SurfaceY, 0f),
            new(31f, SurfaceY, 0f),
            new(0f, SurfaceY, -31f),
            new(-17f, SurfaceY, 17f),
            new(17f, SurfaceY, 17f),
            new(-17f, SurfaceY, -17f),
            new(17f, SurfaceY, -17f),
            new(-28f, SurfaceY, 8f),
            new(28f, SurfaceY, -8f)
        };

        private static readonly Vector3[] ObjectiveTaskSlots =
        {
            new(-7f, SurfaceY, 31f),
            new(7f, SurfaceY, 31f),
            new(-31f, SurfaceY, -7f),
            new(31f, SurfaceY, 7f),
            new(-7f, SurfaceY, -31f),
            new(7f, SurfaceY, -31f)
        };

        private static readonly Vector3[] HubUtilitySlots =
        {
            new(-7f, SurfaceY, -2.5f),
            new(7f, SurfaceY, -2.5f),
            new(-7f, SurfaceY, 2.5f),
            new(7f, SurfaceY, 2.5f),
            new(0f, SurfaceY, 7.4f),
            new(0f, SurfaceY, -7.4f)
        };

        private static readonly Vector3[] ForeUtilitySlots =
        {
            new(-5.5f, SurfaceY, 33.5f),
            new(5.5f, SurfaceY, 33.5f),
            new(0f, SurfaceY, 27.5f),
            new(-10f, SurfaceY, 27.5f),
            new(10f, SurfaceY, 27.5f)
        };

        private static readonly Vector3[] AftUtilitySlots =
        {
            new(-5.5f, SurfaceY, -33.5f),
            new(5.5f, SurfaceY, -33.5f),
            new(0f, SurfaceY, -27.5f),
            new(-10f, SurfaceY, -27.5f),
            new(10f, SurfaceY, -27.5f)
        };

        private static readonly Vector3[] PortUtilitySlots =
        {
            new(-33.5f, SurfaceY, -5.5f),
            new(-33.5f, SurfaceY, 5.5f),
            new(-27.5f, SurfaceY, 0f),
            new(-27.5f, SurfaceY, -10f),
            new(-27.5f, SurfaceY, 10f)
        };

        private static readonly Vector3[] StarboardUtilitySlots =
        {
            new(33.5f, SurfaceY, -5.5f),
            new(33.5f, SurfaceY, 5.5f),
            new(27.5f, SurfaceY, 0f),
            new(27.5f, SurfaceY, -10f),
            new(27.5f, SurfaceY, 10f)
        };

        [SerializeField] private bool moveSpawnPoints = true;
        [SerializeField] private bool moveTasks = true;
        [SerializeField] private bool moveUtilityStations = true;
        [SerializeField] private bool createGroundPads = true;
        [SerializeField, Min(0.1f)] private float firstPassDelaySeconds = 0.15f;
        [SerializeField, Min(0.1f)] private float retryDelaySeconds = 1.0f;
        [SerializeField, Min(1)] private int retryPassCount = 4;

        private Transform _root;
        private Material _taskPadMaterial;
        private Material _utilityPadMaterial;
        private Material _spawnPadMaterial;
        private readonly HashSet<int> _paddedObjects = new();

        private void Start()
        {
            StartCoroutine(ApplyRepeatedly());
        }

        private IEnumerator ApplyRepeatedly()
        {
            yield return new WaitForSeconds(firstPassDelaySeconds);
            for (int i = 0; i < retryPassCount; i++)
            {
                ApplyLayout();
                yield return new WaitForSeconds(retryDelaySeconds);
            }
        }

        private void ApplyLayout()
        {
            EnsureRootAndMaterials();

            if (moveSpawnPoints)
            {
                MoveSpawnPoints();
            }

            if (moveTasks)
            {
                LayoutRepairTasks();
                LayoutObjectiveTasks();
            }

            if (moveUtilityStations)
            {
                LayoutUtilities();
            }
        }

        private void EnsureRootAndMaterials()
        {
            if (_root == null)
            {
                GameObject existing = GameObject.Find(RootName);
                if (existing == null)
                {
                    existing = new GameObject(RootName);
                }

                _root = existing.transform;
            }

            _taskPadMaterial ??= CreateMaterial("HDI Beta Task Pad", new Color(0.10f, 0.12f, 0.18f, 1f));
            _utilityPadMaterial ??= CreateMaterial("HDI Beta Utility Pad", new Color(0.08f, 0.10f, 0.13f, 1f));
            _spawnPadMaterial ??= CreateMaterial("HDI Beta Spawn Pad", new Color(0.05f, 0.18f, 0.16f, 1f));
        }

        private void MoveSpawnPoints()
        {
            List<Transform> spawns = new();
            Transform[] all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t != null && t.name.StartsWith("SpawnPoint_", StringComparison.Ordinal))
                {
                    spawns.Add(t);
                }
            }

            spawns.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < spawns.Count; i++)
            {
                Vector3 slot = SpawnSlots[i % SpawnSlots.Length];
                spawns[i].SetPositionAndRotation(slot, Quaternion.LookRotation((-slot).normalized, Vector3.up));
                if (createGroundPads)
                {
                    EnsurePad("Spawn Pad " + i, slot, new Vector3(2.6f, 0.07f, 2.6f), _spawnPadMaterial, spawns[i].GetInstanceID());
                }
            }
        }

        private void LayoutRepairTasks()
        {
            NetworkRepairTask[] tasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            Array.Sort(tasks, CompareRepairTask);
            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null)
                {
                    continue;
                }

                Vector3 slot = RepairTaskSlots[i % RepairTaskSlots.Length];
                PlaceObject(task.transform, slot);
                if (createGroundPads)
                {
                    EnsurePad("Task Pad " + task.DisplayName, slot, new Vector3(5.4f, 0.08f, 5.4f), _taskPadMaterial, task.GetInstanceID());
                }
            }
        }

        private void LayoutObjectiveTasks()
        {
            TaskObjectiveBase[] tasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            Array.Sort(tasks, CompareObjectiveTask);
            int placed = 0;
            for (int i = 0; i < tasks.Length; i++)
            {
                TaskObjectiveBase task = tasks[i];
                if (task == null || task.GetComponent<NetworkRepairTask>() != null)
                {
                    continue;
                }

                Vector3 slot = ObjectiveTaskSlots[placed % ObjectiveTaskSlots.Length] + new Vector3(0f, 0f, (placed / ObjectiveTaskSlots.Length) * 2.25f);
                PlaceObject(task.transform, slot);
                if (createGroundPads)
                {
                    EnsurePad("Objective Pad " + task.DisplayName, slot, new Vector3(4.8f, 0.08f, 4.8f), _taskPadMaterial, task.GetInstanceID());
                }

                placed++;
            }
        }

        private void LayoutUtilities()
        {
            LayoutComponents(FindObjectsByType<NetworkDecontaminationStation>(FindObjectsSortMode.None), HubUtilitySlots, "Decon");
            LayoutComponents(FindObjectsByType<NetworkInkWellStation>(FindObjectsSortMode.None), HubUtilitySlots, "InkWell", 2);
            LayoutComponents(FindObjectsByType<NetworkCrewRallyStation>(FindObjectsSortMode.None), HubUtilitySlots, "Rally", 4);
            LayoutComponents(FindObjectsByType<NetworkVitalsStation>(FindObjectsSortMode.None), HubUtilitySlots, "Vitals", 5);

            LayoutComponents(FindObjectsByType<NetworkPaintScannerStation>(FindObjectsSortMode.None), ForeUtilitySlots, "Scanner");
            LayoutComponents(FindObjectsByType<NetworkSecurityCameraStation>(FindObjectsSortMode.None), ForeUtilitySlots, "Camera", 1);
            LayoutComponents(FindObjectsByType<NetworkCalloutBeacon>(FindObjectsSortMode.None), ForeUtilitySlots, "Callout", 2);

            LayoutComponents(FindObjectsByType<NetworkFloodgateStation>(FindObjectsSortMode.None), AftUtilitySlots, "Floodgate");
            LayoutComponents(FindObjectsByType<NetworkEmergencySealStation>(FindObjectsSortMode.None), AftUtilitySlots, "Seal", 1);
            LayoutComponents(FindObjectsByType<NetworkSafeRoomBeacon>(FindObjectsSortMode.None), AftUtilitySlots, "SafeRoom", 2);

            LayoutComponents(FindObjectsByType<NetworkSabotageConsole>(FindObjectsSortMode.None), PortUtilitySlots, "Sabotage");
            LayoutComponents(FindObjectsByType<NetworkFalseEvidenceStation>(FindObjectsSortMode.None), PortUtilitySlots, "Smear", 1);
            LayoutComponents(FindObjectsByType<NetworkBulkheadLockStation>(FindObjectsSortMode.None), PortUtilitySlots, "Bulkhead", 2);

            LayoutComponents(FindObjectsByType<NetworkSlimeLaunchPad>(FindObjectsSortMode.None), StarboardUtilitySlots, "Launch");
            LayoutComponents(FindObjectsByType<NetworkAlarmTripwire>(FindObjectsSortMode.None), StarboardUtilitySlots, "Tripwire", 1);
        }

        private void LayoutComponents<T>(T[] components, Vector3[] slots, string label, int slotOffset = 0) where T : Component
        {
            if (components == null || components.Length == 0 || slots == null || slots.Length == 0)
            {
                return;
            }

            Array.Sort(components, (a, b) => CompareComponentName(a, b));
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (component == null)
                {
                    continue;
                }

                Vector3 slot = slots[(i + slotOffset) % slots.Length];
                PlaceObject(component.transform, slot);
                if (createGroundPads)
                {
                    EnsurePad(label + " Pad " + i, slot, new Vector3(3.4f, 0.07f, 3.4f), _utilityPadMaterial, component.GetInstanceID());
                }
            }
        }

        private void PlaceObject(Transform target, Vector3 position)
        {
            if (target == null)
            {
                return;
            }

            target.position = position;
            Vector3 toHub = Vector3.zero - position;
            toHub.y = 0f;
            if (toHub.sqrMagnitude > 0.001f)
            {
                target.rotation = Quaternion.LookRotation(toHub.normalized, Vector3.up);
            }
        }

        private void EnsurePad(string name, Vector3 center, Vector3 scale, Material material, int key)
        {
            if (_paddedObjects.Contains(key))
            {
                return;
            }

            _paddedObjects.Add(key);
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = SanitizeName(name);
            pad.transform.SetParent(_root, false);
            pad.transform.position = new Vector3(center.x, PadY, center.z);
            pad.transform.localScale = scale;

            Renderer renderer = pad.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static int CompareRepairTask(NetworkRepairTask a, NetworkRepairTask b)
        {
            string left = a != null ? a.DisplayName : string.Empty;
            string right = b != null ? b.DisplayName : string.Empty;
            int result = string.Compare(left, right, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            return (a != null ? a.GetInstanceID() : 0).CompareTo(b != null ? b.GetInstanceID() : 0);
        }

        private static int CompareObjectiveTask(TaskObjectiveBase a, TaskObjectiveBase b)
        {
            string left = a != null ? a.DisplayName : string.Empty;
            string right = b != null ? b.DisplayName : string.Empty;
            int result = string.Compare(left, right, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            return (a != null ? a.GetInstanceID() : 0).CompareTo(b != null ? b.GetInstanceID() : 0);
        }

        private static int CompareComponentName(Component a, Component b)
        {
            return string.Compare(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty, StringComparison.Ordinal);
        }

        private static Material CreateMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Layout Pad";
            }

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value;
        }
    }
}
