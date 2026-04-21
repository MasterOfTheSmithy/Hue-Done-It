// File: Assets/_Project/Gameplay/Paint/PaintSplatData.cs
using UnityEngine;

namespace HueDoneIt.Gameplay.Paint
{
    public struct PaintSplatData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Radius;
        public float Intensity;
        public Color Color;

        public PaintEventKind EventKind;
        public PaintSplatType SplatType;
        public PaintSplatPermanence Permanence;

        public float ForceMagnitude;
        public Vector3 VelocityDirection;
        public Vector3 TangentDirection;

        // Keep both during merge reconciliation.
        public int PatternIndex;
        public int PatternSeed;

        public float StretchAmount;
        public float RotationDegrees;
    }
}