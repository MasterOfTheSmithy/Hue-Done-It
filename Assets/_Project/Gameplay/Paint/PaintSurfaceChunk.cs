// File: Assets/_Project/Gameplay/Paint/PaintSurfaceChunk.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Paint
{
    public enum PaintSurfaceProjectionPlane : byte
    {
        LocalXY = 0,
        LocalXZ = 1,
        LocalYZ = 2
    }

    [DisallowMultipleComponent]
    public sealed class PaintSurfaceChunk : MonoBehaviour
    {
        [Header("Projection")]
        [SerializeField] private PaintSurfaceProjectionPlane projectionPlane = PaintSurfaceProjectionPlane.LocalXZ;
        [SerializeField] private Vector2 surfaceSize = new Vector2(8f, 8f);
        [SerializeField] private Vector2 surfaceOffset = Vector2.zero;
        [SerializeField, Min(0.01f)] private float maxProjectionDepth = 1f;

        [Header("Textures")]
        [SerializeField, Min(64)] private int textureResolution = 128;

        [Header("Drying")]
        [SerializeField, Min(0.01f)] private float wetnessDecayPerSecond = 0.18f;
        [SerializeField, Min(0.01f)] private float ageRisePerSecond = 0.055f;
        [SerializeField, Min(0.01f)] private float temporaryFadePerSecond = 0.12f;

        [Header("Stamp Behavior")]
        [SerializeField, Range(0f, 1f)] private float directionalStretchStrength = 0.7f;
        [SerializeField, Range(0f, 1f)] private float irregularityStrength = 0.34f;
        [SerializeField, Range(0f, 1f)] private float dropletScatterStrength = 0.4f;
        [SerializeField, Min(0f)] private float minimumStampRadius = 0.03f;
        [SerializeField, Min(0f)] private float projectionPadding = 0.15f;
        [SerializeField, Min(0.01f)] private float dryTickIntervalSeconds = 0.18f;

        [Header("Runtime")]
        [SerializeField] private PaintSurfaceMaterialDriver materialDriver;

        private Texture2D _paintColorTexture;
        private Texture2D _paintWetnessTexture;
        private Texture2D _paintAgeTexture;

        private Color32[] _paintColorPixels;
        private Color32[] _paintWetnessPixels;
        private Color32[] _paintAgePixels;

        private bool _dirty;
        private bool _hasActivePaint;
        private float _lastPaintTime;
        private float _nextApplyTime;
        private float _nextDryTickTime;
        private Color _latestColor = Color.white;
        private float _fallbackColorBlend;

        public Bounds WorldBounds
        {
            get
            {
                Vector3 center = transform.TransformPoint(GetLocalPlaneCenter());
                Vector3 extents = GetWorldExtents();
                return new Bounds(center, extents * 2f);
            }
        }

        public bool IsMaterialReady => materialDriver != null && materialDriver.SupportsPaintTextures;

        private void Awake()
        {
            EnsureTextures();
            PaintSurfaceRegistry.Register(this);

            if (materialDriver == null)
            {
                materialDriver = GetComponent<PaintSurfaceMaterialDriver>();
            }

            ApplyTexturesToMaterial();
        }

        private void OnEnable()
        {
            PaintSurfaceRegistry.Register(this);
            ApplyTexturesToMaterial();
        }

        private void OnDisable()
        {
            PaintSurfaceRegistry.Unregister(this);
        }

        private void OnDestroy()
        {
            PaintSurfaceRegistry.Unregister(this);
        }

        private void Update()
        {
            if (_paintColorPixels == null)
            {
                return;
            }

            if (_hasActivePaint && Time.time >= _nextDryTickTime)
            {
                _nextDryTickTime = Time.time + dryTickIntervalSeconds;
                TickDrying(dryTickIntervalSeconds);
            }

            if (_dirty && Time.time >= _nextApplyTime)
            {
                _nextApplyTime = Time.time + 0.08f;
                UploadTextures();
            }

            if (_hasActivePaint && !_dirty && (Time.time - _lastPaintTime) > 12f)
            {
                _hasActivePaint = false;
            }
        }

        public void Configure(PaintSurfaceProjectionPlane plane, Vector2 size, float depth, PaintSurfaceMaterialDriver driver)
        {
            projectionPlane = plane;
            surfaceSize = new Vector2(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y));
            maxProjectionDepth = Mathf.Max(0.01f, depth);
            materialDriver = driver;
        }

        public bool CanProject(PaintBurstCommand burst)
        {
            if (!enabled || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Vector3 localPoint = transform.InverseTransformPoint(burst.Position);
            float planeDepth = GetPlaneDepth(localPoint);
            Vector2 localRadius = WorldRadiusToLocalPlaneRadius(burst.Radius + projectionPadding);
            float localDepthRadius = WorldRadiusToLocalDepth(burst.Radius + projectionPadding);
            if (Mathf.Abs(planeDepth) > maxProjectionDepth + localDepthRadius)
            {
                return false;
            }

            Vector2 planePoint = GetPlanePoint(localPoint) - surfaceOffset;
            Vector2 half = surfaceSize * 0.5f;
            return planePoint.x >= (-half.x - localRadius.x) &&
                   planePoint.x <= (half.x + localRadius.x) &&
                   planePoint.y >= (-half.y - localRadius.y) &&
                   planePoint.y <= (half.y + localRadius.y);
        }

        public void ApplyBurst(PaintBurstCommand burst)
        {
            EnsureTextures();

            Vector2 surfaceUv;
            Vector2 surfaceVelocity2D;
            float depth;
            if (!TryProjectWorldPoint(burst.Position, burst.Velocity, out surfaceUv, out surfaceVelocity2D, out depth))
            {
                return;
            }

            if (Mathf.Abs(depth) > maxProjectionDepth + WorldRadiusToLocalDepth(burst.Radius))
            {
                return;
            }

            float clampedRadius = Mathf.Max(minimumStampRadius, burst.Radius);
            float coverage = Mathf.Clamp01(Mathf.Lerp(0.18f, 1f, burst.Volume));
            float stretch = Mathf.Lerp(1f, 2.4f, Mathf.Clamp01(burst.Speed / 14f) * directionalStretchStrength);

            StampMainSplat(surfaceUv, surfaceVelocity2D, clampedRadius, stretch, burst.Color, coverage, burst.Seed, burst.Permanent);

            int dropletCount = GetDropletCount(burst);
            for (int i = 0; i < dropletCount; i++)
            {
                StampDroplet(surfaceUv, surfaceVelocity2D, clampedRadius, burst.Color, coverage, burst.Seed, i, burst.Permanent);
            }

            _latestColor = burst.Color;
            _fallbackColorBlend = Mathf.Clamp01(_fallbackColorBlend + coverage * 0.2f);
            _lastPaintTime = Time.time;
            _hasActivePaint = true;
            _dirty = true;
        }

        private void EnsureTextures()
        {
            int resolution = Mathf.Clamp(textureResolution, 64, 1024);
            if (_paintColorTexture != null && _paintColorTexture.width == resolution)
            {
                return;
            }

            _paintColorTexture = CreateTexture("PaintColorTex", resolution);
            _paintWetnessTexture = CreateTexture("PaintWetnessTex", resolution);
            _paintAgeTexture = CreateTexture("PaintAgeTex", resolution);

            int pixelCount = resolution * resolution;
            _paintColorPixels = new Color32[pixelCount];
            _paintWetnessPixels = new Color32[pixelCount];
            _paintAgePixels = new Color32[pixelCount];

            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixelCount; i++)
            {
                _paintColorPixels[i] = clear;
                _paintWetnessPixels[i] = new Color32(0, 0, 0, 255);
                _paintAgePixels[i] = new Color32(255, 255, 255, 255);
            }

            UploadTextures();
        }

        private void TickDrying(float deltaTime)
        {
            bool changed = false;
            int count = _paintColorPixels.Length;
            if (count == 0)
            {
                return;
            }

            byte wetnessDecay = (byte)Mathf.Clamp(Mathf.RoundToInt(wetnessDecayPerSecond * deltaTime * 255f), 0, 255);
            byte ageRise = (byte)Mathf.Clamp(Mathf.RoundToInt(ageRisePerSecond * deltaTime * 255f), 0, 255);
            byte temporaryFade = (byte)Mathf.Clamp(Mathf.RoundToInt(temporaryFadePerSecond * deltaTime * 255f), 0, 255);

            for (int i = 0; i < count; i++)
            {
                Color32 wet = _paintWetnessPixels[i];
                Color32 age = _paintAgePixels[i];
                Color32 paint = _paintColorPixels[i];

                byte oldWet = wet.r;
                byte oldAge = age.r;
                byte oldCoverage = paint.a;

                if (wet.r > 0)
                {
                    wet.r = (byte)Mathf.Max(0, wet.r - wetnessDecay);
                    wet.g = wet.r;
                    wet.b = wet.r;
                }

                if (age.r < 255)
                {
                    age.r = (byte)Mathf.Min(255, age.r + ageRise);
                    age.g = age.r;
                    age.b = age.r;
                }

                if (paint.a > 0 && age.r > 180)
                {
                    paint.a = (byte)Mathf.Max(0, paint.a - temporaryFade);
                }

                if (wet.r != oldWet || age.r != oldAge || paint.a != oldCoverage)
                {
                    _paintWetnessPixels[i] = wet;
                    _paintAgePixels[i] = age;
                    _paintColorPixels[i] = paint;
                    changed = true;
                }
            }

            if (_fallbackColorBlend > 0f)
            {
                _fallbackColorBlend = Mathf.Max(0f, _fallbackColorBlend - (temporaryFadePerSecond * 0.35f * deltaTime));
            }

            if (changed)
            {
                _dirty = true;
            }
        }

        private void StampMainSplat(Vector2 uv, Vector2 velocity2D, float worldRadius, float stretch, Color32 color, float coverage, uint seed, bool permanent)
        {
            int resolution = _paintColorTexture.width;
            Vector2 localRadius = WorldRadiusToLocalPlaneRadius(worldRadius);
            float radiusU = localRadius.x / Mathf.Max(0.0001f, surfaceSize.x);
            float radiusV = localRadius.y / Mathf.Max(0.0001f, surfaceSize.y);

            Vector2 direction = velocity2D.sqrMagnitude > 0.0001f ? velocity2D.normalized : Vector2.up;
            float anisotropy = Mathf.Lerp(1f, stretch, Mathf.Clamp01(velocity2D.magnitude));
            float angle = Mathf.Atan2(direction.y, direction.x);

            StampEllipse(resolution, uv, radiusU, radiusV, anisotropy, angle, color, coverage, seed, permanent, 1f);
        }

        private void StampDroplet(Vector2 uv, Vector2 velocity2D, float worldRadius, Color32 color, float coverage, uint seed, int dropletIndex, bool permanent)
        {
            uint localSeed = seed + (uint)(dropletIndex * 977);
            Vector2 direction = velocity2D.sqrMagnitude > 0.0001f ? velocity2D.normalized : Vector2.up;

            float spread = worldRadius * Mathf.Lerp(0.15f, 0.9f, Hash01(localSeed + 14u) * dropletScatterStrength + 0.1f);
            float lateral = (Hash01(localSeed + 9u) * 2f - 1f) * worldRadius * 0.65f;

            Vector2 tangent = new Vector2(-direction.y, direction.x);
            Vector2 worldOffset = (direction * spread) + (tangent * lateral);
            Vector2 planeScale = GetLocalPlaneWorldScale();
            Vector2 localOffset = new Vector2(
                worldOffset.x / Mathf.Max(0.0001f, planeScale.x),
                worldOffset.y / Mathf.Max(0.0001f, planeScale.y));
            Vector2 uvOffset = new Vector2(localOffset.x / Mathf.Max(0.0001f, surfaceSize.x), localOffset.y / Mathf.Max(0.0001f, surfaceSize.y));

            float dropletRadius = worldRadius * Mathf.Lerp(0.08f, 0.32f, Hash01(localSeed + 3u));
            Vector2 localDropletRadius = WorldRadiusToLocalPlaneRadius(dropletRadius);
            float dropletCoverage = coverage * Mathf.Lerp(0.45f, 0.9f, Hash01(localSeed + 21u));
            float angle = Hash01(localSeed + 77u) * Mathf.PI * 2f;

            StampEllipse(_paintColorTexture.width, uv + uvOffset, localDropletRadius.x / Mathf.Max(0.0001f, surfaceSize.x), localDropletRadius.y / Mathf.Max(0.0001f, surfaceSize.y), Mathf.Lerp(1f, 1.8f, Hash01(localSeed + 31u)), angle, color, dropletCoverage, localSeed, permanent, 0.65f);
        }

        private void StampEllipse(int resolution, Vector2 centerUv, float radiusU, float radiusV, float anisotropy, float angle, Color32 color, float coverage, uint seed, bool permanent, float weightMultiplier)
        {
            int centerX = Mathf.RoundToInt(centerUv.x * (resolution - 1));
            int centerY = Mathf.RoundToInt(centerUv.y * (resolution - 1));
            int extentX = Mathf.CeilToInt(radiusU * resolution * anisotropy * 1.35f);
            int extentY = Mathf.CeilToInt(radiusV * resolution * 1.35f);
            if (extentX <= 0 || extentY <= 0)
            {
                return;
            }

            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            int minX = Mathf.Clamp(centerX - extentX, 0, resolution - 1);
            int maxX = Mathf.Clamp(centerX + extentX, 0, resolution - 1);
            int minY = Mathf.Clamp(centerY - extentY, 0, resolution - 1);
            int maxY = Mathf.Clamp(centerY + extentY, 0, resolution - 1);
            float invRadiusX = 1f / Mathf.Max(0.0001f, extentX);
            float invRadiusY = 1f / Mathf.Max(0.0001f, extentY);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float localX = (x - centerX) * invRadiusX;
                    float localY = (y - centerY) * invRadiusY;
                    float rotatedX = (localX * cos) - (localY * sin);
                    float rotatedY = (localX * sin) + (localY * cos);
                    rotatedX /= Mathf.Max(1f, anisotropy);
                    float radial = Mathf.Sqrt((rotatedX * rotatedX) + (rotatedY * rotatedY));
                    if (radial > 1.1f)
                    {
                        continue;
                    }

                    float lobeNoise = Mathf.Sin((Mathf.Atan2(rotatedY, rotatedX) * 4f) + (Hash01(seed + 11u) * 6.28318f)) * 0.08f;
                    float irregularity = (Hash21(seed, x, y) - 0.5f) * irregularityStrength;
                    float shape = radial + lobeNoise + irregularity;
                    if (shape > 1f)
                    {
                        continue;
                    }

                    float alpha = Mathf.Clamp01(1f - shape);
                    alpha = alpha * alpha;
                    alpha *= coverage * weightMultiplier;
                    int pixelIndex = (y * resolution) + x;
                    BlendPaintPixel(pixelIndex, color, alpha, permanent);
                }
            }
        }

        private void BlendPaintPixel(int index, Color32 color, float alpha, bool permanent)
        {
            alpha = Mathf.Clamp01(alpha);
            if (alpha <= 0.001f)
            {
                return;
            }

            Color32 existingColor = _paintColorPixels[index];
            Color32 wetness = _paintWetnessPixels[index];
            Color32 age = _paintAgePixels[index];

            float existingAlpha = existingColor.a / 255f;
            float targetAlpha = Mathf.Clamp01(existingAlpha + alpha);
            float blend = targetAlpha <= 0.0001f ? 0f : alpha / targetAlpha;

            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(existingColor.r, color.r, blend)), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(existingColor.g, color.g, blend)), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(existingColor.b, color.b, blend)), 0, 255);
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(targetAlpha * 255f), 0, 255);
            _paintColorPixels[index] = new Color32(r, g, b, a);

            byte targetWetness = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(wetness.r, permanent ? 255f : 220f, alpha)), 0, 255);
            wetness.r = targetWetness;
            wetness.g = targetWetness;
            wetness.b = targetWetness;
            wetness.a = 255;
            _paintWetnessPixels[index] = wetness;

            age.r = 0;
            age.g = 0;
            age.b = 0;
            age.a = 255;
            _paintAgePixels[index] = age;
        }

        private bool TryProjectWorldPoint(Vector3 worldPoint, Vector3 velocity, out Vector2 uv, out Vector2 velocity2D, out float planeDepth)
        {
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            Vector2 planePoint = GetPlanePoint(localPoint) - surfaceOffset;
            planeDepth = GetPlaneDepth(localPoint);
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            velocity2D = GetPlanePoint(localVelocity);

            Vector2 half = surfaceSize * 0.5f;
            uv = new Vector2(Mathf.InverseLerp(-half.x, half.x, planePoint.x), Mathf.InverseLerp(-half.y, half.y, planePoint.y));
            if (uv.x < -0.25f || uv.x > 1.25f || uv.y < -0.25f || uv.y > 1.25f)
            {
                return false;
            }

            return true;
        }

        private Vector2 WorldRadiusToLocalPlaneRadius(float worldRadius)
        {
            Vector2 scale = GetLocalPlaneWorldScale();
            return new Vector2(
                worldRadius / Mathf.Max(0.0001f, scale.x),
                worldRadius / Mathf.Max(0.0001f, scale.y));
        }

        private float WorldRadiusToLocalDepth(float worldRadius)
        {
            Vector3 scale = transform.lossyScale;
            float axisScale = projectionPlane switch
            {
                PaintSurfaceProjectionPlane.LocalXY => Mathf.Abs(scale.z),
                PaintSurfaceProjectionPlane.LocalYZ => Mathf.Abs(scale.x),
                _ => Mathf.Abs(scale.y)
            };

            return worldRadius / Mathf.Max(0.0001f, axisScale);
        }

        private Vector2 GetLocalPlaneWorldScale()
        {
            Vector3 scale = transform.lossyScale;
            return projectionPlane switch
            {
                PaintSurfaceProjectionPlane.LocalXY => new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
                PaintSurfaceProjectionPlane.LocalYZ => new Vector2(Mathf.Abs(scale.z), Mathf.Abs(scale.y)),
                _ => new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
            };
        }

        private Vector2 GetPlanePoint(Vector3 localPoint)
        {
            switch (projectionPlane)
            {
                case PaintSurfaceProjectionPlane.LocalXY: return new Vector2(localPoint.x, localPoint.y);
                case PaintSurfaceProjectionPlane.LocalYZ: return new Vector2(localPoint.z, localPoint.y);
                default: return new Vector2(localPoint.x, localPoint.z);
            }
        }

        private float GetPlaneDepth(Vector3 localPoint)
        {
            switch (projectionPlane)
            {
                case PaintSurfaceProjectionPlane.LocalXY: return localPoint.z;
                case PaintSurfaceProjectionPlane.LocalYZ: return localPoint.x;
                default: return localPoint.y;
            }
        }

        private Vector3 GetLocalPlaneCenter()
        {
            switch (projectionPlane)
            {
                case PaintSurfaceProjectionPlane.LocalXY: return new Vector3(surfaceOffset.x, surfaceOffset.y, 0f);
                case PaintSurfaceProjectionPlane.LocalYZ: return new Vector3(0f, surfaceOffset.y, surfaceOffset.x);
                default: return new Vector3(surfaceOffset.x, 0f, surfaceOffset.y);
            }
        }

        private Vector3 GetWorldExtents()
        {
            Vector3 localExtents;
            switch (projectionPlane)
            {
                case PaintSurfaceProjectionPlane.LocalXY:
                    localExtents = new Vector3(surfaceSize.x * 0.5f, surfaceSize.y * 0.5f, maxProjectionDepth);
                    break;
                case PaintSurfaceProjectionPlane.LocalYZ:
                    localExtents = new Vector3(maxProjectionDepth, surfaceSize.y * 0.5f, surfaceSize.x * 0.5f);
                    break;
                default:
                    localExtents = new Vector3(surfaceSize.x * 0.5f, maxProjectionDepth, surfaceSize.y * 0.5f);
                    break;
            }

            Vector3 right = transform.TransformVector(new Vector3(localExtents.x, 0f, 0f));
            Vector3 up = transform.TransformVector(new Vector3(0f, localExtents.y, 0f));
            Vector3 forward = transform.TransformVector(new Vector3(0f, 0f, localExtents.z));
            return new Vector3(Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(forward.x), Mathf.Abs(right.y) + Mathf.Abs(up.y) + Mathf.Abs(forward.y), Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(forward.z));
        }

        private int GetDropletCount(PaintBurstCommand burst)
        {
            float speed01 = Mathf.Clamp01(burst.Speed / 16f);
            float volume01 = Mathf.Clamp01(burst.Volume);
            float eventBias;
            switch (burst.EventKind)
            {
                case PaintEventKind.Move: eventBias = 0.2f; break;
                case PaintEventKind.Land: eventBias = 0.55f; break;
                case PaintEventKind.WallStick: eventBias = 0.45f; break;
                case PaintEventKind.WallLaunch: eventBias = 0.65f; break;
                case PaintEventKind.Punch: eventBias = 0.55f; break;
                case PaintEventKind.RagdollImpact: eventBias = 0.9f; break;
                case PaintEventKind.TaskInteract: eventBias = 0.35f; break;
                default: eventBias = 0.25f; break;
            }

            return Mathf.Clamp(Mathf.RoundToInt((speed01 * 2f) + (volume01 * 2f) + (eventBias * 1.5f)), 0, 3);
        }

        private void UploadTextures()
        {
            if (_paintColorTexture == null)
            {
                return;
            }

            _paintColorTexture.SetPixels32(_paintColorPixels);
            _paintWetnessTexture.SetPixels32(_paintWetnessPixels);
            _paintAgeTexture.SetPixels32(_paintAgePixels);

            _paintColorTexture.Apply(false, false);
            _paintWetnessTexture.Apply(false, false);
            _paintAgeTexture.Apply(false, false);

            ApplyTexturesToMaterial();
            _dirty = false;
        }

        private void ApplyTexturesToMaterial()
        {
            if (materialDriver == null)
            {
                return;
            }

            materialDriver.ApplyTextures(_paintColorTexture, _paintWetnessTexture, _paintAgeTexture);
            if (!materialDriver.SupportsPaintTextures)
            {
                materialDriver.ApplyFallbackTint(_latestColor, _fallbackColorBlend);
            }
        }

        private static Texture2D CreateTexture(string textureName, int resolution)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, false);
            texture.name = textureName;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
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
    }
}
