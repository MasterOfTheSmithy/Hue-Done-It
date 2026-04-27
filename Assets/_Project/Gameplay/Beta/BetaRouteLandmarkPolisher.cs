// File: Assets/_Project/Gameplay/Beta/BetaRouteLandmarkPolisher.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Adds readable route signage and trim lights to the generated beta map without requiring authored prefab hookup.
    /// This is a temporary production scaffold: it makes the current map legible while the final map is still evolving.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaRouteLandmarkPolisher : MonoBehaviour
    {
        private const string RootName = "__BetaRouteLandmarkPolish";

        [SerializeField] private bool installOnStart = true;
        [SerializeField] private Color foreColor = new Color(0.1f, 0.75f, 1f, 1f);
        [SerializeField] private Color aftColor = new Color(1f, 0.34f, 0.16f, 1f);
        [SerializeField] private Color portColor = new Color(1f, 0.18f, 0.82f, 1f);
        [SerializeField] private Color starboardColor = new Color(0.2f, 1f, 0.38f, 1f);
        [SerializeField] private Color hubColor = new Color(1f, 0.92f, 0.2f, 1f);

        private bool _installed;

        private void Start()
        {
            if (installOnStart)
            {
                Install();
            }
        }

        private void Install()
        {
            if (_installed || GameObject.Find(RootName) != null)
            {
                _installed = true;
                return;
            }

            _installed = true;
            GameObject root = new GameObject(RootName);

            CreateLandmark(root.transform, "FORE CAMERA ROUTE", new Vector3(0f, 2.6f, 19.5f), foreColor);
            CreateLandmark(root.transform, "AFT FLOOD RELEASE", new Vector3(0f, 2.6f, -19.5f), aftColor);
            CreateLandmark(root.transform, "PORT TASK LOOP", new Vector3(-19.5f, 2.6f, 0f), portColor);
            CreateLandmark(root.transform, "STARBOARD TASK LOOP", new Vector3(19.5f, 2.6f, 0f), starboardColor);
            CreateLandmark(root.transform, "MEETING / HUB", new Vector3(0f, 3.1f, 0f), hubColor);

            CreateLane(root.transform, "ForeAftLane", new Vector3(0f, 0.035f, 0f), new Vector3(2.1f, 0.018f, 42f), hubColor);
            CreateLane(root.transform, "PortStarboardLane", new Vector3(0f, 0.04f, 0f), new Vector3(42f, 0.018f, 2.1f), hubColor);
            CreateLane(root.transform, "PortLoopLane", new Vector3(-12f, 0.045f, 0f), new Vector3(1.3f, 0.018f, 32f), portColor);
            CreateLane(root.transform, "StarboardLoopLane", new Vector3(12f, 0.045f, 0f), new Vector3(1.3f, 0.018f, 32f), starboardColor);
        }

        private static void CreateLandmark(Transform parent, string label, Vector3 position, Color color)
        {
            GameObject root = new GameObject(label);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            GameObject backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "Panel";
            backing.transform.SetParent(root.transform, false);
            backing.transform.localScale = new Vector3(5.8f, 0.14f, 1.15f);
            ApplyRendererColor(backing.GetComponent<Renderer>(), Color.Lerp(Color.black, color, 0.34f));
            Collider collider = backing.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            GameObject textObject = new GameObject("Label");
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0.18f, -0.62f);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 44;
            text.characterSize = 0.13f;
            text.color = Color.Lerp(Color.white, color, 0.25f);

            Light light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 1.35f;
            light.range = 8.5f;
        }

        private static void CreateLane(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject lane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lane.name = name;
            lane.transform.SetParent(parent, false);
            lane.transform.position = position;
            lane.transform.localScale = scale;
            ApplyRendererColor(lane.GetComponent<Renderer>(), new Color(color.r, color.g, color.b, 0.36f));
            Collider collider = lane.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private static void ApplyRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = "BetaRouteLandmarkMaterial";
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            renderer.sharedMaterial = material;
        }
    }
}
