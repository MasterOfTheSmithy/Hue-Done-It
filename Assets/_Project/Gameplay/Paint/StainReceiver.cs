using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HueDoneIt.Gameplay.Paint
{
    [DisallowMultipleComponent]
    public sealed class StainReceiver : MonoBehaviour
    {
        [Header("Surface Tint")]
        [SerializeField] private Renderer targetRenderer;
        [SerializeField, Min(0f)] private float stainBlendPerHit = 0.08f;
        [SerializeField, Min(0f)] private float decayPerSecond = 0.015f;

        [Header("Splat Rendering")]
        [SerializeField, Min(1)] private int maxSplatDecals = 96;
        [SerializeField, Min(0.25f)] private float temporaryLifetimeSeconds = 6f;
        [SerializeField] private List<Texture2D> splatPatterns = new();
        [SerializeField] private Shader splatShader;
        [SerializeField] private bool randomizeRotation = true;
        [SerializeField] private bool randomizeFlip = true;
        [SerializeField, Min(0f)] private float forceToDensityMultiplier = 0.18f;
        [SerializeField] private Vector2 alphaRange = new(0.24f, 0.82f);

        private readonly List<DecalInstance> _activeDecals = new();
        private readonly Stack<DecalInstance> _pool = new();
        private readonly List<Material> _patternMaterials = new();
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor;
        private Color _stainColor;
        private bool _hasBaseColor;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private bool _useBaseColorProperty = true;

        private static readonly List<GlobalDecal> GlobalDecals = new();
        private static readonly Stack<GlobalDecal> GlobalPool = new();
        private static Transform _globalRoot;
        private static Material _fallbackGlobalMaterial;

        private sealed class DecalInstance
        {
            public GameObject GameObject;
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropertyBlock;
            public float ExpireAt;
            public bool Permanent;
        }

        private sealed class GlobalDecal
        {
            public GameObject GameObject;
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropertyBlock;
            public float ExpireAt;
        }

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            CacheBaseColor();
            InitializePatternMaterials();
            PrewarmPool();
        }

        private void Update()
        {
            if (!_hasBaseColor)
            {
                CacheBaseColor();
            }

            if (_hasBaseColor)
            {
                _stainColor = Color.Lerp(_stainColor, _baseColor, decayPerSecond * Time.deltaTime);
                ApplyTint(_stainColor);
            }

            TickDecals();
            TickGlobalDecals();
        }

        public void ApplyStain(Color color, PaintSplatData splatData, bool wet)
        {
            if (!_hasBaseColor)
            {
                CacheBaseColor();
            }

            float blend = Mathf.Clamp01(stainBlendPerHit * Mathf.Max(0.2f, splatData.Intensity));
            if (wet)
            {
                blend *= 0.75f;
            }

            _stainColor = Color.Lerp(_hasBaseColor ? _stainColor : color, color, blend);
            ApplyTint(_stainColor);
            SpawnDensityAdjustedSplats(color, splatData, wet);
        }

        public static void SpawnGlobalSplat(Color color, PaintSplatData splatData, bool wet, float lifetimeSeconds)
        {
            EnsureGlobalRoot();

            GlobalDecal decal = GlobalPool.Count > 0 ? GlobalPool.Pop() : CreateGlobalDecal();
            if (decal == null)
            {
                return;
            }

            decal.GameObject.SetActive(true);
            decal.Transform.position = splatData.Position + (splatData.Normal * 0.01f);
            decal.Transform.rotation = Quaternion.LookRotation(splatData.Normal, Vector3.up);
            float scale = Mathf.Clamp(splatData.Radius, 0.06f, 2.6f);
            decal.Transform.localScale = new Vector3(scale, scale, 1f);

            float alpha = Mathf.Clamp01((wet ? 0.34f : 0.48f) * Mathf.Clamp(splatData.Intensity, 0.35f, 1.5f));
            Color renderedColor = color;
            renderedColor.a = alpha;

            decal.Renderer.GetPropertyBlock(decal.PropertyBlock);
            decal.PropertyBlock.SetColor(BaseColorId, renderedColor);
            decal.Renderer.SetPropertyBlock(decal.PropertyBlock);
            decal.ExpireAt = Time.time + Mathf.Max(0.1f, lifetimeSeconds);

            GlobalDecals.Add(decal);
        }

        private void CacheBaseColor()
        {
            if (targetRenderer == null)
            {
                return;
            }

            _baseColor = targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_BaseColor")
                ? targetRenderer.sharedMaterial.GetColor(BaseColorId)
                : targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_Color")
                    ? targetRenderer.sharedMaterial.GetColor(ColorId)
                    : Color.gray;
            _useBaseColorProperty = targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_BaseColor");
            _stainColor = _baseColor;
            _hasBaseColor = true;
            ApplyTint(_baseColor);
        }

        private void ApplyTint(Color color)
        {
            if (targetRenderer == null)
            {
                return;
            }

            targetRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(_useBaseColorProperty ? BaseColorId : ColorId, color);
            targetRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void InitializePatternMaterials()
        {
            if (splatShader == null)
            {
                splatShader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            _patternMaterials.Clear();
            int count = splatPatterns != null ? splatPatterns.Count : 0;

            if (count <= 0)
            {
                Material fallback = BuildPatternMaterial(null);
                if (fallback != null)
                {
                    _patternMaterials.Add(fallback);
                }

                return;
            }

            for (int i = 0; i < count; i++)
            {
                Material material = BuildPatternMaterial(splatPatterns[i]);
                if (material != null)
                {
                    _patternMaterials.Add(material);
                }
            }

            if (_patternMaterials.Count == 0)
            {
                Material fallback = BuildPatternMaterial(null);
                if (fallback != null)
                {
                    _patternMaterials.Add(fallback);
                }
            }
        }

        private Material BuildPatternMaterial(Texture2D pattern)
        {
            if (splatShader == null)
            {
                return null;
            }

            Material material = new(splatShader)
            {
                renderQueue = (int)RenderQueue.Transparent
            };

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", pattern);
            }
            else if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", pattern);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            material.SetFloat("_ZWrite", 0f);
            material.enableInstancing = true;
            return material;
        }

        private void PrewarmPool()
        {
            for (int i = 0; i < maxSplatDecals; i++)
            {
                DecalInstance instance = CreateDecalInstance();
                if (instance != null)
                {
                    _pool.Push(instance);
                }
            }
        }

        private DecalInstance CreateDecalInstance()
        {
            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decal.name = "StainDecal";
            decal.transform.SetParent(transform, true);
            Collider decalCollider = decal.GetComponent<Collider>();
            if (decalCollider != null)
            {
                Destroy(decalCollider);
            }

            MeshRenderer renderer = decal.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Destroy(decal);
                return null;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = _patternMaterials.Count > 0 ? _patternMaterials[0] : null;
            decal.SetActive(false);
            return new DecalInstance
            {
                GameObject = decal,
                Transform = decal.transform,
                Renderer = renderer,
                PropertyBlock = new MaterialPropertyBlock(),
                ExpireAt = 0f,
                Permanent = false
            };
        }

        private static GlobalDecal CreateGlobalDecal()
        {
            EnsureGlobalRoot();

            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decal.name = "GlobalPaintSplat";
            decal.transform.SetParent(_globalRoot, true);

            Collider decalCollider = decal.GetComponent<Collider>();
            if (decalCollider != null)
            {
                Destroy(decalCollider);
            }

            MeshRenderer renderer = decal.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Destroy(decal);
                return null;
            }

            if (_fallbackGlobalMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    _fallbackGlobalMaterial = new Material(shader)
                    {
                        renderQueue = (int)RenderQueue.Transparent
                    };
                    _fallbackGlobalMaterial.SetFloat("_Surface", 1f);
                    _fallbackGlobalMaterial.SetFloat("_ZWrite", 0f);
                    _fallbackGlobalMaterial.enableInstancing = true;
                }
            }

            renderer.sharedMaterial = _fallbackGlobalMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            decal.SetActive(false);

            return new GlobalDecal
            {
                GameObject = decal,
                Transform = decal.transform,
                Renderer = renderer,
                PropertyBlock = new MaterialPropertyBlock(),
                ExpireAt = 0f
            };
        }

        private static void EnsureGlobalRoot()
        {
            if (_globalRoot != null)
            {
                return;
            }

            GameObject root = GameObject.Find("__GlobalPaintSplats");
            if (root == null)
            {
                root = new GameObject("__GlobalPaintSplats");
            }

            _globalRoot = root.transform;
        }

        private void SpawnDensityAdjustedSplats(Color color, PaintSplatData splatData, bool wet)
        {
            int splatCount = 1 + Mathf.Clamp(Mathf.FloorToInt(splatData.ForceMagnitude * forceToDensityMultiplier), 0, 3);
            Vector3 tangent = Vector3.Cross(splatData.Normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.Cross(splatData.Normal, Vector3.right);
            }

            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(splatData.Normal, tangent).normalized;

            for (int i = 0; i < splatCount; i++)
            {
                float spread = i == 0 ? 0f : (0.06f * (0.5f + i));
                Vector3 offset = (tangent * spread * ((i & 1) == 0 ? -1f : 1f)) + (bitangent * spread * ((i & 2) == 0 ? 1f : -1f));
                SpawnDecal(color, splatData, wet, offset, i);
            }
        }

        private void SpawnDecal(Color color, PaintSplatData splatData, bool wet, Vector3 offset, int localIndex)
        {
            DecalInstance instance = AcquireDecalInstance();
            if (instance == null)
            {
                return;
            }

            instance.GameObject.SetActive(true);
            instance.Transform.position = splatData.Position + (splatData.Normal * 0.01f) + offset;

            Vector3 projectedVelocity = Vector3.ProjectOnPlane(splatData.VelocityDirection, splatData.Normal);
            Vector3 up = projectedVelocity.sqrMagnitude > 0.0001f ? projectedVelocity.normalized : Vector3.Cross(splatData.Normal, Vector3.right).normalized;
            Quaternion rotation = Quaternion.LookRotation(splatData.Normal, up);
            if (randomizeRotation)
            {
                int rotationSeed = Mathf.Abs(splatData.PatternIndex + (localIndex * 17));
                float randomAngle = (rotationSeed % 360);
                rotation = Quaternion.AngleAxis(randomAngle, splatData.Normal) * rotation;
            }

            instance.Transform.rotation = rotation;

            float baseScale = Mathf.Clamp(splatData.Radius * (1f - (localIndex * 0.12f)), 0.06f, 2.8f);
            float streakFactor = splatData.SplatType is PaintSplatType.WallImpact or PaintSplatType.Punch
                ? 1.45f
                : 1f;
            Vector3 scale = new(baseScale * streakFactor, baseScale, 1f);
            if (randomizeFlip && ((splatData.PatternIndex + localIndex) & 1) == 0)
            {
                scale.x *= -1f;
            }

            instance.Transform.localScale = scale;

            int materialIndex = _patternMaterials.Count == 0
                ? 0
                : Mathf.Abs(splatData.PatternIndex + localIndex) % _patternMaterials.Count;
            if (_patternMaterials.Count > 0)
            {
                instance.Renderer.sharedMaterial = _patternMaterials[materialIndex];
            }

            float alpha = Mathf.Lerp(alphaRange.x, alphaRange.y, Mathf.Clamp01(splatData.Intensity));
            if (wet)
            {
                alpha *= 0.72f;
            }

            Color renderedColor = color;
            renderedColor.a = alpha;
            instance.Renderer.GetPropertyBlock(instance.PropertyBlock);
            instance.PropertyBlock.SetColor(BaseColorId, renderedColor);
            if (instance.Renderer.sharedMaterial != null && instance.Renderer.sharedMaterial.HasProperty("_Color"))
            {
                instance.PropertyBlock.SetColor(ColorId, renderedColor);
            }

            instance.Renderer.SetPropertyBlock(instance.PropertyBlock);

            bool permanent = splatData.Permanence == PaintSplatPermanence.Permanent;
            instance.Permanent = permanent;
            instance.ExpireAt = permanent ? float.PositiveInfinity : Time.time + temporaryLifetimeSeconds;
            _activeDecals.Add(instance);

            TrimIfOverBudget();
        }

        private DecalInstance AcquireDecalInstance()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }

            if (_activeDecals.Count == 0)
            {
                return null;
            }

            int candidateIndex = -1;
            float oldestExpiry = float.PositiveInfinity;

            for (int i = 0; i < _activeDecals.Count; i++)
            {
                DecalInstance active = _activeDecals[i];
                if (active.Permanent)
                {
                    continue;
                }

                if (active.ExpireAt < oldestExpiry)
                {
                    oldestExpiry = active.ExpireAt;
                    candidateIndex = i;
                }
            }

            if (candidateIndex < 0)
            {
                candidateIndex = 0;
            }

            DecalInstance recycled = _activeDecals[candidateIndex];
            _activeDecals.RemoveAt(candidateIndex);
            return recycled;
        }

        private void TickDecals()
        {
            float now = Time.time;

            for (int i = _activeDecals.Count - 1; i >= 0; i--)
            {
                DecalInstance decal = _activeDecals[i];
                if (decal.Permanent || now < decal.ExpireAt)
                {
                    continue;
                }

                _activeDecals.RemoveAt(i);
                decal.GameObject.SetActive(false);
                _pool.Push(decal);
            }
        }

        private static void TickGlobalDecals()
        {
            float now = Time.time;
            for (int i = GlobalDecals.Count - 1; i >= 0; i--)
            {
                GlobalDecal decal = GlobalDecals[i];
                if (now < decal.ExpireAt)
                {
                    continue;
                }

                GlobalDecals.RemoveAt(i);
                decal.GameObject.SetActive(false);
                GlobalPool.Push(decal);
            }
        }

        private void TrimIfOverBudget()
        {
            while (_activeDecals.Count > maxSplatDecals)
            {
                int indexToRemove = -1;
                float oldestExpiry = float.PositiveInfinity;

                for (int i = 0; i < _activeDecals.Count; i++)
                {
                    DecalInstance decal = _activeDecals[i];
                    if (decal.Permanent)
                    {
                        continue;
                    }

                    if (decal.ExpireAt < oldestExpiry)
                    {
                        oldestExpiry = decal.ExpireAt;
                        indexToRemove = i;
                    }
                }

                if (indexToRemove < 0)
                {
                    indexToRemove = 0;
                }

                DecalInstance removed = _activeDecals[indexToRemove];
                _activeDecals.RemoveAt(indexToRemove);
                removed.GameObject.SetActive(false);
                _pool.Push(removed);
            }
        }
    }
}
