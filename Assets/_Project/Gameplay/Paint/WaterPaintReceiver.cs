// File: Assets/_Project/Gameplay/Paint/WaterPaintReceiver.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Paint
{
    [DisallowMultipleComponent]
    public sealed class WaterPaintReceiver : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Collider targetCollider;
        [SerializeField, Min(0.01f)] private float diffusionDecayPerSecond = 0.1f;
        [SerializeField, Range(0f, 1f)] private float maxTintBlend = 0.45f;
        [SerializeField] private Color baseTint = new Color(0.12f, 0.35f, 0.85f, 0.35f);

        private MaterialPropertyBlock _block;
        private Color _currentTint;
        private float _currentBlend;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public Bounds WorldBounds
        {
            get
            {
                if (targetCollider != null)
                {
                    return targetCollider.bounds;
                }

                if (targetRenderer != null)
                {
                    return targetRenderer.bounds;
                }

                return new Bounds(transform.position, Vector3.one);
            }
        }

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            if (targetCollider == null)
            {
                targetCollider = GetComponent<Collider>();
            }

            _block = new MaterialPropertyBlock();
            _currentTint = baseTint;
            ApplyTint();
            PaintSurfaceRegistry.Register(this);
        }

        private void OnEnable()
        {
            PaintSurfaceRegistry.Register(this);
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
            if (_currentBlend <= 0f)
            {
                return;
            }

            _currentBlend = Mathf.Max(0f, _currentBlend - (diffusionDecayPerSecond * Time.deltaTime));
            _currentTint = Color.Lerp(baseTint, _currentTint, _currentBlend);
            ApplyTint();
        }

        public void Configure(Renderer renderer, Collider colliderRef)
        {
            if (renderer != null)
            {
                targetRenderer = renderer;
            }

            if (colliderRef != null)
            {
                targetCollider = colliderRef;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }
        }

        public bool CanAffect(PaintBurstCommand burst)
        {
            Bounds bounds = WorldBounds;
            bounds.Expand(burst.Radius * 2f);
            return bounds.Contains(burst.Position);
        }

        public void InjectPaint(PaintBurstCommand burst)
        {
            Color incoming = burst.Color;
            incoming.a = 1f;
            float blend = Mathf.Clamp01(Mathf.Lerp(0.08f, 0.28f, burst.Volume));
            _currentTint = Color.Lerp(_currentTint, incoming, blend);
            _currentBlend = Mathf.Clamp(_currentBlend + blend, 0f, maxTintBlend);
            ApplyTint();
        }

        private void ApplyTint()
        {
            if (targetRenderer == null)
            {
                return;
            }

            Color renderColor = Color.Lerp(baseTint, _currentTint, _currentBlend);
            renderColor.a = Mathf.Lerp(baseTint.a, Mathf.Max(baseTint.a, 0.65f), _currentBlend);
            targetRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, renderColor);
            _block.SetColor(ColorId, renderColor);
            targetRenderer.SetPropertyBlock(_block);
        }
    }
}
