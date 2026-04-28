// File: Assets/_Project/Gameplay/Beta/BetaSlimeMovementFeelTuner.cs
using System.Reflection;
using HueDoneIt.Gameplay.Players;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Beta movement tuning layer. It adjusts the existing authoritative mover's serialized feel values at runtime
    /// instead of replacing movement architecture. This keeps Netcode authority intact while making the blob feel
    /// more responsive, bouncy, and wall-play friendly for friend tests.
    /// </summary>
    [DefaultExecutionOrder(-120)]
    [DisallowMultipleComponent]
    public sealed class BetaSlimeMovementFeelTuner : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float refreshIntervalSeconds = 2.0f;

        private static readonly BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private float _nextRefreshTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            ApplyToAllMovers();
        }

        private static void ApplyToAllMovers()
        {
            NetworkPlayerAuthoritativeMover[] movers = FindObjectsByType<NetworkPlayerAuthoritativeMover>(FindObjectsSortMode.None);
            for (int i = 0; i < movers.Length; i++)
            {
                NetworkPlayerAuthoritativeMover mover = movers[i];
                if (mover == null)
                {
                    continue;
                }

                // Core feel: slightly quicker acceleration, more elastic jump, better air control.
                SetFloat(mover, "moveSpeed", 6.6f);
                SetFloat(mover, "burstMoveSpeed", 8.9f);
                SetFloat(mover, "groundAcceleration", 76f);
                SetFloat(mover, "airAcceleration", 31f);
                SetFloat(mover, "dragDamping", 6.8f);
                SetFloat(mover, "jumpVelocity", 7.8f);
                SetFloat(mover, "coyoteTimeSeconds", 0.18f);
                SetFloat(mover, "jumpBufferSeconds", 0.18f);

                // Wall play: stronger parkour impulse and more forgiving detach/glide.
                SetFloat(mover, "wallStickDuration", 0.46f);
                SetFloat(mover, "wallSlideMaxFallSpeed", 2.0f);
                SetFloat(mover, "wallLaunchHorizontalForce", 9.2f);
                SetFloat(mover, "wallLaunchVerticalForce", 7.5f);
                SetFloat(mover, "wallLaunchInputInfluence", 1.6f);
                SetFloat(mover, "wallLaunchSeparationBoost", 3.1f);
                SetFloat(mover, "wallGlidePlanarAssist", 0.88f);
                SetFloat(mover, "postWallLaunchControlLockSeconds", 0.10f);
                SetFloat(mover, "postWallLaunchAirAccelerationMultiplier", 0.56f);
                SetFloat(mover, "wallDetachGraceSeconds", 0.24f);

                // Ragdoll should read as slapstick, not a permanent loss of control.
                SetFloat(mover, "ragdollMinDurationSeconds", 0.62f);
                SetFloat(mover, "ragdollRecoveryLockSeconds", 0.28f);
                SetFloat(mover, "ragdollHorizontalDamping", 1.55f);
            }
        }

        private static void SetFloat(NetworkPlayerAuthoritativeMover mover, string fieldName, float value)
        {
            FieldInfo field = typeof(NetworkPlayerAuthoritativeMover).GetField(fieldName, FieldFlags);
            if (field == null || field.FieldType != typeof(float))
            {
                return;
            }

            field.SetValue(mover, value);
        }
    }
}
