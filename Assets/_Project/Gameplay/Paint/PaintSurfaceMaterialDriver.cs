// File: Assets/_Project/Gameplay/Paint/PaintSurfaceMaterialDriver.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Paint
{
    [DisallowMultipleComponent]
    public sealed class PaintSurfaceMaterialDriver : MonoBehaviour
    {
        [Header("Renderer")]
        [SerializeField] private Renderer targetRenderer;

        [Header("Material Contract")]
        [SerializeField] private string paintColorTextureProperty = "_PaintColorTex";
        [SerializeField] private string paintWetnessTextureProperty = "_PaintWetnessTex";
        [SerializeField] private string paintAgeTextureProperty = "_PaintAgeTex";
        [SerializeField] private string paintBlendStrengthProperty = "_PaintBlendStrength";
        [SerializeField] private string paintWetGlossProperty = "_PaintWetGloss";
        [SerializeField] private string paintDryRoughnessProperty = "_PaintDryRoughness";
        [SerializeField] private string paintAgeDarkenProperty = "_PaintAgeDarken";
        [SerializeField] private string paintDesaturationProperty = "_PaintDesaturation";
        [SerializeField] private string fallbackMainTextureProperty = "_MainTex";
        [SerializeField] private string fallbackBaseMapProperty = "_BaseMap";

        [Header("Runtime Values")]
        [SerializeField, Range(0f, 1f)] private float paintBlendStrength = 1f;
        [SerializeField, Range(0f, 4f)] private float paintWetGloss = 1.35f;
        [SerializeField, Range(0f, 1f)] private float paintDryRoughness = 0.62f;
        [SerializeField, Range(0f, 1f)] private float paintAgeDarken = 0.18f;
        [SerializeField, Range(0f, 1f)] private float paintDesaturation = 0.22f;

        private MaterialPropertyBlock _block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public Renderer TargetRenderer => targetRenderer;

        public bool SupportsPaintTextures
        {
            get
            {
                if (targetRenderer == null || targetRenderer.sharedMaterial == null)
                {
                    return false;
                }

                Material material = targetRenderer.sharedMaterial;
                return material.HasProperty(paintColorTextureProperty) ||
                       material.HasProperty(paintWetnessTextureProperty) ||
                       material.HasProperty(paintAgeTextureProperty);
            }
        }

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            _block = new MaterialPropertyBlock();
        }

        public void Configure(Renderer renderer)
        {
            if (renderer != null)
            {
                targetRenderer = renderer;
            }

            _block ??= new MaterialPropertyBlock();
        }

        public void ApplyTextures(Texture paintColor, Texture wetness, Texture age)
        {
            if (targetRenderer == null || !SupportsPaintTextures)
            {
                return;
            }

            targetRenderer.GetPropertyBlock(_block);
            Material material = targetRenderer.sharedMaterial;

            if (!string.IsNullOrWhiteSpace(paintColorTextureProperty) && material.HasProperty(paintColorTextureProperty))
            {
                _block.SetTexture(paintColorTextureProperty, paintColor);
            }

            if (!string.IsNullOrWhiteSpace(paintWetnessTextureProperty) && material.HasProperty(paintWetnessTextureProperty))
            {
                _block.SetTexture(paintWetnessTextureProperty, wetness);
            }

            if (!string.IsNullOrWhiteSpace(paintAgeTextureProperty) && material.HasProperty(paintAgeTextureProperty))
            {
                _block.SetTexture(paintAgeTextureProperty, age);
            }

            if (!string.IsNullOrWhiteSpace(paintBlendStrengthProperty) && material.HasProperty(paintBlendStrengthProperty))
            {
                _block.SetFloat(paintBlendStrengthProperty, paintBlendStrength);
            }

            if (!string.IsNullOrWhiteSpace(paintWetGlossProperty) && material.HasProperty(paintWetGlossProperty))
            {
                _block.SetFloat(paintWetGlossProperty, paintWetGloss);
            }

            if (!string.IsNullOrWhiteSpace(paintDryRoughnessProperty) && material.HasProperty(paintDryRoughnessProperty))
            {
                _block.SetFloat(paintDryRoughnessProperty, paintDryRoughness);
            }

            if (!string.IsNullOrWhiteSpace(paintAgeDarkenProperty) && material.HasProperty(paintAgeDarkenProperty))
            {
                _block.SetFloat(paintAgeDarkenProperty, paintAgeDarken);
            }

            if (!string.IsNullOrWhiteSpace(paintDesaturationProperty) && material.HasProperty(paintDesaturationProperty))
            {
                _block.SetFloat(paintDesaturationProperty, paintDesaturation);
            }

            _block.SetColor(BaseColorId, Color.white);
            _block.SetColor(ColorId, Color.white);
            targetRenderer.SetPropertyBlock(_block);
        }

        public void ApplyFallbackTint(Color color, float blend01)
        {
            // Do not tint the whole renderer as a fallback. On Unity cube primitives this makes
            // one contact patch look like paint spread across the entire floor/wall mesh. Surfaces
            // without the explicit paint shader contract are represented by localized decals.
        }
    }
}
