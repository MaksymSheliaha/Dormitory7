namespace Dormitory7.Interaction
{
    public interface IInteractable
    {
        string GetPrompt(PlayerContext context);
        bool CanInteract(PlayerContext context);
        void Interact(PlayerContext context);
    }

    public interface IHoldInteractable : IInteractable
    {
        float HoldDuration { get; }
        void OnHoldStart(PlayerContext context);
        void OnHoldProgress(PlayerContext context, float normalizedProgress);
        void OnHoldCancel(PlayerContext context);
    }
}
