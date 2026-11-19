using Dormitory7.NPC;
using UnityEngine;

namespace Dormitory7.Interaction
{
    public class BedInteractable : MonoBehaviour, IInteractable
    {
        private KomendantAI _komendant;

        private void Start()
        {
            _komendant = FindAnyObjectByType<KomendantAI>();
        }

        private bool IsBeingChasedByKomendant()
        {
            if (_komendant == null)
                _komendant = FindAnyObjectByType<KomendantAI>();
            return _komendant != null &&
                   (_komendant.State == KomendantState.Chasing || _komendant.State == KomendantState.BribeWindow);
        }

        public string GetPrompt(PlayerContext context)
        {
            if (context.DayTime == null)
                return "[E] Лягти спати";

            if (IsBeingChasedByKomendant())
                return "Не можна спати, поки за вами женеться комендантша!";

            if (context.DayTime.CurrentDay >= 5)
                return "[E] Лягти спати — Завершити тиждень (П'ятниця!)";

            int restore = context.Stats?.Config != null ? Mathf.RoundToInt(context.Stats.Config.sleepEnergyRestore) : 30;
            return $"[E] Лягти спати (+{restore}% Енергії, наступний день)";
        }

        public bool CanInteract(PlayerContext context)
        {
            return context.DayTime != null && !context.DayTime.IsWeekCompleted && !IsBeingChasedByKomendant();
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            context.DayTime.SleepAndAdvanceDay();
        }
    }
}
