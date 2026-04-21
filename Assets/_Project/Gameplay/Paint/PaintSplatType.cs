// File: Assets/_Project/Gameplay/Paint/PaintSplatType.cs
namespace HueDoneIt.Gameplay.Paint
{
    public enum PaintSplatType : byte
    {
        Generic = 0,

        Default = 1,
        Footstep = 2,
        MoveSmear = 3,
        Landing = 4,
        WallImpact = 5,
        WallScrape = 6,
        WallLaunchBurst = 7,
        Punch = 8,
        HeavyImpact = 9,
        RagdollImpact = 10,
        TaskInteract = 11,
        ThrownObject = 12,
        Flood = 13
    }
}