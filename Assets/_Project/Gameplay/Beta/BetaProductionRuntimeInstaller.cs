// File: Assets/_Project/Gameplay/Beta/BetaProductionRuntimeInstaller.cs
using HueDoneIt.Flood;
using HueDoneIt.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Self-installs beta polish/safety systems into gameplay scenes without relying on fragile scene wiring.
    /// This is intentionally additive: it does not replace authored systems, it patches over missing beta glue.
    /// </summary>
    public static class BetaProductionRuntimeInstaller
    {
        private const string RuntimeRootName = "__HueDoneIt_BetaProductionRuntime";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterInitialSceneLoad()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryInstallForActiveScene();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstallForScene(scene);
        }

        private static void TryInstallForActiveScene()
        {
            TryInstallForScene(SceneManager.GetActiveScene());
        }

        private static void TryInstallForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !LooksLikeGameplayScene(scene))
            {
                return;
            }

            GameObject existing = GameObject.Find(RuntimeRootName);
            if (existing != null)
            {
                EnsureComponents(existing);
                return;
            }

            GameObject root = new GameObject(RuntimeRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Object.DontDestroyOnLoad(root);
            EnsureComponents(root);
        }

        private static bool LooksLikeGameplayScene(Scene scene)
        {
            string sceneName = scene.name == null ? string.Empty : scene.name.ToLowerInvariant();
            if (sceneName.Contains("gameplay") || sceneName.Contains("undertint") || sceneName.Contains("flood"))
            {
                return true;
            }

            return Object.FindFirstObjectByType<NetworkRepairTask>() != null ||
                   Object.FindFirstObjectByType<FloodSequenceController>() != null;
        }

        private static void EnsureComponents(GameObject root)
        {
            // Full beta production polish stack. Keep existing playability/floor recovery systems, then layer
            // contextual slime presentation, movement feel tuning, task/flood feedback, HUD pulses, and audio.
            Ensure<BetaPlayableMapDirector>(root);
            Ensure<BetaRoomDeclutterDirector>(root);
            Ensure<BetaTaskAndStationLayoutDirector>(root);
            Ensure<BetaCollisionPlayabilityRepair>(root);
            Ensure<BetaFloorCollisionRepair>(root);
            Ensure<BetaAboveMapRecoveryDirector>(root);
            Ensure<BetaMapVariantDirector>(root);
            Ensure<BetaSafeStartDirector>(root);
            Ensure<BetaPlayerMovementStuckGuard>(root);
            Ensure<BetaFloodPlayabilityDirector>(root);
            Ensure<BetaTaskDifficultyTuner>(root);
            Ensure<BetaTaskEndpointGuard>(root);
            Ensure<BetaTaskWorldAffordancePresenter>(root);
            Ensure<BetaObjectiveGlowDirector>(root);
            Ensure<BetaSlimeMovementFeelTuner>(root);
            Ensure<BetaSlimePlayerPolishDirector>(root);
            Ensure<BetaPlayerModelOutfitDirector>(root);
            Ensure<BetaContextualTaskFeedbackDirector>(root);
            Ensure<BetaContextualHudFeedbackOverlay>(root);
            Ensure<BetaTaskPhysicalPresenter>(root);
            Ensure<BetaPointClickTaskOverlay>(root);
            Ensure<BetaTaskSequencePolishDirector>(root);
            Ensure<BetaHudDeclutterDirector>(root);
            Ensure<BetaPlayabilityObjectiveBoard>(root);
            Ensure<BetaWaterColorDiffusion>(root);
            Ensure<BetaFloodWarningBeaconInstaller>(root);
            Ensure<BetaFeedbackAudioDirector>(root);
            Ensure<BetaSlimeAudioFeedbackDirector>(root);
            Ensure<BetaRuntimePaintBudgetTuner>(root);
            Ensure<BetaPlayerSafetyNet>(root);
            Ensure<BetaMatchFlowSanityMonitor>(root);
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
