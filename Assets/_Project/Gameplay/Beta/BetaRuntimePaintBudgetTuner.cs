// File: Assets/_Project/Gameplay/Beta/BetaRuntimePaintBudgetTuner.cs
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
        [SerializeField, Min(1)] private int maxEventsPerSecond = 16;
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
            NetworkPlayerPaintEmitter[] emitters = FindObjectsOfType<NetworkPlayerPaintEmitter>();
            for (int i = 0; i < emitters.Length; i++)
            {
                NetworkPlayerPaintEmitter emitter = emitters[i];
                if (emitter == null)
                {
                    continue;
                }

                SetPrivateField(emitter, "maxReplicatedPaintEventsPerSecond", maxEventsPerSecond);
                SetPrivateField(emitter, "radiusClamp", new Vector2(0.05f, 2.15f));
                SetPrivateField(emitter, "intensityClamp", new Vector2(0.06f, 1.35f));
                SetPrivateField(emitter, "stretchClamp", new Vector2(1f, 5.6f));
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
