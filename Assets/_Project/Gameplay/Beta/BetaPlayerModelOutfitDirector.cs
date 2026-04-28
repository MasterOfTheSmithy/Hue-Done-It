// File: Assets/_Project/Gameplay/Beta/BetaPlayerModelOutfitDirector.cs
using HueDoneIt.Gameplay.Players;
using UnityEngine;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Installs runtime model/outfit presentation on every network avatar.
    /// </summary>
    [DefaultExecutionOrder(505)]
    [DisallowMultipleComponent]
    public sealed class BetaPlayerModelOutfitDirector : MonoBehaviour
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
            NetworkPlayerAvatar[] avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                NetworkPlayerAvatar avatar = avatars[i];
                if (avatar != null && avatar.GetComponent<BetaPlayerModelOutfitPresentation>() == null)
                {
                    avatar.gameObject.AddComponent<BetaPlayerModelOutfitPresentation>();
                }
            }
        }
    }
}
