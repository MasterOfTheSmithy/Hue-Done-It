// File: Assets/_Project/Gameplay/Paint/PaintBurstCommand.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Paint
{
    public struct PaintBurstCommand
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Velocity;
        public float Speed;
        public float Radius;
        public float Volume;
        public Color32 Color;
        public PaintEventKind EventKind;
        public uint Seed;
        public bool Permanent;

        public static PaintBurstCommand FromLegacy(PaintSplatData splatData, Color color)
        {
            return new PaintBurstCommand
            {
                Position = splatData.Position,
                Normal = splatData.Normal.sqrMagnitude > 0.0001f ? splatData.Normal.normalized : Vector3.up,
                Velocity = splatData.VelocityDirection.sqrMagnitude > 0.0001f ? splatData.VelocityDirection.normalized * splatData.ForceMagnitude : Vector3.zero,
                Speed = splatData.ForceMagnitude,
                Radius = Mathf.Max(0.025f, splatData.Radius),
                Volume = Mathf.Clamp01(splatData.Intensity),
                Color = color,
                EventKind = splatData.EventKind,
                Seed = unchecked((uint)(splatData.PatternSeed == 0 ? splatData.PatternIndex + 1 : splatData.PatternSeed)),
                Permanent = splatData.Permanence == PaintSplatPermanence.Permanent
            };
        }
    }
}
