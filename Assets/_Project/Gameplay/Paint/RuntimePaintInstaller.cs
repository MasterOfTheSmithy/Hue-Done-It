// File: Assets/_Project/Gameplay/Paint/RuntimePaintInstaller.cs
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Paint
{
    public static class RuntimePaintInstaller
    {
        private static bool _sceneHookInstalled;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForActiveScene()
        {
            EnsureSceneHook();
            EnsureInstalledForScene(null, false);
        }

        private static void EnsureSceneHook()
        {
            if (_sceneHookInstalled)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            _sceneHookInstalled = true;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureInstalledForScene(null, false);
        }

        public static void EnsureInstalledForScene(Transform runtimeRoot, bool verboseLogging)
        {
            EnsureSceneHook();
            EnsurePaintWorldManager();
            InstallPaintSurfaces(verboseLogging);
            InstallWaterReceivers(verboseLogging);
        }

        private static void EnsurePaintWorldManager()
        {
            if (Object.FindFirstObjectByType<PaintWorldManager>() != null)
            {
                return;
            }

            GameObject go = new GameObject(nameof(PaintWorldManager));
            go.hideFlags = HideFlags.None;
            go.AddComponent<PaintWorldManager>();
        }

        private static void InstallPaintSurfaces(bool verboseLogging)
        {
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!IsEligibleHardSurface(renderer) || IsWaterLike(renderer))
                {
                    continue;
                }

                ResolveProjection(renderer, out PaintSurfaceProjectionPlane plane, out Vector2 size, out float depth);

                bool supportsTexturePaint = MaterialSupportsExplicitPaintTextures(renderer.sharedMaterial);
                if (supportsTexturePaint)
                {
                    PaintSurfaceOverlayRenderer overlay = renderer.GetComponent<PaintSurfaceOverlayRenderer>();
                    if (overlay == null)
                    {
                        overlay = renderer.gameObject.AddComponent<PaintSurfaceOverlayRenderer>();
                    }

                    overlay.enabled = true;
                    overlay.Configure(renderer, plane, size, depth * 0.55f);

                    PaintSurfaceMaterialDriver driver = renderer.GetComponent<PaintSurfaceMaterialDriver>();
                    if (driver == null)
                    {
                        driver = renderer.gameObject.AddComponent<PaintSurfaceMaterialDriver>();
                    }

                    driver.enabled = true;
                    driver.Configure(overlay.OverlayRenderer != null ? overlay.OverlayRenderer : renderer);

                    PaintSurfaceChunk chunk = renderer.GetComponent<PaintSurfaceChunk>();
                    if (chunk == null)
                    {
                        chunk = renderer.gameObject.AddComponent<PaintSurfaceChunk>();
                    }

                    chunk.enabled = true;
                    chunk.Configure(plane, size, depth, driver);
                }
                else
                {
                    // Generic Unity primitive/Lit materials reuse cube UVs across faces. Driving
                    // _BaseMap/_MainTex with a paint texture makes a local hit look like the whole
                    // mesh changed color. Localized StainReceiver decals remain enabled below.
                    PaintSurfaceChunk chunk = renderer.GetComponent<PaintSurfaceChunk>();
                    if (chunk != null)
                    {
                        chunk.enabled = false;
                    }

                    PaintSurfaceOverlayRenderer overlay = renderer.GetComponent<PaintSurfaceOverlayRenderer>();
                    if (overlay != null)
                    {
                        overlay.enabled = false;
                    }

                    Transform overlayTransform = renderer.transform.Find("PaintSurfaceOverlay");
                    if (overlayTransform != null && overlayTransform.TryGetComponent(out MeshRenderer overlayRenderer))
                    {
                        overlayRenderer.enabled = false;
                    }

                    PaintSurfaceMaterialDriver driver = renderer.GetComponent<PaintSurfaceMaterialDriver>();
                    if (driver != null)
                    {
                        driver.enabled = false;
                    }
                }

                StainReceiver receiver = renderer.GetComponent<StainReceiver>();
                if (receiver == null)
                {
                    receiver = renderer.GetComponentInParent<StainReceiver>();
                }

                if (receiver == null)
                {
                    receiver = renderer.gameObject.AddComponent<StainReceiver>();
                }

                receiver.ConfigureTargetRenderer(renderer);
                receiver.ConfigureLegacyDecals(true);

                count++;
            }

            if (verboseLogging)
            {
                Debug.Log($"RuntimePaintInstaller: configured {count} hard paint surfaces in scene '{SceneManager.GetActiveScene().name}'.");
            }
        }

        private static void InstallWaterReceivers(bool verboseLogging)
        {
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!IsWaterLike(renderer))
                {
                    continue;
                }

                WaterPaintReceiver receiver = renderer.GetComponent<WaterPaintReceiver>();
                if (receiver == null)
                {
                    receiver = renderer.gameObject.AddComponent<WaterPaintReceiver>();
                }

                Collider colliderRef = renderer.GetComponent<Collider>();
                if (colliderRef == null)
                {
                    colliderRef = renderer.GetComponentInParent<Collider>();
                }

                receiver.Configure(renderer, colliderRef);
                count++;
            }

            if (verboseLogging)
            {
                Debug.Log($"RuntimePaintInstaller: configured {count} water paint receivers in scene '{SceneManager.GetActiveScene().name}'.");
            }
        }

        private static bool IsEligibleHardSurface(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer || renderer is SpriteRenderer || renderer is SkinnedMeshRenderer)
            {
                return false;
            }

            if (renderer.GetComponentInParent<NetworkPlayerAvatar>() != null)
            {
                return false;
            }

            if (renderer.GetComponentInParent<PlayerRemains>() != null)
            {
                return false;
            }

            if (renderer.transform.name.Contains("PaintSurfaceOverlay"))
            {
                return false;
            }

            Collider colliderRef = renderer.GetComponent<Collider>();
            if (colliderRef == null)
            {
                colliderRef = renderer.GetComponentInParent<Collider>();
            }

            if (colliderRef == null || colliderRef.isTrigger)
            {
                return false;
            }

            Vector3 size = renderer.bounds.size;
            if (size.x * size.y * size.z < 0.0015f)
            {
                return false;
            }

            return true;
        }

        private static bool MaterialSupportsExplicitPaintTextures(Material material)
        {
            if (material == null)
            {
                return false;
            }

            return material.HasProperty("_PaintColorTex") ||
                   material.HasProperty("_PaintWetnessTex") ||
                   material.HasProperty("_PaintAgeTex");
        }

        private static bool IsWaterLike(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            string full = (renderer.name + " " + renderer.gameObject.name + " " + renderer.gameObject.tag + " " + renderer.gameObject.layer).ToLowerInvariant();
            if (full.Contains("water") || full.Contains("ocean") || full.Contains("puddle") || full.Contains("fluid") || full.Contains("liquid") || full.Contains("flood"))
            {
                return true;
            }

            Material shared = renderer.sharedMaterial;
            if (shared != null)
            {
                string mat = shared.name.ToLowerInvariant();
                if (mat.Contains("water") || mat.Contains("ocean") || mat.Contains("flood") || mat.Contains("fluid") || mat.Contains("liquid"))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ResolveProjection(Renderer renderer, out PaintSurfaceProjectionPlane plane, out Vector2 size, out float depth)
        {
            Vector3 localSize = GetApproximateLocalSize(renderer);
            Vector3 scale = renderer != null ? renderer.transform.lossyScale : Vector3.one;
            Vector3 worldWeightedSize = new Vector3(
                Mathf.Abs(localSize.x * scale.x),
                Mathf.Abs(localSize.y * scale.y),
                Mathf.Abs(localSize.z * scale.z));

            float weightedX = Mathf.Max(0.0001f, worldWeightedSize.x);
            float weightedY = Mathf.Max(0.0001f, worldWeightedSize.y);
            float weightedZ = Mathf.Max(0.0001f, worldWeightedSize.z);

            float localX = Mathf.Max(0.0001f, Mathf.Abs(localSize.x));
            float localY = Mathf.Max(0.0001f, Mathf.Abs(localSize.y));
            float localZ = Mathf.Max(0.0001f, Mathf.Abs(localSize.z));

            // Projection plane is selected from scaled dimensions, because Unity primitives are
            // usually 1x1x1 meshes stretched into long floors/walls. The chunk still stores local
            // surface size; PaintSurfaceChunk converts world-radius stamps back into local space.
            if (weightedY <= weightedX && weightedY <= weightedZ)
            {
                plane = PaintSurfaceProjectionPlane.LocalXZ;
                size = new Vector2(Mathf.Max(0.1f, localX), Mathf.Max(0.1f, localZ));
                depth = Mathf.Max(0.05f, localY * 0.75f + 0.05f);
                return;
            }

            if (weightedZ <= weightedX && weightedZ <= weightedY)
            {
                plane = PaintSurfaceProjectionPlane.LocalXY;
                size = new Vector2(Mathf.Max(0.1f, localX), Mathf.Max(0.1f, localY));
                depth = Mathf.Max(0.05f, localZ * 0.75f + 0.05f);
                return;
            }

            plane = PaintSurfaceProjectionPlane.LocalYZ;
            size = new Vector2(Mathf.Max(0.1f, localZ), Mathf.Max(0.1f, localY));
            depth = Mathf.Max(0.05f, localX * 0.75f + 0.05f);
        }

        private static Vector3 GetApproximateLocalSize(Renderer renderer)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh.bounds.size;
            }

            BoxCollider box = renderer.GetComponent<BoxCollider>();
            if (box != null)
            {
                return box.size;
            }

            SphereCollider sphere = renderer.GetComponent<SphereCollider>();
            if (sphere != null)
            {
                float diameter = sphere.radius * 2f;
                return new Vector3(diameter, diameter, diameter);
            }

            CapsuleCollider capsule = renderer.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                float diameter = capsule.radius * 2f;
                return new Vector3(diameter, capsule.height, diameter);
            }

            Vector3 lossy = renderer.transform.lossyScale;
            Bounds bounds = renderer.bounds;
            return new Vector3(
                bounds.size.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
                bounds.size.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
                bounds.size.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
        }
    }
}
