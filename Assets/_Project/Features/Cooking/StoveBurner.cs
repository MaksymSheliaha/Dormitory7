using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cooking
{
    public class StoveBurner : MonoBehaviour, IInteractable
    {
        [SerializeField] private Transform panAnchor;
        [SerializeField] private GameObject flameGlowVisual;
        [SerializeField] private ItemDefinition hotPanDefinition;

        private WorldItem _mountedPanItem;
        private FryingPanState _mountedPanState;

        public bool HasPan => _mountedPanItem != null;
        public FryingPanState MountedPanState => _mountedPanState;

        public void Configure(Transform anchor, GameObject flameVisual, ItemDefinition hotDef)
        {
            panAnchor = anchor;
            flameGlowVisual = flameVisual;
            hotPanDefinition = hotDef;
        }

        private void Awake()
        {
            if (flameGlowVisual != null)
                flameGlowVisual.SetActive(false);
        }

        public string GetPrompt(PlayerContext context)
        {
            if (!HasPan)
            {
                var active = context.ActiveItem;
                if (active != null && (active.itemId == "pan_cold" || active.itemId == "pan_hot"))
                    return "[E] Поставити сковорідку на конфорку";
                return "Конфорка вільна — принесіть Сковорідку";
            }

            if (_mountedPanState == null)
                return "[E] Зняти гарячу сковорідку";

            switch (_mountedPanState.State)
            {
                case DishState.Empty:
                    if (context.Inventory != null && context.Inventory.HasActiveItemWithId("raw_potato"))
                        return "[E] Покласти сиру картоплю на сковорідку";
                    return "[E] Зняти гарячу сковорідку (або покладіть Сиру картоплю)";

                case DishState.Raw:
                case DishState.Cooking:
                    int sec = Mathf.CeilToInt(_mountedPanState.GetRemainingStageSeconds());
                    return $"Смажиться ({sec}с) • [E] Зняти гарячу сковорідку";

                case DishState.NeedsFlip:
                    int flipSec = Mathf.CeilToInt(_mountedPanState.GetRemainingStageSeconds());
                    return $"[E] ПЕРЕВЕРНУТИ картоплю! ({flipSec}с до підгоряння)";

                case DishState.Ready:
                    return "[E] З'їсти готову картоплю (+50% Енергії) та зняти гарячу сковорідку";

                case DishState.Burned:
                    return "[E] Зняти згорілу сковорідку (зупинити дим і Треш!)";

                default:
                    return "[E] Зняти сковорідку";
            }
        }

        public bool CanInteract(PlayerContext context)
        {
            if (context.Inventory == null) return false;

            if (!HasPan)
            {
                var active = context.ActiveItem;
                return active != null && (active.itemId == "pan_cold" || active.itemId == "pan_hot");
            }

            if (_mountedPanState != null && _mountedPanState.State == DishState.Empty && context.Inventory.HasActiveItemWithId("raw_potato"))
                return true;

            if (_mountedPanState != null && _mountedPanState.State == DishState.NeedsFlip)
                return true;

            // Lifting pan requires free hands (hot pan is two-handed!)
            return !context.IsCarryingTwoHanded;
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;

            if (!HasPan)
            {
                MountPanFromPlayer(context);
                return;
            }

            if (_mountedPanState != null)
            {
                if (_mountedPanState.State == DishState.Empty && context.Inventory.HasActiveItemWithId("raw_potato"))
                {
                    if (context.Inventory.TryConsumeActiveItem())
                    {
                        _mountedPanState.AddRawIngredients("Картопля");
                        HudController.Instance?.ShowToast("Картопля на сковорідці! Слідкуйте за таймером перевертання.");
                    }
                    return;
                }

                if (_mountedPanState.State == DishState.NeedsFlip)
                {
                    _mountedPanState.FlipDish();
                    return;
                }

                if (_mountedPanState.State == DishState.Ready)
                {
                    float restore = _mountedPanState.EatReadyDish();
                    context.Stats?.ModifyEnergy(restore);
                    HudController.Instance?.ShowToast($"Смачна картопля! (+{Mathf.RoundToInt(restore)}% Енергії). Несіть гарячу сковорідку в руках!");
                    LiftPanToPlayer(context);
                    return;
                }

                if (_mountedPanState.State == DishState.Burned)
                {
                    _mountedPanState.ClearDish();
                    HudController.Instance?.ShowToast("Згорілу страву прибрано, дим зупинено! Сковорідка гаряча.");
                    LiftPanToPlayer(context);
                    return;
                }
            }

            LiftPanToPlayer(context);
        }

        private void MountPanFromPlayer(PlayerContext context)
        {
            WorldItem pan = context.Inventory.DetachActiveItemForWorld();
            if (pan == null) return;

            _mountedPanItem = pan;
            _mountedPanState = pan.GetComponent<FryingPanState>();
            if (_mountedPanState == null)
                _mountedPanState = pan.gameObject.AddComponent<FryingPanState>();

            Transform targetAnchor = panAnchor != null ? panAnchor : transform;
            pan.SetHeldState(targetAnchor, Vector3.zero, Vector3.zero);
            _mountedPanState.SetOnBurner(true);

            if (flameGlowVisual != null)
                flameGlowVisual.SetActive(true);
        }

        private void LiftPanToPlayer(PlayerContext context)
        {
            if (_mountedPanItem == null) return;

            WorldItem pan = _mountedPanItem;
            FryingPanState panState = _mountedPanState;

            _mountedPanItem = null;
            _mountedPanState = null;

            if (flameGlowVisual != null)
                flameGlowVisual.SetActive(false);

            if (panState != null)
            {
                panState.SetOnBurner(false);
                panState.HeatUpPanForPickup();
            }
            else if (hotPanDefinition != null)
            {
                pan.Initialize(hotPanDefinition);
                pan.StartCooling();
            }

            context.Inventory.TryPickUp(pan);
        }
    }
}
