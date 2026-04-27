// File: Assets/_Project/Gameplay/Beta/BetaTaskWorldAffordancePresenter.cs
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Adds task radius rings for playtest readability, but keeps actual task prompts hidden unless the local
    /// player is inside the task's interaction radius. This prevents map-wide floating prompt spam.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaTaskWorldAffordancePresenter : MonoBehaviour
    {
        private const string MarkerName = "__BetaTaskAffordance";

        [SerializeField, Min(0.25f)] private float refreshIntervalSeconds = 0.35f;
        [SerializeField, Min(0f)] private float promptVisibilityPadding = 0.35f;
        [SerializeField] private bool showDistantRadiusRings = false;
        [SerializeField] private Color idleColor = new Color(0.12f, 0.85f, 1f, 0.30f);
        [SerializeField] private Color activeColor = new Color(1f, 0.78f, 0.16f, 0.55f);
        [SerializeField] private Color completedColor = new Color(0.18f, 1f, 0.36f, 0.22f);
        [SerializeField] private Color blockedColor = new Color(1f, 0.12f, 0.12f, 0.35f);

        private float _nextRefreshTime;
        private Material _ringMaterial;
        private Transform _localPlayer;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            _localPlayer = ResolveLocalPlayer();
            EnsureMarkers();
        }

        private void EnsureMarkers()
        {
            NetworkRepairTask[] tasks = FindObjectsOfType<NetworkRepairTask>();
            for (int i = 0; i < tasks.Length; i++)
            {
                NetworkRepairTask task = tasks[i];
                if (task == null)
                {
                    continue;
                }

                Transform existing = task.transform.Find(MarkerName);
                GameObject marker = existing != null ? existing.gameObject : CreateMarker(task);
                UpdateMarker(task, marker);
            }
        }

        private GameObject CreateMarker(NetworkRepairTask task)
        {
            GameObject root = new GameObject(MarkerName);
            root.transform.SetParent(task.transform, false);
            root.transform.localPosition = Vector3.zero;

            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "InteractionRadius";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            ring.transform.localScale = new Vector3(Mathf.Max(0.25f, task.MaxUseDistance * 2f), 0.012f, Mathf.Max(0.25f, task.MaxUseDistance * 2f));
            Collider collider = ring.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = ring.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetRingMaterial();
            }

            GameObject labelObject = new GameObject("TaskLabel");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 2f, 0f);

            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.13f;
            label.fontSize = 34;
            label.text = string.Empty;

            return root;
        }

        private void UpdateMarker(NetworkRepairTask task, GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            float distance = _localPlayer != null ? Vector3.Distance(_localPlayer.position, task.transform.position) : float.MaxValue;
            bool inPromptRange = distance <= task.MaxUseDistance + promptVisibilityPadding;
            Color color = ResolveColor(task);
            Color ringColor = color;
            if (!inPromptRange)
            {
                ringColor.a *= 0.35f;
            }

            Transform ring = marker.transform.Find("InteractionRadius");
            if (ring != null)
            {
                ring.gameObject.SetActive(showDistantRadiusRings || inPromptRange);
            }

            Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material material = renderer.material;
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", ringColor);
                }
                else if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", ringColor);
                }
            }

            TextMesh label = marker.GetComponentInChildren<TextMesh>(true);
            if (label != null)
            {
                label.gameObject.SetActive(inPromptRange);
                if (inPromptRange)
                {
                    label.text = BuildLabel(task);
                    label.color = Color.Lerp(Color.white, color, 0.35f);
                    if (Camera.main != null)
                    {
                        label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position, Vector3.up);
                    }
                }
                else
                {
                    label.text = string.Empty;
                }
            }
        }

        private string BuildLabel(NetworkRepairTask task)
        {
            string state = task.CurrentState.ToString();
            return task.DisplayName + "\n" + state + "\nE: start / cancel";
        }

        private Color ResolveColor(NetworkRepairTask task)
        {
            switch (task.CurrentState)
            {
                case RepairTaskState.Completed:
                    return completedColor;
                case RepairTaskState.InProgress:
                    return activeColor;
                case RepairTaskState.Locked:
                case RepairTaskState.FailedAttempt:
                    return blockedColor;
                default:
                    return idleColor;
            }
        }

        private Transform ResolveLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
            {
                return manager.LocalClient.PlayerObject.transform;
            }

            Camera mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform : null;
        }

        private Material GetRingMaterial()
        {
            if (_ringMaterial != null)
            {
                return _ringMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            _ringMaterial = new Material(shader);
            _ringMaterial.name = "BetaTaskRadiusMaterial";
            _ringMaterial.color = idleColor;
            return _ringMaterial;
        }
    }
}
