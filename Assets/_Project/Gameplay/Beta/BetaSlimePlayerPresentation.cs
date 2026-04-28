// File: Assets/_Project/Gameplay/Beta/BetaSlimePlayerPresentation.cs
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Contextual slime body presentation. It creates a non-colliding visual blob shell that squashes,
    /// stretches, wobbles, and puddles based on the existing authoritative movement state.
    /// </summary>
    [DefaultExecutionOrder(620)]
    [DisallowMultipleComponent]
    public sealed class BetaSlimePlayerPresentation : MonoBehaviour
    {
        private const string VisualRootName = "__BetaSlimePresentation";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Blob Shape")]
        [SerializeField] private Vector3 baseBlobScale = new(0.88f, 0.78f, 0.88f);
        [SerializeField, Min(0f)] private float speedStretch = 0.028f;
        [SerializeField, Min(0f)] private float airborneStretch = 0.18f;
        [SerializeField, Min(0f)] private float wallFlatten = 0.22f;
        [SerializeField, Min(0f)] private float ragdollWobble = 0.34f;
        [SerializeField, Min(0.1f)] private float scaleLerpSpeed = 14f;
        [SerializeField, Min(0.1f)] private float rotationLerpSpeed = 10f;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float velocityLag = 0.035f;
        [SerializeField, Min(0f)] private float idlePulseAmount = 0.035f;
        [SerializeField, Min(0f)] private float landingSquashStrength = 0.11f;
        [SerializeField, Min(0f)] private float accelerationSquash = 0.16f;
        [SerializeField, Min(0f)] private float turnLeanAmount = 10f;
        [SerializeField, Min(0f)] private float airDriftOffset = 0.08f;
        [SerializeField, Min(0f)] private float wetDesaturation = 0.36f;

        private NetworkPlayerAuthoritativeMover _mover;
        private PlayerColorProfile _colorProfile;
        private PlayerFloodZoneTracker _floodTracker;
        private PlayerLifeState _lifeState;

        private Transform _visualRoot;
        private Renderer _blobRenderer;
        private Renderer _shadowRenderer;
        private Material _blobMaterial;
        private Material _shadowMaterial;
        private MaterialPropertyBlock _block;

        private Vector3 _currentScale = Vector3.one;
        private Vector3 _visualVelocity;
        private Vector3 _previousVelocity;
        private float _landingWobble;
        private float _lastLandingImpact;

        private void Awake()
        {
            _mover = GetComponent<NetworkPlayerAuthoritativeMover>();
            _colorProfile = GetComponent<PlayerColorProfile>();
            _floodTracker = GetComponent<PlayerFloodZoneTracker>();
            _lifeState = GetComponent<PlayerLifeState>();
            _block = new MaterialPropertyBlock();
            CreateVisualRig();
            HideLegacyStaticModel();
        }

        private void OnDestroy()
        {
            if (_visualRoot != null)
            {
                Destroy(_visualRoot.gameObject);
            }
        }

        private void LateUpdate()
        {
            if (_visualRoot == null || _blobRenderer == null)
            {
                CreateVisualRig();
            }

            HideLegacyStaticModel();
            TickPresentation(Time.deltaTime);
        }

        private void CreateVisualRig()
        {
            Transform existing = transform.Find(VisualRootName);
            if (existing != null)
            {
                _visualRoot = existing;
                _blobRenderer = _visualRoot.GetComponentInChildren<Renderer>();
                return;
            }

            GameObject root = new GameObject(VisualRootName);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, 0.54f, 0f);
            root.transform.localRotation = Quaternion.identity;
            _visualRoot = root.transform;

            GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blob.name = "JellyBody";
            blob.transform.SetParent(_visualRoot, false);
            blob.transform.localPosition = Vector3.zero;
            blob.transform.localRotation = Quaternion.identity;
            blob.transform.localScale = baseBlobScale;
            DestroyCollider(blob);

            _blobRenderer = blob.GetComponent<Renderer>();
            _blobMaterial = CreateMaterial("HDI Beta Slime Body", Color.white, 1.0f);
            if (_blobRenderer != null)
            {
                _blobRenderer.sharedMaterial = _blobMaterial;
            }

            GameObject shine = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shine.name = "JellyHighlight";
            shine.transform.SetParent(_visualRoot, false);
            shine.transform.localPosition = new Vector3(-0.18f, 0.22f, -0.25f);
            shine.transform.localScale = new Vector3(0.18f, 0.08f, 0.12f);
            DestroyCollider(shine);

            Renderer shineRenderer = shine.GetComponent<Renderer>();
            Material shineMaterial = CreateMaterial("HDI Beta Slime Highlight", new Color(1f, 1f, 1f, 0.85f), 0.85f);
            if (shineRenderer != null)
            {
                shineRenderer.sharedMaterial = shineMaterial;
            }

            GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadow.name = "SquishShadow";
            shadow.transform.SetParent(_visualRoot, false);
            shadow.transform.localPosition = new Vector3(0f, -0.50f, 0f);
            shadow.transform.localScale = new Vector3(0.82f, 0.012f, 0.82f);
            DestroyCollider(shadow);

            _shadowRenderer = shadow.GetComponent<Renderer>();
            _shadowMaterial = CreateMaterial("HDI Beta Slime Contact Shadow", new Color(0f, 0f, 0f, 0.32f), 0.32f);
            if (_shadowRenderer != null)
            {
                _shadowRenderer.sharedMaterial = _shadowMaterial;
            }

            _currentScale = baseBlobScale;
        }

        private void TickPresentation(float deltaTime)
        {
            if (_mover == null)
            {
                return;
            }

            Vector3 velocity = _mover.CurrentVelocity;
            Vector3 planarVelocity = velocity;
            planarVelocity.y = 0f;
            float speed = planarVelocity.magnitude;
            Vector3 acceleration = deltaTime > 0.0001f ? (velocity - _previousVelocity) / deltaTime : Vector3.zero;
            float acceleration01 = Mathf.Clamp01(acceleration.magnitude / 16f);
            bool alive = _lifeState == null || _lifeState.IsAlive;
            float saturation = _floodTracker != null ? _floodTracker.Saturation01 : 0f;

            float landingImpact = _mover.LastLandingImpact;
            if (landingImpact > _lastLandingImpact + 0.05f)
            {
                _landingWobble = Mathf.Clamp01(landingImpact / 14f);
            }
            _lastLandingImpact = landingImpact;
            _landingWobble = Mathf.MoveTowards(_landingWobble, 0f, deltaTime * 2.2f);

            float idlePulse = 1f + Mathf.Sin(Time.time * 3.2f) * idlePulseAmount;
            float stretch = Mathf.Clamp(speed * speedStretch, 0f, 0.42f);
            float accelerationSquash01 = acceleration01 * accelerationSquash;
            Vector3 targetScale = baseBlobScale;

            switch (_mover.CurrentState)
            {
                case NetworkPlayerAuthoritativeMover.LocomotionState.Grounded:
                    targetScale = new Vector3(
                        baseBlobScale.x * (idlePulse + stretch * 0.35f + _landingWobble * landingSquashStrength + accelerationSquash01 * 0.45f),
                        baseBlobScale.y * (1f - stretch * 0.22f - _landingWobble * landingSquashStrength - accelerationSquash01 * 0.85f),
                        baseBlobScale.z * (idlePulse + stretch * 0.35f + _landingWobble * landingSquashStrength + accelerationSquash01 * 0.45f));
                    break;

                case NetworkPlayerAuthoritativeMover.LocomotionState.Airborne:
                case NetworkPlayerAuthoritativeMover.LocomotionState.LowGravityFloat:
                    targetScale = new Vector3(
                        baseBlobScale.x * Mathf.Max(0.66f, 1f - airborneStretch - stretch * 0.18f - accelerationSquash01 * 0.2f),
                        baseBlobScale.y * (1f + airborneStretch + stretch + accelerationSquash01 * 0.4f),
                        baseBlobScale.z * Mathf.Max(0.66f, 1f - airborneStretch - stretch * 0.18f - accelerationSquash01 * 0.2f));
                    break;

                case NetworkPlayerAuthoritativeMover.LocomotionState.WallSlide:
                case NetworkPlayerAuthoritativeMover.LocomotionState.WallStick:
                case NetworkPlayerAuthoritativeMover.LocomotionState.WallLaunch:
                    targetScale = new Vector3(
                        baseBlobScale.x * (1f + wallFlatten + _mover.WallCompression * 0.38f),
                        baseBlobScale.y * (1f - wallFlatten * 0.35f - accelerationSquash01 * 0.15f),
                        baseBlobScale.z * Mathf.Max(0.48f, 1f - wallFlatten - _mover.WallCompression * 0.2f));
                    break;

                case NetworkPlayerAuthoritativeMover.LocomotionState.Knockback:
                case NetworkPlayerAuthoritativeMover.LocomotionState.Ragdoll:
                    float wobble = Mathf.Sin(Time.time * 14f) * ragdollWobble;
                    targetScale = new Vector3(
                        baseBlobScale.x * (1f + Mathf.Abs(wobble) * 0.35f + stretch * 0.35f),
                        baseBlobScale.y * (0.82f + Mathf.Abs(wobble) * 0.18f),
                        baseBlobScale.z * (1f - wobble * 0.25f));
                    break;
            }

            if (!alive)
            {
                targetScale = new Vector3(baseBlobScale.x * 1.85f, baseBlobScale.y * 0.18f, baseBlobScale.z * 1.85f);
            }

            float wetShrink = saturation * 0.12f;
            targetScale.y *= Mathf.Max(0.58f, 1f - wetShrink);
            targetScale.x *= 1f + saturation * 0.08f;
            targetScale.z *= 1f + saturation * 0.08f;

            _currentScale = Vector3.Lerp(_currentScale, targetScale, 1f - Mathf.Exp(-scaleLerpSpeed * deltaTime));
            _visualVelocity = Vector3.Lerp(_visualVelocity, velocity, 1f - Mathf.Exp(-9f * deltaTime));
            _visualRoot.localScale = _currentScale;

            float airOffset = _mover.CurrentState == NetworkPlayerAuthoritativeMover.LocomotionState.Airborne ||
                              _mover.CurrentState == NetworkPlayerAuthoritativeMover.LocomotionState.LowGravityFloat
                ? airDriftOffset
                : 0f;

            _visualRoot.localPosition = new Vector3(
                Mathf.Clamp(-_visualVelocity.x * (velocityLag + airOffset), -0.26f, 0.26f),
                0.54f + Mathf.Sin(Time.time * 4.4f) * 0.018f + Mathf.Clamp(-velocity.y * 0.01f, -0.08f, 0.08f),
                Mathf.Clamp(-_visualVelocity.z * (velocityLag + airOffset), -0.26f, 0.26f));

            Quaternion targetRotation = Quaternion.identity;
            Vector3 planar = planarVelocity;
            if (planar.sqrMagnitude > 0.25f)
            {
                targetRotation = Quaternion.LookRotation(planar.normalized, Vector3.up);
            }

            float lateralLean = Mathf.Clamp(Vector3.Dot(planar.normalized, transform.right), -1f, 1f);
            float forwardLean = Mathf.Clamp(Vector3.Dot(planar.normalized, transform.forward), -1f, 1f);
            if (planar.sqrMagnitude > 0.04f)
            {
                targetRotation *= Quaternion.Euler(-forwardLean * turnLeanAmount * Mathf.Clamp01(speed / 8f), 0f, lateralLean * -turnLeanAmount);
            }

            if (_mover.CurrentState == NetworkPlayerAuthoritativeMover.LocomotionState.Ragdoll ||
                _mover.CurrentState == NetworkPlayerAuthoritativeMover.LocomotionState.Knockback)
            {
                targetRotation *= Quaternion.Euler(Mathf.Sin(Time.time * 12f) * 12f, 0f, Mathf.Cos(Time.time * 10f) * 18f);
            }

            _visualRoot.rotation = Quaternion.Slerp(_visualRoot.rotation, targetRotation, 1f - Mathf.Exp(-rotationLerpSpeed * deltaTime));
            _previousVelocity = velocity;
            ApplyColors(saturation, alive);
        }

        private void HideLegacyStaticModel()
        {
            Renderer[] renderers = GetComponents<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = false;
            }

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = null;
            }
        }

        private void ApplyColors(float saturation, bool alive)
        {
            if (_blobRenderer == null)
            {
                return;
            }

            Color baseColor = _colorProfile != null ? _colorProfile.PlayerColor : Color.white;
            Color wetColor = Color.Lerp(baseColor, new Color(0.72f, 0.84f, 0.96f, 1f), saturation * wetDesaturation);
            if (!alive)
            {
                wetColor = Color.Lerp(wetColor, new Color(0.25f, 0.25f, 0.28f, 1f), 0.45f);
            }

            _block ??= new MaterialPropertyBlock();
            _blobRenderer.GetPropertyBlock(_block);
            if (_blobRenderer.sharedMaterial != null && _blobRenderer.sharedMaterial.HasProperty(BaseColorId))
            {
                _block.SetColor(BaseColorId, wetColor);
            }
            if (_blobRenderer.sharedMaterial != null && _blobRenderer.sharedMaterial.HasProperty(ColorId))
            {
                _block.SetColor(ColorId, wetColor);
            }
            if (_blobRenderer.sharedMaterial != null && _blobRenderer.sharedMaterial.HasProperty(EmissionColorId))
            {
                _block.SetColor(EmissionColorId, wetColor * (0.25f + saturation * 0.55f));
            }
            _blobRenderer.SetPropertyBlock(_block);

            if (_shadowRenderer != null)
            {
                _shadowRenderer.transform.localScale = new Vector3(
                    Mathf.Lerp(0.72f, 1.2f, Mathf.Clamp01(_currentScale.x)),
                    0.012f,
                    Mathf.Lerp(0.72f, 1.2f, Mathf.Clamp01(_currentScale.z)));
            }
        }

        private static Material CreateMaterial(string name, Color color, float alpha)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = name;
            color.a = alpha;
            material.color = color;
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }
            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }
            if (material.HasProperty(EmissionColorId))
            {
                material.SetColor(EmissionColorId, color * 0.25f);
            }
            return material;
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
