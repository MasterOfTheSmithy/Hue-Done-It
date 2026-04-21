namespace HueDoneIt.Gameplay.Paint
{
    public enum PaintEventKind : byte
    {
        Move = 0,
        Land = 1,
        WallStick = 2,
        WallLaunch = 3,
        Punch = 4,
        FloodBurst = 5,
        FloodDrip = 6,
        TaskInteract = 7,
        RagdollImpact = 8,
        ThrownObjectImpact = 9
    }
}
