// File: Assets/_Project/Flood/FloodZoneVisualPresenter.cs
using UnityEngine;

namespace HueDoneIt.Flood
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FloodZone))]
    public sealed class FloodZoneVisualPresenter : MonoBehaviour
    {
        [SerializeField] private Color dryColor = new(0.10f, 0.25f, 0.35f, 0.06f);
        [SerializeField] private Color wetColor = new(0.10f, 0.45f, 0.85f, 0.18f);
        [SerializeField] private Color floodingColor = new(0.15f, 0.62f, 1f, 0.34f);
        [SerializeField] private Color submergedColor = new(0.08f, 0.58f, 0.92f, 0.46f);
        [SerializeField] private Color sealedSafeColor = new(0.20f, 0.90f, 0.60f, 0.16f);

        private FloodZone _zone;
        private Transform _waterVisual;
        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            _zone = GetComponent<FloodZone>();
            EnsureVisual();
        }

        private void Update()
        {
            if (_zone == null)
            {
                _zone = GetComponent<FloodZone>();
                if (_zone == null)
                {
                    return;
                }
            }

            EnsureVisual();
            float level = Mathf.Clamp01(_zone.WaterLevel01);
            _waterVisual.localPosition = new Vector3(0f, Mathf.Lerp(-0.48f, 0.48f, level), 0f);
            _waterVisual.localScale = new Vector3(0.96f, Mathf.Max(0.04f, level), 0.96f);

            Color color = _zone.CurrentState switch
            {
                FloodZoneState.Wet => wetColor,
                FloodZoneState.Flooding => floodingColor,
                FloodZoneState.Submerged => submergedColor,
                FloodZoneState.SealedSafe => sealedSafeColor,
                _ => dryColor
            };

            float pulse = _zone.CurrentState is FloodZoneState.Flooding or FloodZoneState.Submerged
                ? 0.08f + (Mathf.Sin(Time.time * 4.5f) * 0.04f)
                : 0f;
            color.a = Mathf.Clamp01(color.a + pulse);

            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetColor("_Color", color);
            _block.SetColor("_BaseColor", color);
            _renderer.SetPropertyBlock(_block);
            _waterVisual.gameObject.SetActive(level > 0.01f || _zone.CurrentState != FloodZoneState.Dry);
        }

        private void EnsureVisual()
        {
            if (_waterVisual != null)
            {
                return;
            }

            GameObject waterObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            waterObject.name = "WaterVisual";
            waterObject.transform.SetParent(transform, false);
            waterObject.transform.localRotation = Quaternion.identity;
            waterObject.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            waterObject.transform.localScale = new Vector3(0.96f, 0.05f, 0.96f);
            Collider colliderRef = waterObject.GetComponent<Collider>();
            if (colliderRef != null)
            {
                Destroy(colliderRef);
            }

            _waterVisual = waterObject.transform;
            _renderer = waterObject.GetComponent<Renderer>();
            if (_renderer != null)
            {
                Material material = new Material(Shader.Find("Standard"));
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
                _renderer.sharedMaterial = material;
            }
        }
    }
}
