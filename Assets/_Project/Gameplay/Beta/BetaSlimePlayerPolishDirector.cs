// File: Assets/_Project/Gameplay/Beta/BetaSlimePlayerPolishDirector.cs
using HueDoneIt.Gameplay.Players;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Installs local presentation polish on every network avatar without changing network authority or colliders.
    /// This is intentionally visual/audio only; movement simulation remains in NetworkPlayerAuthoritativeMover.
    /// </summary>
    [DefaultExecutionOrder(510)]
    [DisallowMultipleComponent]
    public sealed class BetaSlimePlayerPolishDirector : MonoBehaviour
    {
        [SerializeField, Min(0.25f)] private float refreshIntervalSeconds = 0.75f;

        private float _nextRefreshTime;

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            InstallMissingPresentation();
        }

        private static void InstallMissingPresentation()
        {
            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (avatar == null)
                {
                    continue;
                }

                if (avatar.GetComponent<BetaSlimePlayerPresentation>() == null)
                {
                    avatar.gameObject.AddComponent<BetaSlimePlayerPresentation>();
                }
            }
        }
    }
}
