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
        [SerializeField, Min(0f)] private float stainBlendPerHit = 0.05f;
        [SerializeField, Min(0f)] private float decayPerSecond = 0.012f;

        [Header("Area Story Accumulation")]
        [SerializeField, Min(1f)] private float maxActivityScore = 40f;
        [SerializeField, Min(0f)] private float activityDecayPerSecond = 0.45f;
        [SerializeField, Min(0f)] private float activityToDensityMultiplier = 0.18f;
        [SerializeField, Min(0f)] private float activityToTintBlend = 0.16f;
        [SerializeField, Min(1)] private int maxPermanentEvidenceMarks = 48;
        [SerializeField, Min(0f)] private float movementWearThreshold = 4f;

        [Header("Flood Interaction")]
        [SerializeField, Min(0f)] private float floodWashoutStrength = 0.62f;
        [SerializeField, Min(0f)] private float floodDilutionStrength = 0.48f;
        [SerializeField] private Color wetRoomTint = new(0.72f, 0.8f, 0.9f, 1f);

        [Header("Splat Rendering")]
        [SerializeField, Min(8)] private int maxSplatDecals = 160;
        [SerializeField, Min(0.5f)] private float temporaryLifetimeSeconds = 7f;
        [SerializeField, Min(0.5f)] private float heavyTemporaryLifetimeSeconds = 12f;
        [SerializeField, Min(0f)] private float zOffset = 0.015f;
        [SerializeField] private List<Texture2D> splatPatterns = new();
        [SerializeField] private Shader splatShader;
        [SerializeField] private bool randomizeRotation = true;
        [SerializeField] private bool randomizeFlip = true;
        [SerializeField, Min(0f)] private float forceToDensityMultiplier = 0.22f;
        [SerializeField] private Vector2 alphaRange = new(0.26f, 0.9f);
        [SerializeField, Min(0f)] private float stretchMultiplier = 0.65f;
        [SerializeField] private Vector2 stretchClamp = new(1f, 4.5f);

        [Header("Type Lifetime Thresholds")]
        [SerializeField, Min(0f)] private float landingPermanentForceThreshold = 13f;
        [SerializeField, Min(0f)] private float thrownPermanentForceThreshold = 10f;

        [Header("Mask Preferences")]
        [SerializeField] private SplatTypePatternRule[] typePatternRules;

        [Header("Debug")]
        [SerializeField] private bool debugDrawDirection;

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

            if (_hasBaseColor)
            {
                Color neutralized = Color.Lerp(_stainColor, _baseColor, decayPerSecond * Time.deltaTime);
                float activityBlend = ActivityScore01 * activityToTintBlend;
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

        public void ApplyStain(Color color, PaintSplatData splatData, bool wet)
        {
            if (!_hasBaseColor)
            {
                CacheBaseColor();
            }

            float wetFactor = Mathf.Clamp01(_roomWashout01 + (wet ? 0.4f : 0f));
            float blend = Mathf.Clamp01(stainBlendPerHit * Mathf.Max(0.2f, splatData.Intensity));
            blend *= 1f - (wetFactor * floodDilutionStrength * 0.7f);

            _stainColor = Color.Lerp(_hasBaseColor ? _stainColor : color, color, blend);
            _stainColor = Color.Lerp(_stainColor, _baseColor, wetFactor * floodDilutionStrength * 0.2f);
            ApplyTint(_stainColor);

            RegisterActivity(splatData);
            SpawnDensityAdjustedSplats(color, splatData, wet || wetFactor > 0.01f);

            if (splatData.EventKind == PaintEventKind.Move)
            {
                _movementWear += Mathf.Clamp(splatData.Intensity * 0.35f, 0.08f, 0.5f);
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
                PatternIndex = Mathf.RoundToInt(position.x * 37f + position.z * 17f),
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

            Vector3 tangent = Vector3.ProjectOnPlane(splatData.VelocityDirection, splatData.Normal);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.Cross(splatData.Normal, Mathf.Abs(splatData.Normal.y) > 0.7f ? Vector3.right : Vector3.up);
            }

            decal.Transform.rotation = Quaternion.LookRotation(splatData.Normal, tangent.normalized);

            float baseScale = Mathf.Clamp(splatData.Radius, 0.06f, 2.6f);
            float stretch = Mathf.Clamp((1f + (splatData.ForceMagnitude * 0.08f)), 1f, 3.5f);
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
            wear.SplatType = PaintSplatType.Footstep;
            wear.Permanence = PaintSplatPermanence.Permanent;
            wear.Radius *= 0.65f;
            wear.Intensity *= 0.55f;
                        wear.PatternIndex += 97;
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
            SplatVisualProfile profile = BuildVisualProfile(splatData, wet);
            float activityBoost = ActivityScore01 * activityToDensityMultiplier;
            int splatCount = 1 + profile.AdditionalSplats + Mathf.Clamp(Mathf.FloorToInt(splatData.ForceMagnitude * forceToDensityMultiplier), 0, 4) + Mathf.Clamp(Mathf.FloorToInt(activityBoost * 3f), 0, 2);

            Vector3 normal = splatData.Normal.sqrMagnitude > 0.001f ? splatData.Normal.normalized : Vector3.up;
            Vector3 dragDirection = ResolveDragDirection(splatData, normal);
            Vector3 bitangent = Vector3.Cross(normal, dragDirection).normalized;

            for (int i = 0; i < splatCount; i++)
            {
                float along = i == 0 ? 0f : (splatData.Radius * 0.22f * i);
                float lateralSign = ((i & 1) == 0 ? -1f : 1f);
                float lateral = i == 0 ? 0f : (splatData.Radius * 0.12f * lateralSign * (1f + (0.2f * i)));
                Vector3 offset = (dragDirection * along) + (bitangent * lateral);
                float localStretch = Mathf.Lerp(profile.StretchMultiplier, profile.StretchMultiplier * 0.65f, i / Mathf.Max(1f, splatCount - 1f));
                float localRadius = Mathf.Lerp(profile.RadiusMultiplier, profile.RadiusMultiplier * 0.78f, i / Mathf.Max(1f, splatCount - 1f));
                SpawnDecal(color, splatData, profile, offset, i, localRadius, localStretch, dragDirection);
            }
        }

        private SplatVisualProfile BuildVisualProfile(PaintSplatData splatData, bool wet)
        {
            float localWashout = Mathf.Clamp01(_roomWashout01 + (wet ? 0.32f : 0f));
            SplatVisualProfile profile = new()
            {
                RadiusMultiplier = 1f,
                AlphaMultiplier = wet ? 0.75f : 1f,
                StretchMultiplier = Mathf.Clamp((1f + (splatData.ForceMagnitude * 0.08f)) * stretchMultiplier, stretchClamp.x, stretchClamp.y),
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
                    profile.RadiusMultiplier = 1.2f;
                    profile.AlphaMultiplier *= 1.05f;
                    profile.StretchMultiplier *= 1.1f;
                    profile.AdditionalSplats = 1;
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
                    profile.RadiusMultiplier = 1.45f;
                    profile.AlphaMultiplier *= 1.2f;
                    profile.StretchMultiplier *= Mathf.Lerp(1.3f, 2.4f, Mathf.InverseLerp(6f, 24f, splatData.ForceMagnitude));
                    profile.AdditionalSplats = 3;
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
            Vector3 preferredDirection = splatData.VelocityDirection.sqrMagnitude > 0.0001f
                ? splatData.VelocityDirection
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
                int rotationSeed = splatData.PatternIndex + (localIndex * 131);
                randomAngle = Mathf.Repeat(rotationSeed * 13.137f, 360f);
            }

            rotation = Quaternion.AngleAxis(randomAngle, normal) * rotation;
            instance.Transform.rotation = rotation;

            float baseScale = Mathf.Clamp(splatData.Radius * localRadiusMultiplier, 0.05f, 3.1f);
            float stretchForce = Mathf.Lerp(1f, localStretch, Mathf.InverseLerp(0f, 24f, splatData.ForceMagnitude));
            float xScale = baseScale * stretchForce;
            float yScale = baseScale;
            if (randomizeFlip && ((splatData.PatternIndex + localIndex) & 1) == 0)
            {
                xScale *= -1f;
            }

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

        private int ResolvePatternIndex(PaintSplatData splatData, int localIndex, bool preferDirectional)
        {
            if (_patternMaterials.Count == 0)
            {
                return 0;
            }

            if (_patternLookup.TryGetValue(splatData.SplatType, out int[] preferredIndices) && preferredIndices != null && preferredIndices.Length > 0)
            {
                int preferredIndex = Mathf.Abs(splatData.PatternIndex + (localIndex * 17)) % preferredIndices.Length;
                return Mathf.Clamp(preferredIndices[preferredIndex], 0, _patternMaterials.Count - 1);
            }

            int patternCount = _patternMaterials.Count;
            int baseIndex = Mathf.Abs(splatData.PatternIndex + localIndex);
            if (preferDirectional && patternCount > 1)
            {
                baseIndex += 1;
            }

            return baseIndex % patternCount;
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
                if (decal.Permanent)
                {
                    continue;
                }

                if (now >= decal.ExpireAt)
                {
                    _activeDecals.RemoveAt(i);
                    decal.GameObject.SetActive(false);
                    _pool.Push(decal);
                    continue;
                }

                float normalizedLife = Mathf.InverseLerp(decal.SpawnAt, decal.ExpireAt, now);
                float alphaMultiplier = 1f - normalizedLife;
                decal.Renderer.GetPropertyBlock(decal.PropertyBlock);
                Color fadeColor = decal.PropertyBlock.GetColor(BaseColorId);
                fadeColor.a = decal.InitialAlpha * alphaMultiplier;
                decal.PropertyBlock.SetColor(BaseColorId, fadeColor);
                decal.PropertyBlock.SetColor(ColorId, fadeColor);
                decal.Renderer.SetPropertyBlock(decal.PropertyBlock);
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
