// File: Assets/_Project/Gameplay/Beta/BetaObjectiveGlowDirector.cs
using HueDoneIt.Tasks;
using HueDoneIt.Gameplay.Elimination;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Adds an obvious glowing objective marker for the local player. This does not alter task logic; it only
    /// highlights the nearest unfinished objective and gives friend-testers a concrete place to go next.
    /// </summary>
    [DefaultExecutionOrder(420)]
    [DisallowMultipleComponent]
    public sealed class BetaObjectiveGlowDirector : MonoBehaviour
    {
        private const string MarkerName = "__BetaLocalObjectiveGlow";

        [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 0.25f;
        [SerializeField, Min(0.5f)] private float markerHeight = 2.45f;
        [SerializeField, Min(0.25f)] private float beamHeight = 2.1f;
        [SerializeField] private Color objectiveColor = new Color(0.22f, 0.95f, 1f, 1f);
        [SerializeField] private Color advancedTaskColor = new Color(1f, 0.72f, 0.12f, 1f);

        private GameObject _markerRoot;
        private TextMesh _label;
        private Light _light;
        private Renderer[] _markerRenderers;
        private Material _repairMaterial;
        private Material _advancedMaterial;
        private Transform _currentTarget;
        private string _currentLabel;
        private float _nextRefreshTime;

        private void Start()
        {
            CreateMarker();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
                ResolveTarget();
            }

            UpdateMarker();
        }

        private void CreateMarker()
        {
            if (_markerRoot != null)
            {
                return;
            }

            _repairMaterial = CreateMaterial("HDI Objective Glow Cyan", objectiveColor);
            _advancedMaterial = CreateMaterial("HDI Objective Glow Gold", advancedTaskColor);

            _markerRoot = new GameObject(MarkerName);

            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "ObjectiveOrb";
            orb.transform.SetParent(_markerRoot.transform, false);
            orb.transform.localPosition = Vector3.zero;
            orb.transform.localScale = Vector3.one * 0.42f;
            DestroyCollider(orb);
            ApplyMaterial(orb, _repairMaterial);

            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "ObjectiveBeam";
            beam.transform.SetParent(_markerRoot.transform, false);
            beam.transform.localPosition = new Vector3(0f, -beamHeight * 0.5f, 0f);
            beam.transform.localScale = new Vector3(0.13f, beamHeight * 0.5f, 0.13f);
            DestroyCollider(beam);
            ApplyMaterial(beam, _repairMaterial);

            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "ObjectiveRing";
            ring.transform.SetParent(_markerRoot.transform, false);
            ring.transform.localPosition = new Vector3(0f, -beamHeight, 0f);
            ring.transform.localScale = new Vector3(1.25f, 0.018f, 1.25f);
            DestroyCollider(ring);
            ApplyMaterial(ring, _repairMaterial);

            GameObject labelObject = new GameObject("ObjectiveLabel");
            labelObject.transform.SetParent(_markerRoot.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            _label = labelObject.AddComponent<TextMesh>();
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.characterSize = 0.12f;
            _label.fontSize = 34;
            _label.color = Color.white;

            _light = _markerRoot.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 8f;
            _light.intensity = 2.5f;
            _light.color = objectiveColor;

            _markerRenderers = _markerRoot.GetComponentsInChildren<Renderer>(true);
            _markerRoot.SetActive(false);
        }

        private void ResolveTarget()
        {
            Transform localPlayer = ResolveLocalPlayer();
            if (localPlayer != null &&
                localPlayer.TryGetComponent(out PlayerLifeState lifeState) &&
                !lifeState.IsAlive)
            {
                _currentTarget = null;
                _currentLabel = string.Empty;
                return;
            }

            Vector3 origin = localPlayer != null ? localPlayer.position : Vector3.zero;

            Transform bestTarget = null;
            string bestLabel = string.Empty;
            Material bestMaterial = _repairMaterial;
            Color bestColor = objectiveColor;
            float bestDistanceSqr = float.MaxValue;

            NetworkRepairTask[] repairTasks = FindObjectsByType<NetworkRepairTask>(FindObjectsSortMode.None);
            for (int i = 0; i < repairTasks.Length; i++)
            {
                NetworkRepairTask task = repairTasks[i];
                if (task == null || task.IsCompleted || task.CurrentState == RepairTaskState.Locked)
                {
                    continue;
                }

                float d = (task.transform.position - origin).sqrMagnitude;
                if (d < bestDistanceSqr)
                {
                    bestDistanceSqr = d;
                    bestTarget = task.transform;
                    bestLabel = task.DisplayName + "\n" + task.CurrentState;
                    bestMaterial = _repairMaterial;
                    bestColor = objectiveColor;
                }
            }

            TaskObjectiveBase[] advancedTasks = FindObjectsByType<TaskObjectiveBase>(FindObjectsSortMode.None);
            for (int i = 0; i < advancedTasks.Length; i++)
            {
                TaskObjectiveBase task = advancedTasks[i];
                if (task == null || task.IsCompleted || task.IsLocked)
                {
                    continue;
                }

                float d = (task.transform.position - origin).sqrMagnitude;
                if (d < bestDistanceSqr)
                {
                    bestDistanceSqr = d;
                    bestTarget = task.transform;
                    bestLabel = task.DisplayName + "\n" + task.CurrentState;
                    bestMaterial = _advancedMaterial;
                    bestColor = advancedTaskColor;
                }
            }

            _currentTarget = bestTarget;
            _currentLabel = bestLabel;

            if (_markerRenderers != null)
            {
                for (int i = 0; i < _markerRenderers.Length; i++)
                {
                    Renderer rendererRef = _markerRenderers[i];
                    if (rendererRef != null)
                    {
                        rendererRef.sharedMaterial = bestMaterial;
                    }
                }
            }

            if (_light != null)
            {
                _light.color = bestColor;
            }
        }

        private void UpdateMarker()
        {
            if (_markerRoot == null)
            {
                CreateMarker();
            }

            bool hasTarget = _currentTarget != null;
            _markerRoot.SetActive(hasTarget);
            if (!hasTarget)
            {
                return;
            }

            Vector3 position = _currentTarget.position + Vector3.up * markerHeight;
            float bob = Mathf.Sin(Time.time * 4.5f) * 0.18f;
            _markerRoot.transform.position = position + Vector3.up * bob;
            _markerRoot.transform.Rotate(Vector3.up, 95f * Time.deltaTime, Space.World);

            if (_label != null)
            {
                _label.text = _currentLabel;
                Camera cameraRef = Camera.main;
                if (cameraRef != null)
                {
                    Vector3 toCamera = _label.transform.position - cameraRef.transform.position;
                    if (toCamera.sqrMagnitude > 0.001f)
                    {
                        _label.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                    }
                }
            }

            if (_light != null)
            {
                _light.intensity = 1.6f + (Mathf.Sin(Time.time * 6f) * 0.75f);
            }
        }

        private static Transform ResolveLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
            {
                return manager.LocalClient.PlayerObject.transform;
            }

            Camera cameraRef = Camera.main;
            return cameraRef != null ? cameraRef.transform : null;
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
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * 1.8f);
            }
            return material;
        }

        private static void ApplyMaterial(GameObject go, Material material)
        {
            Renderer rendererRef = go != null ? go.GetComponent<Renderer>() : null;
            if (rendererRef != null)
            {
                rendererRef.sharedMaterial = material;
            }
        }

        private static void DestroyCollider(GameObject go)
        {
            Collider collider = go != null ? go.GetComponent<Collider>() : null;
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }
    }
}
