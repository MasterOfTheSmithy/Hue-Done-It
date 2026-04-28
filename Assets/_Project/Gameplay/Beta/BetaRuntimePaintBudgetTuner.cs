// File: Assets/_Project/Gameplay/Beta/BetaRuntimePaintBudgetTuner.cs
using HueDoneIt.Gameplay.Paint;
using HueDoneIt.Gameplay.Players;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Runtime paint budget guard. It keeps the beta playable when several players/CPUs are producing splats.
    /// This does not replace the real fluid project; it prevents current splat events from becoming a lag source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BetaRuntimePaintBudgetTuner : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxEventsPerSecond = 8;
        [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 2f;

        private float _nextRefreshTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            ApplyBudgets();
        }

        private void ApplyBudgets()
        {
            NetworkPlayerPaintEmitter[] emitters = FindObjectsByType<NetworkPlayerPaintEmitter>(FindObjectsSortMode.None);
            for (int i = 0; i < emitters.Length; i++)
            {
                NetworkPlayerPaintEmitter emitter = emitters[i];
                if (emitter == null)
                {
                    continue;
                }

                SetPrivateField(emitter, "maxReplicatedPaintEventsPerSecond", maxEventsPerSecond);
                SetPrivateField(emitter, "radiusClamp", new Vector2(0.05f, 1.65f));
                SetPrivateField(emitter, "intensityClamp", new Vector2(0.06f, 1.15f));
                SetPrivateField(emitter, "stretchClamp", new Vector2(1f, 3.25f));
                SetPrivateField(emitter, "fallbackLifetimeSeconds", 1.75f);
            }

            StainReceiver[] receivers = FindObjectsByType<StainReceiver>(FindObjectsSortMode.None);
            for (int i = 0; i < receivers.Length; i++)
            {
                StainReceiver receiver = receivers[i];
                if (receiver == null)
                {
                    continue;
                }

                SetPrivateField(receiver, "maxSplatDecals", 32);
                SetPrivateField(receiver, "maxPermanentEvidenceMarks", 12);
                SetPrivateField(receiver, "prewarmSplatDecals", 0);
                SetPrivateField(receiver, "enableWholeSurfaceTint", false);
                SetPrivateField(receiver, "generatedPatternCount", 8);
                SetPrivateField(receiver, "temporaryLifetimeSeconds", 3.5f);
                SetPrivateField(receiver, "heavyTemporaryLifetimeSeconds", 6f);
                SetPrivateField(receiver, "forceToDensityMultiplier", 0.045f);
                SetPrivateField(receiver, "activityToDensityMultiplier", 0.035f);
                SetPrivateField(receiver, "movementWearThreshold", 10f);
                SetPrivateField(receiver, "alphaRange", new Vector2(0.22f, 0.72f));
                SetPrivateField(receiver, "stretchClamp", new Vector2(1f, 3.1f));
                SetPrivateField(receiver, "maxLocalizedDecalMajorAxis", 2.8f);
                SetPrivateField(receiver, "maxLocalizedDecalMinorAxis", 1.45f);
                SetPrivateField(receiver, "maxHeavyImpactMajorAxis", 3.6f);
                SetPrivateField(receiver, "maxHeavyImpactMinorAxis", 1.75f);
            }
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}
