// File: Assets/_Project/Gameplay/Interaction/IInteractable.cs
namespace HueDoneIt.Gameplay.Interaction
{
    public interface IInteractable
    {
        float MaxUseDistance { get; }
        bool CanInteract(in InteractionContext context);
        string GetPromptText(in InteractionContext context);
        bool TryInteract(in InteractionContext context);
    }
}
