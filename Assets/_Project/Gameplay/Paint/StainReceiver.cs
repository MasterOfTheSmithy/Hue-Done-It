using System.Collections.Generic;
using UnityEngine;

namespace HueDoneIt.Gameplay.Paint
{
    [DisallowMultipleComponent]
    public sealed class StainReceiver : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField, Min(0f)] private float stainBlendPerHit = 0.08f;
        [SerializeField, Min(0f)] private float decayPerSecond = 0.015f;
        [SerializeField, Min(1)] private int maxSplatDecals = 48;

        private readonly List<GameObject> _decals = new();
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseColor;
        private Color _stainColor;
        private bool _hasBaseColor;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private bool _useBaseColorProperty = true;

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            CacheBaseColor();
        }

        private void Update()
        {
            if (!_hasBaseColor)
            {
                CacheBaseColor();
            }

            if (!_hasBaseColor)
            {
                return;
            }

            _stainColor = Color.Lerp(_stainColor, _baseColor, decayPerSecond * Time.deltaTime);
            ApplyTint(_stainColor);
        }

        public void ApplyStain(Color color, Vector3 worldPosition, Vector3 worldNormal, float radius, float intensity, bool wet)
        {
            if (!_hasBaseColor)
            {
                CacheBaseColor();
            }

            float blend = Mathf.Clamp01(stainBlendPerHit * Mathf.Max(0.25f, intensity));
            if (wet)
            {
                blend *= 0.75f;
            }

            _stainColor = Color.Lerp(_hasBaseColor ? _stainColor : color, color, blend);
            ApplyTint(_stainColor);
            SpawnDecal(color, worldPosition, worldNormal, radius, wet);
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

        private void SpawnDecal(Color color, Vector3 position, Vector3 normal, float radius, bool wet)
        {
            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decal.name = "StainDecal";
            decal.transform.SetParent(transform, true);
            decal.transform.position = position + (normal * 0.01f);
            decal.transform.rotation = Quaternion.LookRotation(normal);
            float scale = Mathf.Clamp(radius, 0.08f, 2.2f);
            decal.transform.localScale = Vector3.one * scale;

            Collider decalCollider = decal.GetComponent<Collider>();
            if (decalCollider != null)
            {
                Destroy(decalCollider);
            }

            Renderer renderer = decal.GetComponent<Renderer>();
            Material material = new(Shader.Find("Universal Render Pipeline/Unlit"));
            Color tinted = color;
            tinted.a = wet ? 0.28f : 0.42f;
            material.color = tinted;
            material.SetFloat("_Surface", 1f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _decals.Add(decal);
            if (_decals.Count > maxSplatDecals)
            {
                GameObject oldest = _decals[0];
                _decals.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest);
                }
            }
        }
    }
}
