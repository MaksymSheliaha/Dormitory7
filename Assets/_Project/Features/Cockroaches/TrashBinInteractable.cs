using System;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cockroaches
{
    public class TrashBinInteractable : MonoBehaviour, IInteractable
    {
        public static event Action OnAnyCockroachDisposed;

        [SerializeField] private BalanceConfig balanceConfig;

        public void Configure(BalanceConfig config) => balanceConfig = config;

        public string GetPrompt(PlayerContext context)
        {
            if (context.Inventory != null && context.Inventory.HasActiveItemWithId("dead_cockroach"))
                return "[E] Викинути тушку таргана у смітник (-Треш)";
            return "Смітник (візьміть у руку тушку таргана, щоб викинути)";
        }

        public bool CanInteract(PlayerContext context)
        {
            return context.Inventory != null && context.Inventory.HasActiveItemWithId("dead_cockroach");
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            if (context.Inventory.TryConsumeActiveItem())
            {
                float relief = balanceConfig != null ? balanceConfig.disposeCockroachTrashReduction : 2.5f;
                context.Stats?.ModifyTrash(-relief);
                OnAnyCockroachDisposed?.Invoke();
                HudController.Instance?.ShowToast($"Тушку таргана викинуто у смітник (-{relief:F1}% Трешу).");
            }
        }
    }
}
