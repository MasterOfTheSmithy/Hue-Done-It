// File: Assets/_Project/Gameplay/Beta/BetaFloodPlayabilityDirector.cs
using System.Reflection;
using HueDoneIt.Flood;
using Unity.Netcode;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Beta flood tuning. The flood loop should create pressure, not immediately make the test map unreadable.
    /// This slows pulses, gives visible warning time, and keeps the hub/spawn region dry at match start.
    /// </summary>
    [DefaultExecutionOrder(-700)]
    [DisallowMultipleComponent]
    public sealed class BetaFloodPlayabilityDirector : MonoBehaviour
    {
        [SerializeField, Min(2f)] private float startupSafeSeconds = 24f;
        [SerializeField, Min(1f)] private float retuneIntervalSeconds = 3f;
        [SerializeField, Min(0f)] private float hubSafeRadius = 10.5f;

        private float _startTime;
        private float _nextTuneTime;

        private void OnEnable()
        {
            _startTime = Time.unscaledTime;
            ApplyTuning();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextTuneTime)
            {
                return;
            }

            _nextTuneTime = Time.unscaledTime + retuneIntervalSeconds;
            ApplyTuning();
        }

        private void ApplyTuning()
        {
            FloodSequenceController[] controllers = FindObjectsByType<FloodSequenceController>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                FloodSequenceController controller = controllers[i];
                if (controller == null)
                {
                    continue;
                }

                SetPrivateFloat(controller, "earlySpeedMultiplier", 0.55f);
                SetPrivateFloat(controller, "midSpeedMultiplier", 0.78f);
                SetPrivateFloat(controller, "lateSpeedMultiplier", 1.05f);
                SetPrivateFloat(controller, "earlyPulseCadenceSeconds", 72f);
                SetPrivateFloat(controller, "midPulseCadenceSeconds", 48f);
                SetPrivateFloat(controller, "latePulseCadenceSeconds", 32f);
                SetPrivateFloat(controller, "pulseTelegraphSeconds", 11f);
                SetPrivateFloat(controller, "pulseFloodDurationSeconds", 5f);
                SetPrivateFloat(controller, "pulseSubmergeDurationSeconds", 2.25f);
                SetPrivateFloat(controller, "lockedFloodingDelaySeconds", 7f);
                SetPrivateFloat(controller, "lockedSubmergeDelaySeconds", 10f);
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsServer && Time.unscaledTime - _startTime <= startupSafeSeconds)
            {
                KeepHubAndSpawnZonesDry();
            }
        }

        private void KeepHubAndSpawnZonesDry()
        {
            FloodZone[] zones = FindObjectsByType<FloodZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                FloodZone zone = zones[i];
                if (zone == null)
                {
                    continue;
                }

                Vector3 position = zone.transform.position;
                bool nearHub = new Vector2(position.x, position.z).magnitude <= hubSafeRadius;
                bool namedSafe = zone.name.ToLowerInvariant().Contains("hub") ||
                                 zone.name.ToLowerInvariant().Contains("spawn") ||
                                 zone.ZoneId.ToLowerInvariant().Contains("hub") ||
                                 zone.ZoneId.ToLowerInvariant().Contains("spawn");

                if (nearHub || namedSafe)
                {
                    zone.TrySetState(FloodZoneState.Dry);
                }
            }
        }

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
            }
        }
    }
}
