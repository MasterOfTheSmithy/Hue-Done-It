// File: Assets/_Project/Gameplay/Paint/StainReceiver.cs
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
        [SerializeField] private bool enableWholeSurfaceTint;
        [SerializeField, Min(0f)] private float stainBlendPerHit = 0.02f;
        [SerializeField, Min(0f)] private float decayPerSecond = 0.012f;

        [Header("Area Story Accumulation")]
        [SerializeField, Min(1f)] private float maxActivityScore = 40f;
        [SerializeField, Min(0f)] private float activityDecayPerSecond = 0.45f;
        [SerializeField, Min(0f)] private float activityToDensityMultiplier = 0.18f;
        [SerializeField, Min(0f)] private float activityToTintBlend = 0.08f;
        [SerializeField, Min(1)] private int maxPermanentEvidenceMarks = 32;
        [SerializeField, Min(0f)] private float movementWearThreshold = 4f;

        [Header("Flood Interaction")]
        [SerializeField, Min(0f)] private float floodWashoutStrength = 0.62f;
        [SerializeField, Min(0f)] private float floodDilutionStrength = 0.48f;
        [SerializeField] private Color wetRoomTint = new(0.72f, 0.8f, 0.9f, 1f);

        [Header("Splat Rendering")]
        [SerializeField, Min(8)] private int maxSplatDecals = 96;
        [SerializeField, Min(0.5f)] private float temporaryLifetimeSeconds = 7f;
        [SerializeField, Min(0.5f)] private float heavyTemporaryLifetimeSeconds = 12f;
        [SerializeField, Min(0f)] private float zOffset = 0.015f;
        [SerializeField] private List<Texture2D> splatPatterns = new();
        [SerializeField, Min(1)] private int generatedPatternCount = 6;
        [SerializeField] private Shader splatShader;
        [SerializeField] private bool randomizeRotation = true;
        [SerializeField] private bool randomizeFlip = true;
        [SerializeField, Min(0f)] private float forceToDensityMultiplier = 0.22f;
        [SerializeField] private Vector2 alphaRange = new(0.26f, 0.9f);
        [SerializeField, Min(0f)] private float stretchMultiplier = 0.65f;
        [SerializeField] private Vector2 stretchClamp = new(1f, 4.5f);
        [SerializeField, Min(0.1f)] private float maxLocalizedDecalMajorAxis = 4.8f;
        [SerializeField, Min(0.1f)] private float maxLocalizedDecalMinorAxis = 2.15f;
        [SerializeField, Min(0.1f)] private float maxHeavyImpactMajorAxis = 6.2f;
        [SerializeField, Min(0.1f)] private float maxHeavyImpactMinorAxis = 2.75f;

        [Header("Type Lifetime Thresholds")]
        [SerializeField, Min(0f)] private float landingPermanentForceThreshold = 13f;
        [SerializeField, Min(0f)] private float thrownPermanentForceThreshold = 10f;

        [Header("Mask Preferences")]
        [SerializeField] private SplatTypePatternRule[] typePatternRules;

        [Header("Debug")]
        [SerializeField] private bool debugDrawDirection;
        [SerializeField] private bool allowLegacyDecals = true;

        private readonly List<DecalInstance> _activeDecals = new();
        private readonly Stack<DecalInstance> _pool = new();
        private readonly List<Material> _patternMaterials = new();
        private readonly Dictionary<PaintSplatType, int[]> _patternLookup = new();
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor;
        private Color _stainColor;
        private bool _hasBaseColor;
        private float _activityScore;
        private float _movementWear;
        private float _roomHazard01;
        private float _roomWashout01;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private bool _useBaseColorProperty = true;

        private static readonly List<GlobalDecal> GlobalDecals = new();
        private static readonly Stack<GlobalDecal> GlobalPool = new();
        private static Transform _localizedRoot;
        private static Transform _globalRoot;
        private static Material _fallbackGlobalMaterial;

        [System.Serializable]
        private struct SplatTypePatternRule
        {
            public PaintSplatType splatType;
            public int[] patternIndices;
        }

        private sealed class DecalInstance
        {
            public GameObject GameObject;
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropertyBlock;
            public float SpawnAt;
            public float ExpireAt;
            public bool Permanent;
            public float InitialAlpha;
        }

        private sealed class GlobalDecal
        {
            public GameObject GameObject;
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropertyBlock;
            public float ExpireAt;
        }

        private struct SplatVisualProfile
        {
            public float RadiusMultiplier;
            public float AlphaMultiplier;
            public float StretchMultiplier;
            public int AdditionalSplats;
            public float Lifetime;
            public bool ForceTemporary;
            public bool ForcePermanent;
            public bool PreferDirectionalMask;
        }

        public float ActivityScore01 => Mathf.Clamp01(_activityScore / Mathf.Max(0.001f, maxActivityScore));
        public float WashoutFactor01 => _roomWashout01;
        public int ActiveEvidenceCount => _activeDecals.Count;

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            CacheBaseColor();
            InitializePatternMaterials();
            BuildPatternLookup();
            PrewarmPool();
        }

        private void Update()
        {
            if (!_hasBaseColor)
            {
                CacheBaseColor();
            }

            _activityScore = Mathf.Max(0f, _activityScore - (activityDecayPerSecond * Time.deltaTime));
            _movementWear = Mathf.Max(0f, _movementWear - (activityDecayPerSecond * 0.35f * Time.deltaTime));

            if (_hasBaseColor && enableWholeSurfaceTint)
            {
                Color neutralized = Color.Lerp(_stainColor, _baseColor, decayPerSecond * Time.deltaTime);
                float activityBlend = ActivityScore01 * activityToTintBlend * 0.6f;
                Color activeTint = Color.Lerp(neutralized, _stainColor, activityBlend);
                Color hazardTint = Color.Lerp(activeTint, wetRoomTint, _roomHazard01 * 0.22f);
                _stainColor = Color.Lerp(hazardTint, _baseColor, _roomWashout01 * floodWashoutStrength * 0.22f);
                ApplyTint(_stainColor);
            }

            TickDecals();
            TickGlobalDecals();
        }

        public void ApplyRoomState(float hazard01, float washout01)
        {
            _roomHazard01 = Mathf.Clamp01(hazard01);
            _roomWashout01 = Mathf.Clamp01(washout01);
        }


        public void ConfigureLegacyDecals(bool enabled)
        {
            allowLegacyDecals = enabled;
        }

        public void ConfigureTargetRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            targetRenderer = renderer;
            _propertyBlock ??= new MaterialPropertyBlock();
            CacheBaseColor();
        }

        public void ApplyStain(Color color, PaintSplatData splatData, bool wet)
        {
            ApplyStainInternal(color, splatData, wet, enableWholeSurfaceTint);
        }

        public void ApplyLocalizedSplat(Color color, PaintSplatData splatData, bool wet)
        {
            ApplyStainInternal(color, splatData, wet, false);
        }

        private void ApplyStainInternal(Color color, PaintSplatData splatData, bool wet, bool allowWholeSurfaceTint)
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            if (!_hasBaseColor)
            {
                CacheBaseColor();
            }

            float wetFactor = Mathf.Clamp01(_roomWashout01 + (wet ? 0.4f : 0f));
            float intensity01 = Mathf.Clamp01(Mathf.Max(0.18f, splatData.Intensity) / 1.65f);
            float kinetic01 = Mathf.InverseLerp(0.4f, 14f, splatData.ForceMagnitude);
            float eventTintWeight = splatData.EventKind switch
            {
                PaintEventKind.Move => Mathf.Lerp(0.16f, 0.42f, kinetic01),
                PaintEventKind.WallStick => 0.38f,
                PaintEventKind.WallLaunch => 0.48f,
                PaintEventKind.Land => 0.54f,
                PaintEventKind.Punch => 0.58f,
                PaintEventKind.RagdollImpact => 0.72f,
                PaintEventKind.TaskInteract => 0.5f,
                PaintEventKind.ThrownObjectImpact => 0.62f,
                _ => 0.3f
            };

            if (allowWholeSurfaceTint)
            {
                float blend = Mathf.Clamp01(stainBlendPerHit * Mathf.Lerp(0.25f, 1f, intensity01) * eventTintWeight);
                blend *= 1f - (wetFactor * floodDilutionStrength * 0.8f);

                _stainColor = Color.Lerp(_hasBaseColor ? _stainColor : color, color, blend);
                _stainColor = Color.Lerp(_stainColor, _baseColor, wetFactor * floodDilutionStrength * 0.18f);
                ApplyTint(_stainColor);
            }

            RegisterActivity(splatData);
            if (!allowLegacyDecals)
            {
                return;
            }

            SpawnDensityAdjustedSplats(color, splatData, wet || wetFactor > 0.01f);

            if (splatData.EventKind == PaintEventKind.Move)
            {
                float wearContribution = Mathf.Lerp(0.02f, 0.42f, kinetic01) * Mathf.Lerp(0.4f, 1f, intensity01);
                _movementWear += wearContribution;
                if (_movementWear >= movementWearThreshold)
                {
                    _movementWear = 0f;
                    SpawnRouteWearMark(color, splatData);
                }
            }
        }

        public void SpawnEnvironmentalEvidence(Color color, Vector3 position, Vector3 normal, PaintEventKind eventKind, bool permanent, float radius, float intensity)
        {
            PaintSplatData data = new()
            {
                EventKind = eventKind,
                SplatType = eventKind == PaintEventKind.TaskInteract ? PaintSplatType.TaskInteract : PaintSplatType.Generic,
                Permanence = permanent ? PaintSplatPermanence.Permanent : PaintSplatPermanence.Temporary,
                Position = position,
                Normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up,
                Radius = Mathf.Clamp(radius, 0.08f, 2.2f),
                Intensity = Mathf.Clamp(intensity, 0.1f, 1.6f),
                ForceMagnitude = Mathf.Clamp(intensity * 10f, 0f, 20f),
                VelocityDirection = Vector3.forward,
                TangentDirection = Vector3.right,
                PatternIndex = Mathf.RoundToInt(position.x * 37f + position.z * 17f),
                PatternSeed = Mathf.Abs(Mathf.RoundToInt(position.sqrMagnitude * 100f)),
                StretchAmount = 1.15f,
                RotationDegrees = 0f
            };

            ApplyStain(color, data, _roomWashout01 > 0.25f);
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

            Vector3 tangent = Vector3.ProjectOnPlane(splatData.TangentDirection, splatData.Normal);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.Cross(splatData.Normal, Mathf.Abs(splatData.Normal.y) > 0.7f ? Vector3.right : Vector3.up);
            }

            decal.Transform.rotation = Quaternion.LookRotation(splatData.Normal, tangent.normalized);

            float baseScale = Mathf.Clamp(splatData.Radius, 0.06f, 2.6f);
            float stretch = Mathf.Clamp(splatData.StretchAmount, 1f, 3.5f);
            decal.Transform.localScale = new Vector3(baseScale * stretch, baseScale, 1f);

            float alpha = Mathf.Clamp01((wet ? 0.28f : 0.44f) * Mathf.Clamp(splatData.Intensity, 0.35f, 1.5f));
            Color renderedColor = color;
            renderedColor.a = alpha;

            decal.Renderer.GetPropertyBlock(decal.PropertyBlock);
            decal.PropertyBlock.SetColor(BaseColorId, renderedColor);
            decal.PropertyBlock.SetColor(ColorId, renderedColor);
            decal.Renderer.SetPropertyBlock(decal.PropertyBlock);
            decal.ExpireAt = Time.time + Mathf.Max(0.1f, lifetimeSeconds);

            GlobalDecals.Add(decal);
        }

        private void RegisterActivity(PaintSplatData splatData)
        {
            float weight = splatData.EventKind switch
            {
                PaintEventKind.Move => 0.2f,
                PaintEventKind.WallStick => 0.3f,
                PaintEventKind.TaskInteract => 0.6f,
                PaintEventKind.Punch => 0.8f,
                PaintEventKind.RagdollImpact => 1.1f,
                PaintEventKind.ThrownObjectImpact => 0.9f,
                _ => 0.45f
            };

            _activityScore = Mathf.Clamp(_activityScore + (weight * Mathf.Clamp(splatData.Intensity, 0.2f, 1.8f)), 0f, maxActivityScore);
        }

        private void SpawnRouteWearMark(Color color, PaintSplatData fromSplat)
        {
            PaintSplatData wear = fromSplat;
            wear.EventKind = PaintEventKind.Move;
            wear.SplatType = PaintSplatType.MoveSmear;
            wear.Permanence = PaintSplatPermanence.Permanent;
            wear.Radius *= 0.65f;
            wear.Intensity *= 0.55f;
            wear.StretchAmount *= 1.2f;
            wear.PatternSeed += 97;
            SpawnDensityAdjustedSplats(color, wear, _roomWashout01 > 0.25f);
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

            Color finalColor = _hasBaseColor
                ? Color.Lerp(_baseColor, color, 0.22f + (ActivityScore01 * 0.18f))
                : color;

            targetRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(_useBaseColorProperty ? BaseColorId : ColorId, finalColor);
            targetRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void InitializePatternMaterials()
        {
            if (splatShader == null)
            {
                splatShader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            _patternMaterials.Clear();
            if (splatPatterns == null)
            {
                splatPatterns = new List<Texture2D>();
            }

            if (splatPatterns.Count == 0)
            {
                GenerateFallbackPatterns();
            }

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

        private void BuildPatternLookup()
        {
            _patternLookup.Clear();
            if (typePatternRules == null)
            {
                return;
            }

            for (int i = 0; i < typePatternRules.Length; i++)
            {
                SplatTypePatternRule rule = typePatternRules[i];
                if (rule.patternIndices == null || rule.patternIndices.Length == 0)
                {
                    continue;
                }

                _patternLookup[rule.splatType] = rule.patternIndices;
            }
        }


        private void GenerateFallbackPatterns()
        {
            splatPatterns.Clear();
            int count = Mathf.Clamp(generatedPatternCount, 1, 16);
            for (int i = 0; i < count; i++)
            {
                splatPatterns.Add(GeneratePattern(i));
            }
        }

        private Texture2D GeneratePattern(int seed)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.name = "GeneratedSplat_" + seed;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            float baseRotation = Hash01((uint)(seed * 193 + 17)) * Mathf.PI * 2f;
            int lobeCount = 4 + Mathf.FloorToInt(Hash01((uint)(seed * 313 + 29)) * 5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = ((x + 0.5f) / size) * 2f - 1f;
                    float v = ((y + 0.5f) / size) * 2f - 1f;
                    float radius = Mathf.Sqrt((u * u) + (v * v));
                    float angle = Mathf.Atan2(v, u) + baseRotation;

                    float lobe = Mathf.Sin(angle * lobeCount) * 0.18f;
                    float noise = (Hash21((uint)(seed + 1), x, y) - 0.5f) * 0.12f;
                    float edge = 0.78f + lobe + noise;
                    float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(edge, edge + 0.18f, radius));
                    alpha *= alpha;

                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }


        private static float Hash01(uint seed)
        {
            seed ^= 2747636419u;
            seed *= 2654435769u;
            seed ^= seed >> 16;
            seed *= 2654435769u;
            seed ^= seed >> 16;
            seed *= 2654435769u;
            return (seed & 0x00FFFFFFu) / 16777215f;
        }

        private static float Hash21(uint seed, int x, int y)
        {
            unchecked
            {
                uint combined = seed;
                combined ^= (uint)(x * 73856093);
                combined ^= (uint)(y * 19349663);
                return Hash01(combined);
            }
        }

        private Material BuildPatternMaterial(Texture2D pattern)
        {
            if (splatShader == null)
            {
                return null;
            }

            Material material = new Material(splatShader)
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

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
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
            EnsureLocalizedRoot();

            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decal.name = "StainDecal";
            decal.transform.SetParent(_localizedRoot, true);
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
                SpawnAt = 0f,
                ExpireAt = 0f,
                Permanent = false,
                InitialAlpha = 0f
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

                    if (_fallbackGlobalMaterial.HasProperty("_Surface"))
                    {
                        _fallbackGlobalMaterial.SetFloat("_Surface", 1f);
                    }

                    if (_fallbackGlobalMaterial.HasProperty("_Cull"))
                    {
                        _fallbackGlobalMaterial.SetFloat("_Cull", (float)CullMode.Off);
                    }

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

        private static void EnsureLocalizedRoot()
        {
            if (_localizedRoot != null)
            {
                return;
            }

            GameObject root = GameObject.Find("__LocalizedPaintDecals");
            if (root == null)
            {
                root = new GameObject("__LocalizedPaintDecals");
            }

            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            _localizedRoot = root.transform;
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
            SplatVisualProfile profile = BuildVisualProfile(splatData, wet);
            float activityBoost = ActivityScore01 * activityToDensityMultiplier;
            int kineticSplats = splatData.EventKind == PaintEventKind.RagdollImpact
                ? Mathf.Clamp(Mathf.FloorToInt(splatData.ForceMagnitude * (forceToDensityMultiplier + 0.16f)), 1, 8)
                : Mathf.Clamp(Mathf.FloorToInt(splatData.ForceMagnitude * forceToDensityMultiplier), 0, 5);
            int splatCount = 1 + profile.AdditionalSplats + kineticSplats + Mathf.Clamp(Mathf.FloorToInt(activityBoost * 3f), 0, 2);

            Vector3 normal = splatData.Normal.sqrMagnitude > 0.001f ? splatData.Normal.normalized : Vector3.up;
            Vector3 dragDirection = ResolveDragDirection(splatData, normal);
            Vector3 bitangent = Vector3.Cross(normal, dragDirection).normalized;

            for (int i = 0; i < splatCount; i++)
            {
                float t = i / Mathf.Max(1f, splatCount - 1f);
                Vector3 offset = ComputeSplatOffset(splatData, normal, dragDirection, bitangent, i, splatCount);

                float localStretch = Mathf.Lerp(profile.StretchMultiplier, profile.StretchMultiplier * 0.65f, t);
                float localRadius = Mathf.Lerp(profile.RadiusMultiplier, profile.RadiusMultiplier * 0.78f, t);

                if (i > 0 && (splatData.EventKind == PaintEventKind.RagdollImpact || splatData.SplatType == PaintSplatType.HeavyImpact || splatData.SplatType == PaintSplatType.RagdollImpact))
                {
                    uint localSeed = unchecked((uint)(splatData.PatternSeed + (i * 251)));
                    localRadius *= Mathf.Lerp(0.24f, 0.52f, Hash01(localSeed + 17u));
                    localStretch *= Mathf.Lerp(0.42f, 0.9f, Hash01(localSeed + 29u));
                }
                else if (i > 0 && (splatData.EventKind == PaintEventKind.Land || splatData.EventKind == PaintEventKind.WallLaunch))
                {
                    uint localSeed = unchecked((uint)(splatData.PatternSeed + (i * 173)));
                    localRadius *= Mathf.Lerp(0.32f, 0.66f, Hash01(localSeed + 11u));
                    localStretch *= Mathf.Lerp(0.55f, 0.95f, Hash01(localSeed + 19u));
                }

                SpawnDecal(color, splatData, profile, offset, i, localRadius, localStretch, dragDirection);
            }
        }

        private Vector3 ComputeSplatOffset(PaintSplatData splatData, Vector3 normal, Vector3 dragDirection, Vector3 bitangent, int localIndex, int splatCount)
        {
            if (localIndex <= 0)
            {
                return Vector3.zero;
            }

            uint seed = unchecked((uint)(splatData.PatternSeed + (localIndex * 4099)));
            float force01 = Mathf.InverseLerp(3f, 24f, splatData.ForceMagnitude);
            float count01 = localIndex / Mathf.Max(1f, splatCount - 1f);
            float side = Hash01(seed + 3u) < 0.5f ? -1f : 1f;

            if (splatData.EventKind == PaintEventKind.RagdollImpact || splatData.SplatType == PaintSplatType.HeavyImpact || splatData.SplatType == PaintSplatType.RagdollImpact)
            {
                float cone = Mathf.Lerp(0.18f, 1.25f, Hash01(seed + 7u));
                float lateral = (Hash01(seed + 11u) * 2f - 1f) * Mathf.Lerp(0.18f, 1.05f, force01);
                float distance = splatData.Radius * Mathf.Lerp(0.42f, 1.85f, force01) * cone;
                return (dragDirection * distance) + (bitangent * lateral * splatData.Radius);
            }

            if (splatData.EventKind == PaintEventKind.Land)
            {
                float angle = Hash01(seed + 13u) * Mathf.PI * 2f;
                float distance = splatData.Radius * Mathf.Lerp(0.18f, 1.15f, Hash01(seed + 17u)) * Mathf.Lerp(0.55f, 1.25f, force01);
                Vector3 radial = (dragDirection * Mathf.Cos(angle)) + (bitangent * Mathf.Sin(angle));
                return radial.normalized * distance;
            }

            if (splatData.EventKind == PaintEventKind.WallStick || splatData.EventKind == PaintEventKind.Move || splatData.EventKind == PaintEventKind.FloodDrip)
            {
                float along = splatData.Radius * Mathf.Lerp(0.12f, 0.56f, count01);
                float lateral = side * splatData.Radius * Mathf.Lerp(0.06f, 0.28f, Hash01(seed + 23u));
                return (dragDirection * along) + (bitangent * lateral);
            }

            float defaultAlong = splatData.Radius * Mathf.Lerp(0.14f, 0.72f, count01);
            float defaultLateral = side * splatData.Radius * Mathf.Lerp(0.08f, 0.38f, Hash01(seed + 31u));
            return (dragDirection * defaultAlong) + (bitangent * defaultLateral);
        }

        private SplatVisualProfile BuildVisualProfile(PaintSplatData splatData, bool wet)
        {
            float localWashout = Mathf.Clamp01(_roomWashout01 + (wet ? 0.32f : 0f));
            SplatVisualProfile profile = new SplatVisualProfile
            {
                RadiusMultiplier = 1f,
                AlphaMultiplier = wet ? 0.75f : 1f,
                StretchMultiplier = Mathf.Clamp(splatData.StretchAmount * stretchMultiplier, stretchClamp.x, stretchClamp.y),
                AdditionalSplats = 0,
                Lifetime = temporaryLifetimeSeconds,
                ForceTemporary = false,
                ForcePermanent = false,
                PreferDirectionalMask = false
            };

            switch (splatData.EventKind)
            {
                case PaintEventKind.Move:
                    profile.RadiusMultiplier = 0.65f;
                    profile.AlphaMultiplier *= 0.65f;
                    profile.StretchMultiplier *= 1.45f;
                    profile.AdditionalSplats = 0;
                    profile.ForceTemporary = true;
                    profile.Lifetime = temporaryLifetimeSeconds * 0.8f;
                    profile.PreferDirectionalMask = true;
                    break;
                case PaintEventKind.Land:
                    profile.RadiusMultiplier = Mathf.Lerp(1.05f, 1.55f, Mathf.InverseLerp(4f, 18f, splatData.ForceMagnitude));
                    profile.AlphaMultiplier *= 1.05f;
                    profile.StretchMultiplier *= Mathf.Lerp(1.1f, 1.65f, Mathf.InverseLerp(4f, 18f, splatData.ForceMagnitude));
                    profile.AdditionalSplats = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(4f, 18f, splatData.ForceMagnitude) * 3f), 1, 3);
                    profile.Lifetime = heavyTemporaryLifetimeSeconds;
                    if (splatData.ForceMagnitude >= landingPermanentForceThreshold)
                    {
                        profile.ForcePermanent = true;
                    }
                    break;
                case PaintEventKind.WallStick:
                    profile.RadiusMultiplier = 0.9f;
                    profile.AlphaMultiplier *= 0.8f;
                    profile.StretchMultiplier *= 2.2f;
                    profile.AdditionalSplats = 2;
                    profile.ForceTemporary = true;
                    profile.PreferDirectionalMask = true;
                    break;
                case PaintEventKind.WallLaunch:
                    profile.RadiusMultiplier = 1.05f;
                    profile.AlphaMultiplier *= 1.1f;
                    profile.StretchMultiplier *= 1.8f;
                    profile.AdditionalSplats = 2;
                    profile.PreferDirectionalMask = true;
                    break;
                case PaintEventKind.Punch:
                    profile.RadiusMultiplier = 0.85f;
                    profile.AlphaMultiplier *= 1.15f;
                    profile.StretchMultiplier *= 1.7f;
                    profile.AdditionalSplats = 1;
                    profile.PreferDirectionalMask = true;
                    break;
                case PaintEventKind.RagdollImpact:
                    profile.RadiusMultiplier = Mathf.Lerp(1.45f, 2.25f, Mathf.InverseLerp(6f, 24f, splatData.ForceMagnitude));
                    profile.AlphaMultiplier *= 1.25f;
                    profile.StretchMultiplier *= Mathf.Lerp(1.55f, 3.2f, Mathf.InverseLerp(6f, 24f, splatData.ForceMagnitude));
                    profile.AdditionalSplats = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(3f, 7f, Mathf.InverseLerp(6f, 24f, splatData.ForceMagnitude))), 3, 7);
                    profile.ForcePermanent = true;
                    profile.PreferDirectionalMask = true;
                    break;
                case PaintEventKind.TaskInteract:
                    profile.RadiusMultiplier = 0.82f;
                    profile.AlphaMultiplier *= 0.96f;
                    profile.StretchMultiplier *= 1.05f;
                    profile.AdditionalSplats = 0;
                    profile.ForcePermanent = true;
                    break;
                case PaintEventKind.ThrownObjectImpact:
                    profile.RadiusMultiplier = 1.12f;
                    profile.AlphaMultiplier *= 1.08f;
                    profile.StretchMultiplier *= 1.55f;
                    profile.AdditionalSplats = 2;
                    profile.PreferDirectionalMask = true;
                    if (splatData.ForceMagnitude >= thrownPermanentForceThreshold)
                    {
                        profile.ForcePermanent = true;
                    }
                    break;
                case PaintEventKind.FloodDrip:
                    profile.RadiusMultiplier = 0.68f;
                    profile.AlphaMultiplier *= 0.74f;
                    profile.StretchMultiplier *= 1.35f;
                    profile.AdditionalSplats = 0;
                    profile.ForceTemporary = true;
                    break;
                case PaintEventKind.FloodBurst:
                    profile.RadiusMultiplier = 1.05f;
                    profile.AlphaMultiplier *= 0.9f;
                    profile.StretchMultiplier *= 1f;
                    profile.AdditionalSplats = 1;
                    profile.ForceTemporary = true;
                    break;
            }

            profile.AlphaMultiplier *= 1f - (localWashout * floodWashoutStrength);
            profile.StretchMultiplier = Mathf.Clamp(profile.StretchMultiplier, stretchClamp.x, stretchClamp.y);
            return profile;
        }

        private Vector3 ResolveDragDirection(PaintSplatData splatData, Vector3 normal)
        {
            Vector3 preferredDirection = splatData.TangentDirection.sqrMagnitude > 0.0001f
                ? splatData.TangentDirection
                : splatData.VelocityDirection;

            Vector3 dragDirection = Vector3.ProjectOnPlane(preferredDirection, normal);
            if (dragDirection.sqrMagnitude < 0.0001f)
            {
                dragDirection = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.7f ? Vector3.right : Vector3.up);
            }

            return dragDirection.normalized;
        }

        private void SpawnDecal(
            Color color,
            PaintSplatData splatData,
            SplatVisualProfile profile,
            Vector3 offset,
            int localIndex,
            float localRadiusMultiplier,
            float localStretch,
            Vector3 dragDirection)
        {
            DecalInstance instance = AcquireDecalInstance();
            if (instance == null)
            {
                return;
            }

            instance.GameObject.SetActive(true);
            Vector3 normal = splatData.Normal.sqrMagnitude > 0.0001f ? splatData.Normal.normalized : Vector3.up;
            instance.Transform.position = splatData.Position + (normal * zOffset) + offset;

            Vector3 up = dragDirection.sqrMagnitude > 0.0001f
                ? dragDirection
                : Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.7f ? Vector3.right : Vector3.up).normalized;
            Quaternion rotation = Quaternion.LookRotation(normal, up);
            float randomAngle = 0f;
            if (randomizeRotation)
            {
                int rotationSeed = splatData.PatternSeed + (localIndex * 131);
                randomAngle = Mathf.Repeat(rotationSeed * 13.137f, 360f);
            }

            rotation = Quaternion.AngleAxis(splatData.RotationDegrees + randomAngle, normal) * rotation;
            instance.Transform.rotation = rotation;

            float baseScale = Mathf.Clamp(splatData.Radius * localRadiusMultiplier, 0.05f, 3.1f);
            float stretchForce = Mathf.Lerp(1f, localStretch, Mathf.InverseLerp(0f, 24f, splatData.ForceMagnitude));
            float xScale = baseScale * stretchForce;
            float yScale = baseScale;
            if (randomizeFlip && ((splatData.PatternSeed + localIndex) & 1) == 0)
            {
                xScale *= -1f;
            }

            float sign = xScale < 0f ? -1f : 1f;
            float maxMajor = splatData.EventKind == PaintEventKind.RagdollImpact || splatData.SplatType == PaintSplatType.HeavyImpact || splatData.SplatType == PaintSplatType.RagdollImpact
                ? maxHeavyImpactMajorAxis
                : maxLocalizedDecalMajorAxis;
            float maxMinor = splatData.EventKind == PaintEventKind.RagdollImpact || splatData.SplatType == PaintSplatType.HeavyImpact || splatData.SplatType == PaintSplatType.RagdollImpact
                ? maxHeavyImpactMinorAxis
                : maxLocalizedDecalMinorAxis;

            xScale = sign * Mathf.Clamp(Mathf.Abs(xScale), 0.035f, Mathf.Max(0.04f, maxMajor));
            yScale = Mathf.Clamp(yScale, 0.035f, Mathf.Max(0.04f, maxMinor));
            instance.Transform.localScale = new Vector3(xScale, yScale, 1f);

            int materialIndex = ResolvePatternIndex(splatData, localIndex, profile.PreferDirectionalMask);
            if (_patternMaterials.Count > 0)
            {
                instance.Renderer.sharedMaterial = _patternMaterials[materialIndex];
            }

            float alpha = Mathf.Lerp(alphaRange.x, alphaRange.y, Mathf.Clamp01(splatData.Intensity * profile.AlphaMultiplier));
            Color renderedColor = Color.Lerp(Color.white, color, 0.96f);
            renderedColor.a = alpha;

            instance.Renderer.GetPropertyBlock(instance.PropertyBlock);
            instance.PropertyBlock.SetColor(BaseColorId, renderedColor);
            instance.PropertyBlock.SetColor(ColorId, renderedColor);
            instance.Renderer.SetPropertyBlock(instance.PropertyBlock);
            instance.InitialAlpha = alpha;

            bool permanent = splatData.Permanence == PaintSplatPermanence.Permanent;
            if (profile.ForcePermanent)
            {
                permanent = true;
            }
            else if (profile.ForceTemporary)
            {
                permanent = false;
            }

            if (permanent && CountPermanentDecals() >= maxPermanentEvidenceMarks)
            {
                permanent = false;
            }

            instance.Permanent = permanent;
            instance.SpawnAt = Time.time;
            instance.ExpireAt = permanent ? float.PositiveInfinity : (Time.time + Mathf.Max(0.25f, profile.Lifetime));
            _activeDecals.Add(instance);

            if (debugDrawDirection)
            {
                Debug.DrawRay(splatData.Position + (normal * 0.06f), dragDirection * (splatData.Radius * 1.2f), permanent ? Color.cyan : Color.yellow, 1.6f);
            }

            TrimIfOverBudget();
        }

        private int CountPermanentDecals()
        {
            int count = 0;
            for (int i = 0; i < _activeDecals.Count; i++)
            {
                if (_activeDecals[i].Permanent)
                {
                    count++;
                }
            }

            return count;
        }

        private int ResolvePatternIndex(PaintSplatData splatData, int localIndex, bool preferDirectionalMask)
        {
            if (_patternMaterials.Count <= 1)
            {
                return 0;
            }

            if (_patternLookup.TryGetValue(splatData.SplatType, out int[] mappedIndices) && mappedIndices.Length > 0)
            {
                int mapped = Mathf.Abs((splatData.PatternSeed + localIndex) % mappedIndices.Length);
                return Mathf.Clamp(mappedIndices[mapped], 0, _patternMaterials.Count - 1);
            }

            if (preferDirectionalMask)
            {
                return Mathf.Abs((splatData.PatternSeed + (localIndex * 3)) % _patternMaterials.Count);
            }

            return Mathf.Abs((splatData.PatternSeed + localIndex) % _patternMaterials.Count);
        }

        private DecalInstance AcquireDecalInstance()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }

            if (_activeDecals.Count > 0)
            {
                DecalInstance recycled = _activeDecals[0];
                _activeDecals.RemoveAt(0);
                return recycled;
            }

            return CreateDecalInstance();
        }

        private void TrimIfOverBudget()
        {
            while (_activeDecals.Count > maxSplatDecals)
            {
                ReleaseDecal(_activeDecals[0]);
                _activeDecals.RemoveAt(0);
            }
        }

        private void TickDecals()
        {
            float now = Time.time;
            for (int i = _activeDecals.Count - 1; i >= 0; i--)
            {
                DecalInstance instance = _activeDecals[i];
                if (instance == null || instance.GameObject == null)
                {
                    _activeDecals.RemoveAt(i);
                    continue;
                }

                if (instance.Permanent)
                {
                    continue;
                }

                if (now >= instance.ExpireAt)
                {
                    ReleaseDecal(instance);
                    _activeDecals.RemoveAt(i);
                    continue;
                }

                float life01 = Mathf.InverseLerp(instance.ExpireAt, instance.SpawnAt, now);
                float alpha = instance.InitialAlpha * life01;
                instance.Renderer.GetPropertyBlock(instance.PropertyBlock);
                Color currentColor = instance.PropertyBlock.GetColor(ColorId);
                currentColor.a = alpha;
                instance.PropertyBlock.SetColor(ColorId, currentColor);
                instance.PropertyBlock.SetColor(BaseColorId, currentColor);
                instance.Renderer.SetPropertyBlock(instance.PropertyBlock);
            }
        }

        private static void TickGlobalDecals()
        {
            float now = Time.time;
            for (int i = GlobalDecals.Count - 1; i >= 0; i--)
            {
                GlobalDecal decal = GlobalDecals[i];
                if (decal == null || decal.GameObject == null)
                {
                    GlobalDecals.RemoveAt(i);
                    continue;
                }

                if (now >= decal.ExpireAt)
                {
                    decal.GameObject.SetActive(false);
                    GlobalDecals.RemoveAt(i);
                    GlobalPool.Push(decal);
                }
            }
        }

        private void ReleaseDecal(DecalInstance instance)
        {
            if (instance == null || instance.GameObject == null)
            {
                return;
            }

            instance.GameObject.SetActive(false);
            _pool.Push(instance);
        }
    }
}