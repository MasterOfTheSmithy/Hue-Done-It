// File: Assets/_Project/Gameplay/Beta/BetaFloorRegressionRecoveryDirector.cs
using HueDoneIt.Gameplay.Interaction;
using HueDoneIt.Gameplay.Players;
using HueDoneIt.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Debuggable floor safety layer. It keeps a hidden catch deck under the playable area and repairs critical
    /// floor colliders without disabling the declutter/playability systems that caused the regression.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class BetaFloorRegressionRecoveryDirector : MonoBehaviour
    {
        private const string RootName = "__HueDoneIt_FloorRegressionRecovery";

        [SerializeField, Min(0.25f)] private float repairIntervalSeconds = 1.25f;
        [SerializeField] private bool createInvisibleCatchDeck = true;
        [SerializeField] private bool repairDisabledWalkableColliders = true;
        [SerializeField] private bool addMissingWalkableBoxColliders = true;
        [SerializeField] private bool reactivateDisabledCriticalFloorObjects = true;
        [SerializeField] private bool verboseDiagnostics = true;

        private GameObject _root;
        private float _nextRepairTime;
        private float _lastLogTime;

        private void Start()
        {
            EnsureCatchDeck();
            RepairNow();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRepairTime)
            {
                return;
            }

            _nextRepairTime = Time.unscaledTime + repairIntervalSeconds;
            EnsureCatchDeck();
            RepairNow();
        }

        private void EnsureCatchDeck()
        {
            if (!createInvisibleCatchDeck)
            {
                return;
            }

            _root = GameObject.Find(RootName);
            if (_root != null)
            {
                return;
            }

            _root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(_root, gameObject.scene);

            CreateInvisibleCollider("Global Invisible Catch Deck", new Vector3(0f, -0.42f, 0f), new Vector3(110f, 0.32f, 110f));
            CreateInvisibleCollider("Hub Floor Backstop", new Vector3(0f, -0.05f, 0f), new Vector3(22f, 0.08f, 22f));
            CreateInvisibleCollider("Fore Lane Backstop", new Vector3(0f, -0.06f, 18f), new Vector3(12f, 0.08f, 38f));
            CreateInvisibleCollider("Aft Lane Backstop", new Vector3(0f, -0.06f, -18f), new Vector3(12f, 0.08f, 38f));
            CreateInvisibleCollider("Port Lane Backstop", new Vector3(-18f, -0.06f, 0f), new Vector3(38f, 0.08f, 12f));
            CreateInvisibleCollider("Starboard Lane Backstop", new Vector3(18f, -0.06f, 0f), new Vector3(38f, 0.08f, 12f));

            Physics.SyncTransforms();
            Log("[FloorRecovery] Installed invisible catch/backstop colliders.");
        }

        private void CreateInvisibleCollider(string objectName, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objectName;
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            go.transform.localScale = scale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = true;
                collider.isTrigger = false;
            }
        }

        private void RepairNow()
        {
            if (reactivateDisabledCriticalFloorObjects)
            {
                ReactivateCriticalInactiveObjects();
            }

            Collider[] colliders = Resources.FindObjectsOfTypeAll<Collider>();
            int reenabled = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider colliderRef = colliders[i];
                if (colliderRef == null || !IsSceneObject(colliderRef.gameObject))
                {
                    continue;
                }

                if (repairDisabledWalkableColliders && !colliderRef.enabled && IsCriticalCollider(colliderRef))
                {
                    colliderRef.enabled = true;
                    colliderRef.isTrigger = false;
                    reenabled++;
                    Log("[FloorRecovery] Re-enabled critical collider: " + GetPath(colliderRef.transform));
                }

                if (colliderRef.enabled && LooksWalkable(colliderRef.name))
                {
                    colliderRef.isTrigger = false;
                }
            }

            if (addMissingWalkableBoxColliders)
            {
                AddMissingWalkableColliders();
            }

            if (reenabled > 0)
            {
                Physics.SyncTransforms();
            }
        }

        private void ReactivateCriticalInactiveObjects()
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject obj = objects[i];
                if (obj == null || obj.activeSelf || !IsSceneObject(obj))
                {
                    continue;
                }

                if (LooksWalkable(obj.name) || IsProtectedGameplayObject(obj.transform))
                {
                    obj.SetActive(true);
                    Log("[FloorRecovery] Re-activated critical scene object: " + GetPath(obj.transform));
                }
            }
        }

        private void AddMissingWalkableColliders()
        {
            Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
            int added = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rendererRef = renderers[i];
                if (rendererRef == null || !IsSceneObject(rendererRef.gameObject))
                {
                    continue;
                }

                if (!LooksWalkable(rendererRef.gameObject.name) || rendererRef.GetComponent<Collider>() != null || rendererRef.GetComponentInParent<Collider>() != null)
                {
                    continue;
                }

                BoxCollider collider = rendererRef.gameObject.AddComponent<BoxCollider>();
                Bounds localBounds = rendererRef.localBounds;
                collider.center = localBounds.center;
                collider.size = new Vector3(
                    Mathf.Max(localBounds.size.x, 0.25f),
                    Mathf.Max(localBounds.size.y, 0.08f),
                    Mathf.Max(localBounds.size.z, 0.25f));
                collider.isTrigger = false;
                added++;
                Log("[FloorRecovery] Added missing walkable BoxCollider: " + GetPath(rendererRef.transform));
            }

            if (added > 0)
            {
                Physics.SyncTransforms();
            }
        }

        public static bool IsCriticalCollider(Collider colliderRef)
        {
            if (colliderRef == null)
            {
                return false;
            }

            return LooksWalkable(colliderRef.name) ||
                   LooksWalkable(colliderRef.gameObject.name) ||
                   LooksLikeLargeFlatFloor(colliderRef.bounds) ||
                   IsProtectedGameplayObject(colliderRef.transform);
        }

        public static bool LooksWalkable(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            string lower = objectName.ToLowerInvariant();
            return lower.Contains("floor") ||
                   lower.Contains("deck") ||
                   lower.Contains("lane") ||
                   lower.Contains("route") ||
                   lower.Contains("hub") ||
                   lower.Contains("walk") ||
                   lower.Contains("platform") ||
                   lower.Contains("task support") ||
                   lower.Contains("backstop") ||
                   lower.Contains("catch deck") ||
                   lower.Contains("spawn");
        }

        private static bool LooksLikeLargeFlatFloor(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return size.y <= 0.35f && Mathf.Max(size.x, size.z) >= 3.0f && Mathf.Min(size.x, size.z) >= 1.5f;
        }

        public static bool IsProtectedGameplayObject(Transform t)
        {
            if (t == null)
            {
                return false;
            }

            return t.GetComponentInParent<NetworkObject>() != null ||
                   t.GetComponentInParent<NetworkInteractable>() != null ||
                   t.GetComponentInParent<NetworkRepairTask>() != null ||
                   t.GetComponentInParent<TaskObjectiveBase>() != null ||
                   t.GetComponentInParent<NetworkPlayerAvatar>() != null ||
                   t.GetComponentInParent<NetworkPlayerAuthoritativeMover>() != null;
        }

        private static bool IsSceneObject(GameObject obj)
        {
            return obj != null &&
                   obj.scene.IsValid() &&
                   obj.hideFlags == HideFlags.None;
        }

        public static string GetPath(Transform t)
        {
            if (t == null)
            {
                return "<null>";
            }

            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        private void Log(string message)
        {
            if (!verboseDiagnostics)
            {
                return;
            }

            if (Time.unscaledTime - _lastLogTime < 0.15f)
            {
                return;
            }

            _lastLogTime = Time.unscaledTime;
            Debug.Log(message);
        }
    }
}
