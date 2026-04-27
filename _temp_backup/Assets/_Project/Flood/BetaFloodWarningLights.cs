// File: Assets/_Project/Flood/BetaFloodWarningLights.cs
using System.Collections.Generic;
using UnityEngine;

namespace HueDoneIt.Flood
{
    [DisallowMultipleComponent]
    public sealed class BetaFloodWarningLights : MonoBehaviour
    {
        [SerializeField] private FloodSequenceController controller;
        [SerializeField] private FloodZone[] observedZones;
        [SerializeField] private Light[] warningLights;
        [SerializeField] private Renderer[] warningRenderers;
        [SerializeField, Min(0.1f)] private float telegraphPulseSpeed = 7.5f;
        [SerializeField, Min(0.1f)] private float dangerPulseSpeed = 13f;
        [SerializeField, Min(0f)] private float idleIntensity = 0.08f;
        [SerializeField, Min(0f)] private float telegraphIntensity = 3.25f;
        [SerializeField, Min(0f)] private float dangerIntensity = 5.5f;
        [SerializeField] private Color idleColor = new(0.1f, 0.22f, 0.32f, 1f);
        [SerializeField] private Color telegraphColor = new(1f, 0.72f, 0.12f, 1f);
        [SerializeField] private Color dangerColor = new(1f, 0.08f, 0.04f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _propertyBlock;
        private float _nextReferenceResolveTime;

        private void Awake()
        {
            EnsurePropertyBlock();
            ResolveReferences(force: true);
        }

        private void OnEnable()
        {
            EnsurePropertyBlock();
            ResolveReferences(force: true);
        }

        private void Update()
        {
            ResolveReferences(force: false);

            bool danger = HasZoneState(FloodZoneState.Flooding) ||
                          HasZoneState(FloodZoneState.Submerged) ||
                          (controller != null && controller.IsPulseActive);

            bool telegraph = !danger &&
                             (HasZoneState(FloodZoneState.Wet) ||
                              (controller != null && controller.IsPulseTelegraphActive));

            float pulseSpeed = danger ? dangerPulseSpeed : telegraphPulseSpeed;
            float pulse01 = 0.5f + (Mathf.Sin(Time.time * pulseSpeed) * 0.5f);
            float intensity = danger
                ? Mathf.Lerp(dangerIntensity * 0.28f, dangerIntensity, pulse01)
                : telegraph
                    ? Mathf.Lerp(telegraphIntensity * 0.25f, telegraphIntensity, pulse01)
                    : idleIntensity;
            Color color = danger ? dangerColor : telegraph ? telegraphColor : idleColor;

            UpdateLights(color, intensity, danger);
            UpdateRenderers(color, intensity, danger || telegraph);
        }

        private void UpdateLights(Color color, float intensity, bool danger)
        {
            if (warningLights == null)
            {
                return;
            }

            for (int i = 0; i < warningLights.Length; i++)
            {
                Light lightRef = warningLights[i];
                if (lightRef == null)
                {
                    continue;
                }

                lightRef.enabled = true;
                lightRef.color = color;
                lightRef.intensity = intensity;
                lightRef.range = danger ? 8f : 5.5f;
            }
        }

        private void UpdateRenderers(Color color, float intensity, bool activeWarning)
        {
            if (warningRenderers == null)
            {
                return;
            }

            EnsurePropertyBlock();
            Color materialColor = color;
            materialColor.a = activeWarning ? 1f : 0.45f;
            Color emission = color * Mathf.Max(0.1f, intensity * 0.9f);

            for (int i = 0; i < warningRenderers.Length; i++)
            {
                Renderer rendererRef = warningRenderers[i];
                if (rendererRef == null)
                {
                    continue;
                }

                // Do not call GetPropertyBlock here. The previous patch mixed two MPB fields and still
                // allowed a null destination into Renderer.GetPropertyBlock, which throws every frame.
                // This warning beacon owns its tiny visual state, so writing a cleared block is safer.
                _propertyBlock.Clear();
                _propertyBlock.SetColor(BaseColorId, materialColor);
                _propertyBlock.SetColor(ColorId, materialColor);
                _propertyBlock.SetColor(EmissionColorId, emission);
                rendererRef.SetPropertyBlock(_propertyBlock);
            }
        }

        private bool HasZoneState(FloodZoneState state)
        {
            if (observedZones == null)
            {
                return false;
            }

            for (int i = 0; i < observedZones.Length; i++)
            {
                FloodZone zone = observedZones[i];
                if (zone != null && zone.CurrentState == state)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveReferences(bool force)
        {
            if (!force && Time.unscaledTime < _nextReferenceResolveTime)
            {
                return;
            }

            _nextReferenceResolveTime = Time.unscaledTime + 1f;

            if (controller == null)
            {
                controller = FindFirstObjectByType<FloodSequenceController>();
            }

            if (observedZones == null || observedZones.Length == 0)
            {
                observedZones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            }

            if (warningLights == null || warningLights.Length == 0)
            {
                warningLights = GetComponentsInChildren<Light>(true);
            }

            if (warningRenderers == null || warningRenderers.Length == 0)
            {
                List<Renderer> renderers = new();
                Renderer[] all = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null)
                    {
                        renderers.Add(all[i]);
                    }
                }

                warningRenderers = renderers.ToArray();
            }
        }

        private void EnsurePropertyBlock()
        {
            _propertyBlock ??= new MaterialPropertyBlock();
        }
    }
}
