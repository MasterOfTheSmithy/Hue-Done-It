// File: Assets/_Project/Core/Bootstrap/BootSceneEntrypoint.cs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Core.Bootstrap
{
    public static class BootSceneEntrypoint
    {
        private const string BootSceneName = "Boot";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootSceneIsLoadedFirst()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name == BootSceneName)
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(BootSceneName))
            {
                Debug.LogError($"Boot scene '{BootSceneName}' is missing from Build Settings.");
                return;
            }

            SceneManager.LoadScene(BootSceneName, LoadSceneMode.Single);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallBootstrapInBootScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.name != BootSceneName)
            {
                return;
            }

            if (Object.FindAnyObjectByType<AppBootstrap>() != null)
            {
                return;
            }

            GameObject bootstrapObject = new(nameof(AppBootstrap));
            bootstrapObject.AddComponent<AppBootstrap>();
        }
    }
}
