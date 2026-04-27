// File: Assets/_Project/Gameplay/Beta/BetaWaterColorDiffusion.cs
using System.Collections.Generic;
using HueDoneIt.Flood;
using HueDoneIt.Flood.Integration;
using HueDoneIt.Gameplay.Elimination;
using HueDoneIt.Gameplay.Players;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    [DefaultExecutionOrder(1100)]
    public sealed class BetaWaterColorDiffusion : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float scanIntervalSeconds = 0.12f;
        [SerializeField, Min(0f)] private float livingDiffusionPerSecond = 0.18f;
        [SerializeField, Min(0f)] private float deathStainPerSecond = 0.85f;
        [SerializeField, Min(0f)] private float dryDecayPerSecond = 0.08f;
        [SerializeField] private Color cleanWaterColor = new(0.08f, 0.58f, 0.92f, 0.38f);

        private readonly Dictionary<FloodZone, ZoneTint> _zoneTints = new();
        private MaterialPropertyBlock _block;
        private float _nextScanTime;

        private sealed class ZoneTint
        {
            public Color Color = Color.clear;
            public float Strength;
        }

        private void Update()
        {
            _block ??= new MaterialPropertyBlock();

            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + scanIntervalSeconds;
                AccumulatePlayerColor(scanIntervalSeconds);
            }

            ApplyWaterColors();
        }

        private void AccumulatePlayerColor(float deltaTime)
        {
            PlayerFloodZoneTracker[] trackers = FindObjectsByType<PlayerFloodZoneTracker>(FindObjectsSortMode.None);
            for (int i = 0; i < trackers.Length; i++)
            {
                PlayerFloodZoneTracker tracker = trackers[i];
                if (tracker == null || tracker.CurrentZone == null || tracker.CurrentWaterLevel01 <= 0.03f)
                {
                    continue;
                }

                if (!tracker.TryGetComponent(out PlayerColorProfile colorProfile))
                {
                    continue;
                }

                Color playerColor = colorProfile.PlayerColor;
                float rate = livingDiffusionPerSecond * Mathf.Lerp(0.35f, 1f, tracker.Saturation01);

                if (tracker.TryGetComponent(out PlayerLifeState lifeState) && !lifeState.IsAlive)
                {
                    rate = deathStainPerSecond;
                }

                ZoneTint tint = GetTint(tracker.CurrentZone);
                float add = rate * deltaTime;
                tint.Color = tint.Strength <= 0.001f ? playerColor : Color.Lerp(tint.Color, playerColor, Mathf.Clamp01(add));
                tint.Strength = Mathf.Clamp01(tint.Strength + add);
            }

            FloodZone[] zones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                FloodZone zone = zones[i];
                if (zone == null || !_zoneTints.TryGetValue(zone, out ZoneTint tint))
                {
                    continue;
                }

                if (zone.WaterLevel01 <= 0.02f || zone.CurrentState == FloodZoneState.Dry)
                {
                    tint.Strength = Mathf.MoveTowards(tint.Strength, 0f, dryDecayPerSecond * deltaTime);
                }
            }
        }

        private ZoneTint GetTint(FloodZone zone)
        {
            if (!_zoneTints.TryGetValue(zone, out ZoneTint tint))
            {
                tint = new ZoneTint();
                _zoneTints.Add(zone, tint);
            }

            return tint;
        }

        private void ApplyWaterColors()
        {
            foreach (KeyValuePair<FloodZone, ZoneTint> pair in _zoneTints)
            {
                FloodZone zone = pair.Key;
                ZoneTint tint = pair.Value;
                if (zone == null)
                {
                    continue;
                }

                Renderer waterRenderer = ResolveWaterRenderer(zone);
                if (waterRenderer == null)
                {
                    continue;
                }

                Color waterColor = cleanWaterColor;
                waterColor.a = Mathf.Lerp(0.14f, 0.52f, Mathf.Clamp01(zone.WaterLevel01));

                if (tint.Strength > 0.001f)
                {
                    Color stained = Color.Lerp(waterColor, tint.Color, Mathf.Clamp01(tint.Strength));
                    stained.a = Mathf.Clamp01(waterColor.a + (tint.Strength * 0.18f));
                    waterColor = stained;
                }

                _block ??= new MaterialPropertyBlock();
                waterRenderer.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", waterColor);
                _block.SetColor("_Color", waterColor);
                _block.SetColor("_EmissionColor", waterColor * Mathf.Lerp(0.15f, 0.45f, tint.Strength));
                waterRenderer.SetPropertyBlock(_block);
            }
        }

        private static Renderer ResolveWaterRenderer(FloodZone zone)
        {
            Transform zoneTransform = zone.transform;
            Transform water = zoneTransform.Find("WaterVisual");
            if (water != null && water.TryGetComponent(out Renderer renderer))
            {
                return renderer;
            }

            Renderer[] renderers = zone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer candidate = renderers[i];
                if (candidate != null && candidate.name.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
