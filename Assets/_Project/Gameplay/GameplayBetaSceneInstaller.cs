// File: Assets/_Project/Gameplay/GameplayBetaSceneInstaller.cs
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Tasks;
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

            GameObject go = new(nameof(GameplayBetaSceneInstaller));
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<GameplayBetaSceneInstaller>();
        }

        private void Start()
        {
            if (SceneManager.GetActiveScene().name != GameplaySceneName)
            {
                return;
            }

            EnsureArena();
            ConfigureGameplayObjects();
        }

        private void EnsureArena()
        {
            if (GameObject.Find(RuntimeRootName) != null)
            {
                return;
            }

            GameObject root = new(RuntimeRootName);
            root.transform.position = Vector3.zero;

            EnsureFloor();
            BuildOuterWalls(root.transform);
            BuildCenterPieces(root.transform);
            BuildRaisedLanes(root.transform);
            BuildZoneMarkers(root.transform);
        }

        private void ConfigureGameplayObjects()
        {
            PlaceSpawnPoints();
            ConfigurePumpTask();
            ConfigureFloodZones();
            ConfigureSceneLighting();
        }

        private void EnsureFloor()
        {
            GameObject floor = GameObject.Find("Floor");
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Floor";
                floor.transform.position = Vector3.zero;
            }

            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(18f, 1f, 18f);

            Renderer renderer = floor.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateMaterial(new Color(0.11f, 0.12f, 0.15f));
            }

            Collider colliderRef = floor.GetComponent<Collider>();
            if (colliderRef == null)
            {
                floor.AddComponent<BoxCollider>();
            }
        }

        private void BuildOuterWalls(Transform parent)
        {
            CreateBlock(parent, "Wall_North", new Vector3(0f, 1.5f, 9.25f), new Vector3(19f, 3f, 0.5f), new Color(0.17f, 0.18f, 0.22f));
            CreateBlock(parent, "Wall_South", new Vector3(0f, 1.5f, -9.25f), new Vector3(19f, 3f, 0.5f), new Color(0.17f, 0.18f, 0.22f));
            CreateBlock(parent, "Wall_East", new Vector3(9.25f, 1.5f, 0f), new Vector3(0.5f, 3f, 19f), new Color(0.17f, 0.18f, 0.22f));
            CreateBlock(parent, "Wall_West", new Vector3(-9.25f, 1.5f, 0f), new Vector3(0.5f, 3f, 19f), new Color(0.17f, 0.18f, 0.22f));
        }

        private void BuildCenterPieces(Transform parent)
        {
            CreateBlock(parent, "PumpPedestal", new Vector3(0f, 0.35f, 0f), new Vector3(2.4f, 0.7f, 2.4f), new Color(0.26f, 0.28f, 0.34f));
            CreateBlock(parent, "CenterCover_NW", new Vector3(-2.8f, 1f, -2.8f), new Vector3(1.1f, 2f, 1.1f), new Color(0.25f, 0.36f, 0.52f));
            CreateBlock(parent, "CenterCover_NE", new Vector3(2.8f, 1f, -2.8f), new Vector3(1.1f, 2f, 1.1f), new Color(0.52f, 0.31f, 0.25f));
            CreateBlock(parent, "CenterCover_SW", new Vector3(-2.8f, 1f, 2.8f), new Vector3(1.1f, 2f, 1.1f), new Color(0.25f, 0.52f, 0.33f));
            CreateBlock(parent, "CenterCover_SE", new Vector3(2.8f, 1f, 2.8f), new Vector3(1.1f, 2f, 1.1f), new Color(0.52f, 0.25f, 0.48f));
        }

        private void BuildRaisedLanes(Transform parent)
        {
            CreateBlock(parent, "Lane_West", new Vector3(-5.7f, 0.55f, 0f), new Vector3(2f, 1.1f, 10f), new Color(0.2f, 0.24f, 0.29f));
            CreateBlock(parent, "Lane_East", new Vector3(5.7f, 0.55f, 0f), new Vector3(2f, 1.1f, 10f), new Color(0.2f, 0.24f, 0.29f));
            CreateBlock(parent, "Ramp_NW", new Vector3(-5.7f, 0.28f, -5.7f), new Vector3(2f, 0.55f, 2f), new Color(0.34f, 0.36f, 0.4f));
            CreateBlock(parent, "Ramp_SW", new Vector3(-5.7f, 0.28f, 5.7f), new Vector3(2f, 0.55f, 2f), new Color(0.34f, 0.36f, 0.4f));
            CreateBlock(parent, "Ramp_NE", new Vector3(5.7f, 0.28f, -5.7f), new Vector3(2f, 0.55f, 2f), new Color(0.34f, 0.36f, 0.4f));
            CreateBlock(parent, "Ramp_SE", new Vector3(5.7f, 0.28f, 5.7f), new Vector3(2f, 0.55f, 2f), new Color(0.34f, 0.36f, 0.4f));
        }

        private void BuildZoneMarkers(Transform parent)
        {
            CreateMarker(parent, "ZoneMarker_Bilge", new Vector3(-4.5f, 0.02f, 0f), new Vector3(7.5f, 0.04f, 16f), new Color(0.16f, 0.45f, 0.82f, 0.3f));
            CreateMarker(parent, "ZoneMarker_Bridge", new Vector3(4.5f, 0.02f, 0f), new Vector3(7.5f, 0.04f, 16f), new Color(0.92f, 0.72f, 0.2f, 0.3f));
        }

        private void PlaceSpawnPoints()
        {
            Dictionary<string, Vector3> positions = new()
            {
                ["SpawnPoint_01"] = new Vector3(-7f, 1.1f, -7f),
                ["SpawnPoint_02"] = new Vector3(-7f, 1.1f, 7f),
                ["SpawnPoint_03"] = new Vector3(7f, 1.1f, -7f),
                ["SpawnPoint_04"] = new Vector3(7f, 1.1f, 7f)
            };

            foreach (KeyValuePair<string, Vector3> pair in positions)
            {
                GameObject go = GameObject.Find(pair.Key);
                if (go == null)
                {
                    continue;
                }

                go.transform.position = pair.Value;
                go.transform.rotation = Quaternion.LookRotation((Vector3.zero - pair.Value).normalized.WithY(0f), Vector3.up);
            }
        }

        private void ConfigurePumpTask()
        {
            PumpRepairTask pump = FindFirstObjectByType<PumpRepairTask>();
            if (pump == null)
            {
                return;
            }

            pump.transform.position = new Vector3(0f, 0.9f, 0f);

            if (pump.GetComponent<Collider>() == null)
            {
                BoxCollider colliderRef = pump.gameObject.AddComponent<BoxCollider>();
                colliderRef.size = new Vector3(1.6f, 1.5f, 1.6f);
                colliderRef.center = new Vector3(0f, 0.75f, 0f);
            }

            if (pump.GetComponentInChildren<Renderer>() == null)
            {
                CreateBlock(pump.transform, "PumpVisual", new Vector3(0f, 0.35f, 0f), new Vector3(0.9f, 0.7f, 0.9f), new Color(0.95f, 0.64f, 0.14f));
                CreateBlock(pump.transform, "PumpHandle", new Vector3(0f, 0.95f, 0f), new Vector3(0.25f, 0.9f, 0.25f), new Color(0.82f, 0.84f, 0.88f));
            }
        }

        private void ConfigureFloodZones()
        {
            ConfigureFloodZone("FloodZone_Bilge", new Vector3(-4.5f, 1.2f, 0f), new Vector3(7.5f, 2.5f, 16f));
            ConfigureFloodZone("FloodZone_Bridge", new Vector3(4.5f, 1.2f, 0f), new Vector3(7.5f, 2.5f, 16f));
        }

        private void ConfigureFloodZone(string objectName, Vector3 position, Vector3 boxSize)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null)
            {
                return;
            }

            go.transform.position = position;

            BoxCollider colliderRef = go.GetComponent<BoxCollider>();
            if (colliderRef == null)
            {
                colliderRef = go.AddComponent<BoxCollider>();
            }

            colliderRef.isTrigger = true;
            colliderRef.size = boxSize;
            colliderRef.center = Vector3.zero;
        }

        private void ConfigureSceneLighting()
        {
            Light directional = FindFirstObjectByType<Light>();
            if (directional != null && directional.type == LightType.Directional)
            {
                directional.transform.rotation = Quaternion.Euler(42f, -36f, 0f);
                directional.intensity = 1.35f;
            }
        }

        private static GameObject CreateBlock(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject go = parent.Find(name)?.gameObject;
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(parent, false);
            }

            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateMaterial(color);
            }

            return go;
        }

        private static GameObject CreateMarker(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject go = parent.Find(name)?.gameObject;
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(parent, false);
            }

            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            Collider colliderRef = go.GetComponent<Collider>();
            if (colliderRef != null)
            {
                Destroy(colliderRef);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = CreateMaterial(color);
            }

            return go;
        }

        private static Material CreateMaterial(Color color)
        {
            Material material = new(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            return material;
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
