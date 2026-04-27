// File: Assets/_Project/Gameplay/Beta/BetaPlayableMapDirector.cs
using HueDoneIt.Flood;
using HueDoneIt.Tasks;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(-820)]
    [DisallowMultipleComponent]
    public sealed class BetaPlayableMapDirector : MonoBehaviour
    {
        private const string RootName = "__BetaPlayableMapLayer";
        private static readonly string[] OldLayerRoots = { "__BetaIntentionalMapLayer", "__BetaRouteLandmarkPolish" };

        [SerializeField] private bool destroyOldBetaRouteLayers = true;
        [SerializeField] private bool installCleanRouteLayer = true;
        [SerializeField] private bool addTaskSupportPads = true;
        [SerializeField] private bool hideNonGameplayClutter = true;

        private Material _hubMaterial;
        private Material _laneMaterial;
        private Material _taskPocketMaterial;
        private Material _floodMaterial;
        private Material _wallMaterial;
        private Material _trimBlue;
        private Material _trimPink;
        private Material _trimYellow;
        private Material _trimGreen;

        private void Start()
        {
            Install();
        }

        private void Install()
        {
            if (destroyOldBetaRouteLayers)
            {
                DestroyOldLayers();
            }

            if (hideNonGameplayClutter)
            {
                HideClutter();
            }

            CreateMaterials();
            DisableSupersededRouteComponents();

            GameObject root = GameObject.Find(RootName);
            if (root == null && installCleanRouteLayer)
            {
                root = new GameObject(RootName);
                BuildReadableMap(root.transform);
            }

            if (addTaskSupportPads)
            {
                CreateTaskSupportPads(root != null ? root.transform : null);
            }
        }


        private static void DisableSupersededRouteComponents()
        {
            BetaProductionMapPolisher[] oldMapPolishers = FindObjectsByType<BetaProductionMapPolisher>(FindObjectsSortMode.None);
            for (int i = 0; i < oldMapPolishers.Length; i++)
            {
                if (oldMapPolishers[i] != null)
                {
                    oldMapPolishers[i].enabled = false;
                }
            }

            BetaRouteLandmarkPolisher[] oldLandmarkPolishers = FindObjectsByType<BetaRouteLandmarkPolisher>(FindObjectsSortMode.None);
            for (int i = 0; i < oldLandmarkPolishers.Length; i++)
            {
                if (oldLandmarkPolishers[i] != null)
                {
                    oldLandmarkPolishers[i].enabled = false;
                }
            }
        }

        private void DestroyOldLayers()
        {
            for (int i = 0; i < OldLayerRoots.Length; i++)
            {
                GameObject old = GameObject.Find(OldLayerRoots[i]);
                if (old != null)
                {
                    Destroy(old);
                }
            }
        }

        private void CreateMaterials()
        {
            _hubMaterial = CreateMaterial("HDI Beta Hub Floor", new Color(0.075f, 0.075f, 0.095f, 1f));
            _laneMaterial = CreateMaterial("HDI Beta Main Lane", new Color(0.045f, 0.052f, 0.066f, 1f));
            _taskPocketMaterial = CreateMaterial("HDI Beta Task Pocket", new Color(0.065f, 0.072f, 0.095f, 1f));
            _floodMaterial = CreateMaterial("HDI Beta Flood Floor", new Color(0.20f, 0.085f, 0.035f, 1f));
            _wallMaterial = CreateMaterial("HDI Beta Low Wall", new Color(0.030f, 0.034f, 0.045f, 1f));
            _trimBlue = CreateMaterial("HDI Beta Cyan Trim", new Color(0.05f, 0.70f, 1f, 1f));
            _trimPink = CreateMaterial("HDI Beta Pink Trim", new Color(1f, 0.15f, 0.58f, 1f));
            _trimYellow = CreateMaterial("HDI Beta Yellow Trim", new Color(1f, 0.82f, 0.10f, 1f));
            _trimGreen = CreateMaterial("HDI Beta Green Trim", new Color(0.45f, 1f, 0.16f, 1f));
        }

        private void BuildReadableMap(Transform parent)
        {
            CreateWalkable(parent, "MEETING HUB FLOOR", new Vector3(0f, 0.02f, 0f), new Vector3(16f, 0.10f, 16f), _hubMaterial);
            CreateWalkable(parent, "FORE MAIN LANE FLOOR", new Vector3(0f, 0.025f, 17f), new Vector3(8f, 0.10f, 30f), _laneMaterial);
            CreateWalkable(parent, "AFT MAIN LANE FLOOR", new Vector3(0f, 0.025f, -17f), new Vector3(8f, 0.10f, 30f), _laneMaterial);
            CreateWalkable(parent, "PORT MAIN LANE FLOOR", new Vector3(-17f, 0.025f, 0f), new Vector3(30f, 0.10f, 8f), _laneMaterial);
            CreateWalkable(parent, "STARBOARD MAIN LANE FLOOR", new Vector3(17f, 0.025f, 0f), new Vector3(30f, 0.10f, 8f), _laneMaterial);

            CreateWalkable(parent, "FORE TASK POCKET FLOOR", new Vector3(0f, 0.03f, 31f), new Vector3(18f, 0.10f, 9f), _taskPocketMaterial);
            CreateWalkable(parent, "AFT FLOOD RELEASE FLOOR", new Vector3(0f, 0.03f, -31f), new Vector3(18f, 0.10f, 9f), _floodMaterial);
            CreateWalkable(parent, "PORT TASK POCKET FLOOR", new Vector3(-31f, 0.03f, 0f), new Vector3(9f, 0.10f, 18f), _taskPocketMaterial);
            CreateWalkable(parent, "STARBOARD TASK POCKET FLOOR", new Vector3(31f, 0.03f, 0f), new Vector3(9f, 0.10f, 18f), _taskPocketMaterial);

            CreateWalkable(parent, "NORTH LOOP FLOOR", new Vector3(17f, 0.022f, 17f), new Vector3(8f, 0.10f, 22f), _laneMaterial);
            CreateWalkable(parent, "SOUTH LOOP FLOOR", new Vector3(-17f, 0.022f, -17f), new Vector3(8f, 0.10f, 22f), _laneMaterial);
            CreateWalkable(parent, "WEST LOOP FLOOR", new Vector3(-17f, 0.024f, 17f), new Vector3(22f, 0.10f, 8f), _laneMaterial);
            CreateWalkable(parent, "EAST LOOP FLOOR", new Vector3(17f, 0.024f, -17f), new Vector3(22f, 0.10f, 8f), _laneMaterial);

            CreateRail(parent, "FORE OUTER RAIL", new Vector3(0f, 0.55f, 36.4f), new Vector3(24f, 0.80f, 0.45f));
            CreateRail(parent, "AFT OUTER RAIL", new Vector3(0f, 0.55f, -36.4f), new Vector3(24f, 0.80f, 0.45f));
            CreateRail(parent, "PORT OUTER RAIL", new Vector3(-36.4f, 0.55f, 0f), new Vector3(0.45f, 0.80f, 24f));
            CreateRail(parent, "STARBOARD OUTER RAIL", new Vector3(36.4f, 0.55f, 0f), new Vector3(0.45f, 0.80f, 24f));

            CreateTrim(parent, "FORE CYAN ROUTE", new Vector3(0f, 0.105f, 14f), new Vector3(1.15f, 0.018f, 26f), _trimBlue);
            CreateTrim(parent, "AFT YELLOW FLOOD ROUTE", new Vector3(0f, 0.11f, -14f), new Vector3(1.15f, 0.018f, 26f), _trimYellow);
            CreateTrim(parent, "PORT PINK ROUTE", new Vector3(-14f, 0.115f, 0f), new Vector3(26f, 0.018f, 1.15f), _trimPink);
            CreateTrim(parent, "STARBOARD GREEN ROUTE", new Vector3(14f, 0.115f, 0f), new Vector3(26f, 0.018f, 1.15f), _trimGreen);

            CreateLabel(parent, "MEETING / HUB", new Vector3(0f, 2.25f, 0f), _trimYellow);
            CreateLabel(parent, "FORE: SCAN / CAMERA", new Vector3(0f, 2.35f, 32f), _trimBlue);
            CreateLabel(parent, "AFT: FLOOD RELEASE", new Vector3(0f, 2.35f, -32f), _trimYellow);
            CreateLabel(parent, "PORT TASK LOOP", new Vector3(-32f, 2.35f, 0f), _trimPink);
            CreateLabel(parent, "STARBOARD TASK LOOP", new Vector3(32f, 2.35f, 0f), _trimGreen);
        }

        private void CreateTaskSupportPads(Transform parent)
        {
            if (parent == null)
            {
                GameObject root = GameObject.Find(RootName) ?? new GameObject(RootName);
                parent = root.transform;
            }

            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task == null)
                {
                    continue;
                }

                string name = "Task Support Pad - " + task.DisplayName;
                if (GameObject.Find(name) != null)
                {
                    continue;
                }

                Vector3 pos = task.transform.position;
                GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = name;
                pad.transform.SetParent(parent, false);
                pad.transform.position = new Vector3(pos.x, 0.045f, pos.z);
                pad.transform.localScale = new Vector3(5.0f, 0.08f, 5.0f);
                ApplyMaterial(pad, task.CurrentState == RepairTaskState.Locked ? _floodMaterial : _taskPocketMaterial);
            }
        }

        private GameObject CreateWalkable(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.layer = LayerMask.NameToLayer("Default");
            ApplyMaterial(go, material);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = false;
            }
            return go;
        }

        private void CreateRail(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            CreateWalkable(parent, name, position, scale, _wallMaterial);
        }

        private void CreateTrim(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trim.name = name;
            trim.transform.SetParent(parent, false);
            trim.transform.position = position;
            trim.transform.localScale = scale;
            ApplyMaterial(trim, material);
            Collider collider = trim.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void CreateLabel(Transform parent, string text, Vector3 position, Material accentMaterial)
        {
            GameObject root = new GameObject("Label - " + text);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            TextMesh label = root.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.18f;
            label.fontSize = 48;
            label.color = accentMaterial != null ? accentMaterial.color : Color.white;
            root.AddComponent<BillboardToCamera>();
        }

        private void HideClutter()
        {
            GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject obj = objects[i];
                if (obj == null)
                {
                    continue;
                }

                string lower = obj.name.ToLowerInvariant();
                bool looksLikeClutter = lower.Contains("clutter") ||
                                        lower.Contains("debris") ||
                                        lower.Contains("filler") ||
                                        lower.Contains("junk") ||
                                        lower.Contains("loose prop") ||
                                        lower.Contains("decor");

                bool important = lower.Contains("task") ||
                                 lower.Contains("spawn") ||
                                 lower.Contains("player") ||
                                 lower.Contains("network") ||
                                 lower.Contains("flood") ||
                                 lower.Contains("camera") ||
                                 lower.Contains("scanner") ||
                                 lower.Contains("sabotage") ||
                                 lower.Contains("meeting") ||
                                 lower.Contains("lobby");

                if (looksLikeClutter && !important)
                {
                    obj.SetActive(false);
                }
            }
        }

        private static Material CreateMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = name;
            material.color = color;
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

        private static void ApplyMaterial(GameObject go, Material material)
        {
            Renderer renderer = go != null ? go.GetComponent<Renderer>() : null;
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private sealed class BillboardToCamera : MonoBehaviour
        {
            private void LateUpdate()
            {
                Camera cameraRef = Camera.main;
                if (cameraRef == null)
                {
                    return;
                }

                Vector3 toCamera = transform.position - cameraRef.transform.position;
                toCamera.y = 0f;
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }
            }
        }
    }
}
