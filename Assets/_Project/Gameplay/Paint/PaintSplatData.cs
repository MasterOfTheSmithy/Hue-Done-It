using UnityEngine;

namespace HueDoneIt.Gameplay.Paint
{
    public struct PaintSplatData
    {
        public PaintEventKind EventKind;
        public PaintSplatType SplatType;
        public PaintSplatPermanence Permanence;
        public Vector3 Position;
        public Vector3 Normal;
        public float Radius;
        public float Intensity;
        public float ForceMagnitude;
        public Vector3 VelocityDirection;
        public Vector3 TangentDirection;
        public int PatternIndex;
        public int PatternSeed;
        public float StretchAmount;
        public float RotationDegrees;
    }
}
