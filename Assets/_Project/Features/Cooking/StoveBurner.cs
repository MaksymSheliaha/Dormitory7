using System;
using System.Collections.Generic;
using Dormitory7.Data;
using Dormitory7.Features.Quests;
using Dormitory7.Interaction;
using Dormitory7.NPC;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cooking
{
    public class StoveBurner : MonoBehaviour, IInteractable
    {
        public static readonly List<StoveBurner> AllBurners = new List<StoveBurner>();

        [SerializeField] private Transform panAnchor;
        [SerializeField] private GameObject flameGlowVisual;
        [SerializeField] private ItemDefinition hotPanDefinition;

        private WorldItem _mountedPanItem;
        private FryingPanState _mountedPanState;

        public bool HasPan => _mountedPanItem != null;
        public WorldItem MountedPanItem => _mountedPanItem;
        public FryingPanState MountedPanState => _mountedPanState;

        public static event Action<StoveBurner, WorldItem, FryingPanState> OnPlayerTookPanFromBurner;

        private void OnEnable()
        {
            if (!AllBurners.Contains(this))
                AllBurners.Add(this);
        }

        private void OnDisable()
        {
            AllBurners.Remove(this);
        }

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
                    if (context.Inventory != null && context.Inventory.HasActiveItemWithId("dead_cockroach"))
                        return "[E] Підсмажити тушку таргана (екстрена їжа: +10% Енергії, ризик -5 Респекту)";
                    return "[E] Зняти гарячу сковорідку (або покладіть Сиру картоплю / Таргана)";

                case DishState.Raw:
                case DishState.Cooking:
                    int sec = Mathf.CeilToInt(_mountedPanState.GetRemainingStageSeconds());
                    return $"Смажиться {_mountedPanState.CurrentDishName} ({sec}с) • [E] Зняти гарячу сковорідку";

                case DishState.NeedsFlip:
                    int flipSec = Mathf.CeilToInt(_mountedPanState.GetRemainingStageSeconds());
                    return $"[E] ПЕРЕВЕРНУТИ {_mountedPanState.CurrentDishName}! ({flipSec}с до підгоряння)";

                case DishState.Ready:
                    return _mountedPanState.IsCockroachDish
                        ? "[E] З'їсти смаженого таргана (+10% Енергії, -5 Респекту при свідках!)"
                        : "[E] З'їсти готову картоплю (+50% Енергії) та зняти гарячу сковорідку";

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

            if (_mountedPanState != null && _mountedPanState.State == DishState.Empty &&
                (context.Inventory.HasActiveItemWithId("raw_potato") || context.Inventory.HasActiveItemWithId("dead_cockroach")))
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
                        _mountedPanState.AddRawIngredients("Картопля", isCockroachDish: false);
                        HudController.Instance?.ShowToast("Картопля на сковорідці! Слідкуйте за таймером перевертання.");
                    }
                    return;
                }

                if (_mountedPanState.State == DishState.Empty && context.Inventory.HasActiveItemWithId("dead_cockroach"))
                {
                    if (context.Inventory.TryConsumeActiveItem())
                    {
                        _mountedPanState.AddRawIngredients("Смажений тарган", isCockroachDish: true);
                        HudController.Instance?.ShowToast("Екстрена кулінарія: тарган шкварчить на сковорідці... Сподівайся, ніхто не зайде!");
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
                    bool wasCockroach = _mountedPanState.IsCockroachDish;
                    float restore = _mountedPanState.EatReadyDish();
                    context.Stats?.ModifyEnergy(restore);

                    if (wasCockroach)
                    {
                        float witnessRange = context.Stats?.Config != null ? context.Stats.Config.friedCockroachWitnessRange : 8.5f;
                        string witness = FindNearbyNpcWitness(witnessRange);
                        if (!string.IsNullOrEmpty(witness))
                        {
                            float pen = context.Stats?.Config != null ? context.Stats.Config.friedCockroachWitnessRespectPenalty : 5f;
                            context.Stats?.ModifyRespect(-pen);
                            HudController.Instance?.ShowToast(
                                $"ФУ! {witness} побачив, як ви з'їли смаженого таргана! (+{Mathf.RoundToInt(restore)}% Енергії, -{Mathf.RoundToInt(pen)} Респекту)");
                            QuestManager.Instance?.AddChatMessage(
                                witness,
                                "Народ, ви не повірите... Першокурсник із 104-ї щойно підсмажив і з'їв таргана на кухні!!",
                                isAlert: true);
                        }
                        else
                        {
                            HudController.Instance?.ShowToast(
                                $"Екстрена їжа: смажений тарган (+{Mathf.RoundToInt(restore)}% Енергії). На щастя, свідків не було!");
                        }
                    }
                    else
                    {
                        HudController.Instance?.ShowToast($"Смачна картопля! (+{Mathf.RoundToInt(restore)}% Енергії). Несіть гарячу сковорідку в руках!");
                    }

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
            OnPlayerTookPanFromBurner?.Invoke(this, pan, panState);
        }

        private string FindNearbyNpcWitness(float maxDistance)
        {
            var npcs = FindObjectsByType<BaseNpcAgent>();
            for (int i = 0; i < npcs.Length; i++)
            {
                var npc = npcs[i];
                if (npc == null || !npc.gameObject.activeInHierarchy) continue;
                if (Mathf.Abs(npc.transform.position.y - transform.position.y) > 1.8f) continue;
                if (Vector3.Distance(npc.transform.position, transform.position) <= maxDistance)
                {
                    if (npc is HungryNeighborAI neighbor) return neighbor.NeighborName;
                    if (npc is GymBossAI) return "Бос качалки";
                    if (npc is KomendantAI) return "Зінаїда Петрівна";
                    return npc.name;
                }
            }
            return null;
        }

        public WorldItem DetachPanForNpc()
        {
            if (_mountedPanItem == null) return null;

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

            return pan;
        }
    }
}
