// File: Assets/_Project/Gameplay/Paint/PaintSurfaceRegistry.cs
using System.Collections.Generic;

namespace HueDoneIt.Gameplay.Paint
{
    public static class PaintSurfaceRegistry
    {
        private static readonly List<PaintSurfaceChunk> Chunks = new List<PaintSurfaceChunk>();
        private static readonly List<WaterPaintReceiver> WaterReceivers = new List<WaterPaintReceiver>();

        public static IReadOnlyList<PaintSurfaceChunk> RegisteredChunks => Chunks;
        public static IReadOnlyList<WaterPaintReceiver> RegisteredWaterReceivers => WaterReceivers;

        public static void Register(PaintSurfaceChunk chunk)
        {
            if (chunk == null || Chunks.Contains(chunk))
            {
                return;
            }

            Chunks.Add(chunk);
        }

        public static void Unregister(PaintSurfaceChunk chunk)
        {
            if (chunk == null)
            {
                return;
            }

            Chunks.Remove(chunk);
        }

        public static void Register(WaterPaintReceiver receiver)
        {
            if (receiver == null || WaterReceivers.Contains(receiver))
            {
                return;
            }

            WaterReceivers.Add(receiver);
        }

        public static void Unregister(WaterPaintReceiver receiver)
        {
            if (receiver == null)
            {
                return;
            }

            WaterReceivers.Remove(receiver);
        }
    }
}
