// File: Assets/_Project/Gameplay/Beta/BetaFloodWarningBeaconInstaller.cs
using HueDoneIt.Flood;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Ensures the flood warning-light product cue exists even if the scene installer did not wire warning lights.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaFloodWarningBeaconInstaller : MonoBehaviour
    {
        private const string RootName = "__BetaFloodWarningBeaconRig";

        private void Start()
        {
            if (FindObjectOfType<BetaFloodWarningLights>() != null)
            {
                return;
            }

            FloodSequenceController controller = FindObjectOfType<FloodSequenceController>();
            FloodZone[] zones = FindObjectsOfType<FloodZone>();
            if (controller == null && (zones == null || zones.Length == 0))
            {
                return;
            }

            GameObject root = new GameObject(RootName);
            BetaFloodWarningLights warningLights = root.AddComponent<BetaFloodWarningLights>();

            Light[] lights = new Light[6];
            Renderer[] renderers = new Renderer[6];
            Vector3[] positions =
            {
                new Vector3(-18f, 3.4f, -18f),
                new Vector3(18f, 3.4f, -18f),
                new Vector3(-18f, 3.4f, 18f),
                new Vector3(18f, 3.4f, 18f),
                new Vector3(0f, 3.6f, -23f),
                new Vector3(0f, 3.6f, 23f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                beacon.name = "Flood Warning Beacon " + (i + 1);
                beacon.transform.SetParent(root.transform, false);
                beacon.transform.position = positions[i];
                beacon.transform.localScale = Vector3.one * 0.7f;

                renderers[i] = beacon.GetComponent<Renderer>();

                Light light = beacon.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 11f;
                light.intensity = 0.35f;
                lights[i] = light;
            }

            AssignPrivateField(warningLights, "controller", controller);
            AssignPrivateField(warningLights, "observedZones", zones);
            AssignPrivateField(warningLights, "warningLights", lights);
            AssignPrivateField(warningLights, "warningRenderers", renderers);
        }

        private static void AssignPrivateField<T>(object target, string fieldName, T value)
        {
            if (target == null)
            {
                return;
            }

            System.Reflection.FieldInfo field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}
