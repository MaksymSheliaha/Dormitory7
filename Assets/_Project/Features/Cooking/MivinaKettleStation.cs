using System;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cooking
{
    public class MivinaKettleStation : MonoBehaviour, IInteractable
    {
        public static event Action OnAnyMivinaBrewed;

        [SerializeField] private BalanceConfig balanceConfig;

        public void Configure(BalanceConfig config) => balanceConfig = config;

        public string GetPrompt(PlayerContext context)
        {
            if (context.Inventory != null && context.Inventory.HasItemWithId("mivina"))
                return "[E] Заварити «Мівіну» в кухлі (+25% Енергії)";
            return "Чайник для швидкої локшини — принесіть пачку «Мівіни»";
        }

        public bool CanInteract(PlayerContext context)
        {
            return context.Inventory != null && context.Inventory.HasItemWithId("mivina");
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            if (context.Inventory.TryConsumeItemById("mivina"))
            {
                float restore = balanceConfig != null ? balanceConfig.mivinaEnergyRestore : 25f;
                context.Stats?.ModifyEnergy(restore);
                OnAnyMivinaBrewed?.Invoke();
                HudController.Instance?.ShowToast($"Заварено «Мівіну»! (+{Mathf.RoundToInt(restore)}% Енергії)");
            }
        }
    }
}
