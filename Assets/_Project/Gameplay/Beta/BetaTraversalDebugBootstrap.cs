// File: Assets/_Project/Gameplay/Beta/BetaTraversalDebugBootstrap.cs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Independent debug bootstrap for traversal/floor regressions.
    /// This does not replace the main beta installer; it layers diagnostics and recovery on top of whatever Codex added.
    /// </summary>
    public static class BetaTraversalDebugBootstrap
    {
        private const string RootName = "__HueDoneIt_TraversalDebugRuntime";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryInstall(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstall(scene);
        }

        private static void TryInstall(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !LooksLikeGameplayScene(scene))
            {
                return;
            }

            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            Ensure<BetaFloorRegressionRecoveryDirector>(root);
            Ensure<BetaColliderMutationDebugger>(root);
            Ensure<BetaPlayerFloorProbeDebugger>(root);
        }

        private static bool LooksLikeGameplayScene(Scene scene)
        {
            string name = scene.name;
            return name.IndexOf("Gameplay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Undertint", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static T Ensure<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            if (component == null)
            {
                component = root.AddComponent<T>();
            }

            return component;
        }
    }
}
