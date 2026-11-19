using UnityEngine;

namespace Dormitory7.Interaction
{
    public class BedInteractable : MonoBehaviour, IInteractable
    {
        public string GetPrompt(PlayerContext context)
        {
            if (context.DayTime == null)
                return "[E] Лягти спати";

            return $"[E] Лягти спати (+25% Енергії, наступний день)";
        }

        public bool CanInteract(PlayerContext context)
        {
            return context.DayTime != null && !context.DayTime.IsWeekCompleted;
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            context.DayTime.SleepAndAdvanceDay();
        }
    }
}
