// File: Assets/_Project/Gameplay/Beta/BetaGameplaySceneCatalog.cs
using System;

namespace HueDoneIt.Gameplay.Beta
{
    /// <summary>
    /// Central registry for the production gameplay map family. This lets the beta systems treat the
    /// Undertint maps as one cohesive set while still allowing each scene to apply its own runtime pass.
    /// </summary>
    public static class BetaGameplaySceneCatalog
    {
        public const string MainMap = "Gameplay_Undertint";
        public const string AnnexMap = "Gameplay_Undertint_Annex";
        public const string OverflowMap = "Gameplay_Undertint_Overflow";

        public static readonly string[] ProductionGameplayScenes =
        {
            MainMap,
            AnnexMap,
            OverflowMap
        };

        public static readonly string[] LobbySelectableMaps =
        {
            MainMap,
            AnnexMap,
            OverflowMap,
            "Test_Flood",
            "Test_Tasks"
        };

        public static bool IsProductionGameplayScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            for (int i = 0; i < ProductionGameplayScenes.Length; i++)
            {
                if (string.Equals(sceneName, ProductionGameplayScenes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static string ValidateProductionSceneOrDefault(string sceneName)
        {
            return IsProductionGameplayScene(sceneName) ? sceneName : MainMap;
        }

        public static int GetSceneVariantIndex(string sceneName)
        {
            if (string.Equals(sceneName, AnnexMap, StringComparison.Ordinal))
            {
                return 1;
            }

            if (string.Equals(sceneName, OverflowMap, StringComparison.Ordinal))
            {
                return 2;
            }

            return 0;
        }

        public static string GetReadableMapName(string sceneName)
        {
            if (string.Equals(sceneName, AnnexMap, StringComparison.Ordinal))
            {
                return "Undertint Annex";
            }

            if (string.Equals(sceneName, OverflowMap, StringComparison.Ordinal))
            {
                return "Undertint Overflow";
            }

            if (string.Equals(sceneName, MainMap, StringComparison.Ordinal))
            {
                return "Undertint Core";
            }

            return string.IsNullOrWhiteSpace(sceneName) ? MainMap : sceneName;
        }
    }
}
