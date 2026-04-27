// File: Assets/_Project/Gameplay/Beta/BetaProductionMapPolisher.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Adds intentional, readable route geometry and landmarks over the current beta-generated layout.
    /// The goal is not final map art; it is to make the test build traversable and understandable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaProductionMapPolisher : MonoBehaviour
    {
        private const string RootName = "__BetaIntentionalMapLayer";

        [SerializeField] private bool installRouteSlabs = true;
        [SerializeField] private bool installLandmarks = true;
        [SerializeField] private bool hideObviousGeneratedClutter = true;

        private void Start()
        {
            InstallOnce();
        }

        private void InstallOnce()
        {
            if (GameObject.Find(RootName) != null)
            {
                return;
            }

            if (hideObviousGeneratedClutter)
            {
                HideClutter();
            }

            GameObject root = new GameObject(RootName);
            if (installRouteSlabs)
            {
                CreateRouteSlabs(root.transform);
            }

            if (installLandmarks)
            {
                CreateLandmarks(root.transform);
            }
        }

        private void CreateRouteSlabs(Transform parent)
        {
            Material laneMaterial = CreateMaterial("BetaRouteLaneMaterial", new Color(0.055f, 0.06f, 0.075f, 1f));
            Material safeMaterial = CreateMaterial("BetaSafeHubMaterial", new Color(0.08f, 0.09f, 0.12f, 1f));
            Material warningMaterial = CreateMaterial("BetaFloodWarningFloorMaterial", new Color(0.25f, 0.12f, 0.03f, 1f));

            CreateSlab(parent, "Main Fore/Aft Lane", new Vector3(0f, 0.035f, 0f), new Vector3(5.2f, 0.08f, 42f), laneMaterial);
            CreateSlab(parent, "Main Port/Starboard Lane", new Vector3(0f, 0.045f, 0f), new Vector3(42f, 0.08f, 5.2f), laneMaterial);
            CreateSlab(parent, "North Task Pocket", new Vector3(0f, 0.05f, 14f), new Vector3(22f, 0.08f, 6.5f), safeMaterial);
            CreateSlab(parent, "South Task Pocket", new Vector3(0f, 0.05f, -14f), new Vector3(22f, 0.08f, 6.5f), safeMaterial);
            CreateSlab(parent, "West Bypass", new Vector3(-15.5f, 0.04f, 0f), new Vector3(5.2f, 0.08f, 30f), laneMaterial);
            CreateSlab(parent, "East Bypass", new Vector3(15.5f, 0.04f, 0f), new Vector3(5.2f, 0.08f, 30f), laneMaterial);
            CreateSlab(parent, "Flood Release Apron", new Vector3(0f, 0.065f, -21f), new Vector3(24f, 0.08f, 4.25f), warningMaterial);
        }

        private void CreateLandmarks(Transform parent)
        {
            CreateBeacon(parent, "FORE CAMERA", new Vector3(0f, 2.2f, 21f), new Color(0.1f, 0.75f, 1f, 1f));
            CreateBeacon(parent, "AFT FLOOD RELEASE", new Vector3(0f, 2.2f, -21f), new Color(1f, 0.42f, 0.08f, 1f));
            CreateBeacon(parent, "PORT TASKS", new Vector3(-21f, 2.2f, 0f), new Color(1f, 0.1f, 0.85f, 1f));
            CreateBeacon(parent, "STARBOARD TASKS", new Vector3(21f, 2.2f, 0f), new Color(0.22f, 1f, 0.28f, 1f));
            CreateBeacon(parent, "MEETING / HUB", new Vector3(0f, 2.8f, 0f), new Color(0.96f, 0.95f, 0.22f, 1f));
        }

        private void CreateSlab(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.position = position;
            slab.transform.localScale = scale;
            slab.layer = LayerMask.NameToLayer("Default");

            Renderer renderer = slab.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private void CreateBeacon(Transform parent, string label, Vector3 position, Color color)
        {
            GameObject root = new GameObject(label);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "BeaconBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.35f, 1.2f, 0.35f);

            Renderer renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateMaterial(label + " Material", color);
            }

            Light light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 8f;
            light.intensity = 2.2f;

            GameObject textObject = new GameObject("Label");
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.22f;
            text.fontSize = 52;
            text.color = color;
        }

        private Material CreateMaterial(string name, Color color)
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

            return material;
        }

        private void HideClutter()
        {
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            for (int i = 0; i < allObjects.Length; i++)
            {
                GameObject obj = allObjects[i];
                if (obj == null)
                {
                    continue;
                }

                string lower = obj.name.ToLowerInvariant();
                bool looksLikeClutter = lower.Contains("clutter") ||
                                        lower.Contains("debris") ||
                                        lower.Contains("filler") ||
                                        lower.Contains("junk");
                bool isImportant = lower.Contains("task") ||
                                   lower.Contains("spawn") ||
                                   lower.Contains("flood") ||
                                   lower.Contains("camera") ||
                                   lower.Contains("scanner") ||
                                   lower.Contains("sabotage") ||
                                   lower.Contains("network");

                if (looksLikeClutter && !isImportant)
                {
                    obj.SetActive(false);
                }
            }
        }
    }
}
