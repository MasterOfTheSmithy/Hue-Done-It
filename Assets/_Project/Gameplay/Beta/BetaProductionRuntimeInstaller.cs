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
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject existing = GameObject.Find(RuntimeRootName);
            if (!LooksLikeGameplayScene(scene))
            {
                if (existing != null)
                {
                    Object.Destroy(existing);
                }

                return;
            }

            if (existing != null)
            {
                EnsureComponents(existing);
                return;
            }

            GameObject root = new GameObject(RuntimeRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            EnsureComponents(root);
        }

        private static bool LooksLikeGameplayScene(Scene scene)
        {
            return BetaGameplaySceneCatalog.IsProductionGameplayScene(scene.name);
        }

        private static void EnsureComponents(GameObject root)
        {
            DisableNormalPlayDuplicates();

            // Normal beta production profile. GameplayBetaSceneInstaller owns authoritative map/state objects;
            // this root owns a single presentation/safety layer so helper systems do not fight each other.
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
            Ensure<BetaWaterColorDiffusion>(root);
            Ensure<BetaFloodWarningBeaconInstaller>(root);
            Ensure<BetaFeedbackAudioDirector>(root);
            Ensure<BetaRuntimePaintBudgetTuner>(root);
            Ensure<BetaPlayerSafetyNet>(root);
            Ensure<BetaMatchFlowSanityMonitor>(root);
        }

        private static void DisableNormalPlayDuplicates()
        {
            DisableIfPresent<BetaAlwaysOnHudOverlay>();
            DisableIfPresent<BetaObjectiveRouteCompass>();
            DisableIfPresent<BetaPlayabilityObjectiveBoard>();
            DisableIfPresent<BetaSlimeAudioFeedbackDirector>();
            DisableIfPresent<BetaColliderMutationDebugger>();
            DisableIfPresent<BetaPlayerFloorProbeDebugger>();
            DisableIfPresent<BetaPlaytestDiagnostics>();
        }

        private static void DisableIfPresent<T>() where T : Behaviour
        {
            T[] components = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (component != null)
                {
                    component.enabled = false;
                }
            }
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
