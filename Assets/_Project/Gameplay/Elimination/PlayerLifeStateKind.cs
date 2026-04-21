// File: Assets/_Project/Gameplay/Elimination/PlayerLifeStateKind.cs
namespace HueDoneIt.Gameplay.Elimination
{
    public enum PlayerLifeStateKind : byte
    {
        Alive = 0,
        Eliminated = 1,
        DiffusedByFlood = 2,
        Backfired = 3
    }
}
