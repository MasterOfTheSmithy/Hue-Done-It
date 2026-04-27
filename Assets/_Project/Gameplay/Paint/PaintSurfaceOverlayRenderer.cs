// File: Assets/_Project/Gameplay/Paint/PaintSurfaceOverlayRenderer.cs
using UnityEngine;
using UnityEngine.Rendering;

namespace HueDoneIt.Gameplay.Paint
{
    [DisallowMultipleComponent]
    public sealed class PaintSurfaceOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private Renderer sourceRenderer;
        [SerializeField] private MeshRenderer overlayRenderer;
        [SerializeField] private MeshFilter overlayMeshFilter;
        [SerializeField, Min(0f)] private float offsetFromSurface = 0.01f;
        [SerializeField] private string overlayObjectName = "PaintSurfaceOverlay";
        [SerializeField, Min(1f)] private float meshOverlayScale = 1.0035f;

        public Renderer OverlayRenderer => overlayRenderer;

        public void Configure(Renderer source, PaintSurfaceProjectionPlane plane, Vector2 size, float projectionDepth)
        {
            sourceRenderer = source;
            EnsureOverlay();
            UpdateOverlayMesh();
            UpdateTransform(plane, size, projectionDepth);
        }

        private void Awake()
        {
            EnsureOverlay();
            UpdateOverlayMesh();
        }

        private void EnsureOverlay()
        {
            if (overlayRenderer != null && overlayMeshFilter != null)
            {
                return;
            }

            Transform existing = transform.Find(overlayObjectName);
            GameObject overlayObject;
            if (existing != null)
            {
                overlayObject = existing.gameObject;
            }
            else
            {
                overlayObject = new GameObject(overlayObjectName, typeof(MeshFilter), typeof(MeshRenderer));
                overlayObject.transform.SetParent(transform, false);
            }

            overlayMeshFilter = overlayObject.GetComponent<MeshFilter>();
            overlayRenderer = overlayObject.GetComponent<MeshRenderer>();

            overlayRenderer.enabled = true;
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            if (overlayRenderer.sharedMaterial == null || !overlayRenderer.sharedMaterial.name.Contains("RuntimePaintOverlayMaterial"))
            {
                overlayRenderer.sharedMaterial = CreateOverlayMaterial();
            }
        }

        private void UpdateOverlayMesh()
        {
            if (overlayMeshFilter == null)
            {
                return;
            }

            Mesh sourceMesh = null;
            if (sourceRenderer != null)
            {
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter != null)
                {
                    sourceMesh = sourceFilter.sharedMesh;
                }
            }

            overlayMeshFilter.sharedMesh = sourceMesh != null ? sourceMesh : BuildQuadMesh();
        }

        private void UpdateTransform(PaintSurfaceProjectionPlane plane, Vector2 size, float projectionDepth)
        {
            if (overlayRenderer == null)
            {
                return;
            }

            Transform t = overlayRenderer.transform;
            bool usingSourceMesh = false;
            if (sourceRenderer != null)
            {
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                usingSourceMesh = sourceFilter != null && sourceFilter.sharedMesh != null;
            }

            if (usingSourceMesh)
            {
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one * Mathf.Max(1f, meshOverlayScale);
                return;
            }

            t.localPosition = GetLocalOffset(plane, projectionDepth + offsetFromSurface);
            t.localRotation = GetLocalRotation(plane);
            t.localScale = new Vector3(Mathf.Max(0.05f, size.x), Mathf.Max(0.05f, size.y), 1f);
        }

        private static Vector3 GetLocalOffset(PaintSurfaceProjectionPlane plane, float amount)
        {
            return plane switch
            {
                PaintSurfaceProjectionPlane.LocalXY => new Vector3(0f, 0f, amount),
                PaintSurfaceProjectionPlane.LocalYZ => new Vector3(amount, 0f, 0f),
                _ => new Vector3(0f, amount, 0f)
            };
        }

        private static Quaternion GetLocalRotation(PaintSurfaceProjectionPlane plane)
        {
            return plane switch
            {
                PaintSurfaceProjectionPlane.LocalXY => Quaternion.identity,
                PaintSurfaceProjectionPlane.LocalYZ => Quaternion.Euler(0f, 90f, 0f),
                _ => Quaternion.Euler(90f, 0f, 0f)
            };
        }

        private static Mesh BuildQuadMesh()
        {
            Mesh mesh = new Mesh { name = "PaintOverlayQuad" };
            mesh.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                new(-0.5f, -0.5f, 0f),
                new( 0.5f, -0.5f, 0f),
                new( 0.5f,  0.5f, 0f),
                new(-0.5f,  0.5f, 0f)
            });
            mesh.SetUVs(0, new System.Collections.Generic.List<Vector2>
            {
                new(0f, 0f),
                new(1f, 0f),
                new(1f, 1f),
                new(0f, 1f)
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateOverlayMaterial()
        {
            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                name = "RuntimePaintOverlayMaterial",
                renderQueue = (int)RenderQueue.Transparent
            };

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            return material;
        }
    }
}
