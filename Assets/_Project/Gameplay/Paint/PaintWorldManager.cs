// File: Assets/_Project/Gameplay/Paint/PaintWorldManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HueDoneIt.Gameplay.Paint
{
    public sealed class PaintWorldManager : MonoBehaviour
    {
        private static PaintWorldManager _instance;

        [Header("Routing")]
        [SerializeField, Min(0.01f)] private float chunkSearchPadding = 0.5f;

        public static PaintWorldManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PaintWorldManager>();
                }

                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallRuntime()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return;
            }

            if (FindFirstObjectByType<PaintWorldManager>() != null)
            {
                return;
            }

            GameObject go = new GameObject(nameof(PaintWorldManager));
            go.AddComponent<PaintWorldManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        public static bool SubmitBurst(PaintBurstCommand burst)
        {
            if (Instance == null)
            {
                return false;
            }

            return Instance.HandleBurstInternal(burst);
        }

        public static bool SubmitLegacy(PaintSplatData splatData, Color color)
        {
            return SubmitBurst(PaintBurstCommand.FromLegacy(splatData, color));
        }

        private bool HandleBurstInternal(PaintBurstCommand burst)
        {
            if (PaintSurfaceRegistry.RegisteredChunks.Count == 0 && PaintSurfaceRegistry.RegisteredWaterReceivers.Count == 0)
            {
                RuntimePaintInstaller.EnsureInstalledForScene(null, false);
            }

            bool applied = false;

            var waters = PaintSurfaceRegistry.RegisteredWaterReceivers;
            for (int i = 0; i < waters.Count; i++)
            {
                WaterPaintReceiver receiver = waters[i];
                if (receiver == null || !receiver.CanAffect(burst))
                {
                    continue;
                }

                receiver.InjectPaint(burst);
                applied = true;
            }

            var chunks = PaintSurfaceRegistry.RegisteredChunks;
            for (int i = 0; i < chunks.Count; i++)
            {
                PaintSurfaceChunk chunk = chunks[i];
                if (chunk == null || !chunk.enabled || !chunk.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds bounds = chunk.WorldBounds;
                bounds.Expand((burst.Radius + chunkSearchPadding) * 2f);
                if (!bounds.Contains(burst.Position) && !chunk.CanProject(burst))
                {
                    continue;
                }

                chunk.ApplyBurst(burst);
                // A burst that reaches a chunk should count as handled even if the visible path
                // later falls back to tint/overlay behavior. Otherwise the player emitter thinks
                // a surface-paint target consumed the hit but produced no visual fallback.
                applied = true;
            }

            return applied;
        }
    }
}
