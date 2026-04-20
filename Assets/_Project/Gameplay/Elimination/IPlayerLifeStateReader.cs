// File: Assets/_Project/Gameplay/Elimination/IPlayerLifeStateReader.cs
namespace HueDoneIt.Gameplay.Elimination
{
    public interface IPlayerLifeStateReader
    {
        PlayerLifeStateKind CurrentLifeState { get; }
        bool IsAlive { get; }
    }
}
