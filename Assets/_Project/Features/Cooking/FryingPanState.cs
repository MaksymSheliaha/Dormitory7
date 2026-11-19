using System;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Cooking
{
    public enum DishState
    {
        Empty = 0,
        Raw = 1,
        Cooking = 2,
        NeedsFlip = 3,
        Ready = 4,
        Burned = 5
    }

    public class FryingPanState : MonoBehaviour
    {
        public static event Action<string> OnAnyMealCooked;

        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private ItemDefinition hotPanDefinition;
        [SerializeField] private GameObject foodVisual;
        [SerializeField] private GameObject steamVisual;
        [SerializeField] private GameObject smokeVisual;

        public DishState State { get; private set; } = DishState.Empty;
        public bool IsOnActiveBurner { get; private set; }
        public bool HasBeenFlipped { get; private set; }
        public float StageTimer { get; private set; }
        public string CurrentDishName { get; private set; } = "Картопля";
        public string OwnerNpcName { get; set; } = "";

        public bool HasEdibleFood =>
            State == DishState.Raw ||
            State == DishState.Cooking ||
            State == DishState.NeedsFlip ||
            State == DishState.Ready;

        private DormStatsManager _stats;
        private Renderer _foodRenderer;
        private WorldItem _worldItem;

        private static readonly Color RawColor = new Color(0.90f, 0.84f, 0.56f, 1f);
        private static readonly Color CookingColor = new Color(0.88f, 0.68f, 0.28f, 1f);
        private static readonly Color NeedsFlipColor = new Color(0.94f, 0.52f, 0.16f, 1f);
        private static readonly Color ReadyColor = new Color(0.76f, 0.45f, 0.14f, 1f);
        private static readonly Color BurnedColor = new Color(0.11f, 0.10f, 0.10f, 1f);

        public void Configure(
            BalanceConfig config,
            ItemDefinition hotDef,
            GameObject foodObj,
            GameObject steamObj,
            GameObject smokeObj)
        {
            balanceConfig = config;
            hotPanDefinition = hotDef;
            foodVisual = foodObj;
            steamVisual = steamObj;
            smokeVisual = smokeObj;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            _worldItem = GetComponent<WorldItem>();
            if (foodVisual != null)
                _foodRenderer = foodVisual.GetComponentInChildren<Renderer>(true);

            UpdateVisuals();
        }

        private void Start()
        {
            _stats = FindAnyObjectByType<DormStatsManager>();
        }

        public void SetOnBurner(bool onBurner)
        {
            IsOnActiveBurner = onBurner;
            if (onBurner && State == DishState.Raw)
            {
                State = DishState.Cooking;
                StageTimer = 0f;
            }
            UpdateVisuals();
        }

        public bool AddRawIngredients(string dishName = "Картопля")
        {
            if (State != DishState.Empty) return false;
            CurrentDishName = dishName;
            HasBeenFlipped = false;
            StageTimer = 0f;
            State = IsOnActiveBurner ? DishState.Cooking : DishState.Raw;
            UpdateVisuals();
            return true;
        }

        public bool FlipDish()
        {
            if (State != DishState.NeedsFlip) return false;
            HasBeenFlipped = true;
            State = DishState.Cooking;
            StageTimer = 0f;
            UpdateVisuals();
            HudController.Instance?.ShowToast($"{CurrentDishName} перевернуто! Досмажується...");
            return true;
        }

        public float EatReadyDish()
        {
            if (State != DishState.Ready) return 0f;
            float restore = balanceConfig != null ? balanceConfig.cookedMealEnergyRestore : 50f;
            State = DishState.Empty;
            HasBeenFlipped = false;
            StageTimer = 0f;
            UpdateVisuals();
            return restore;
        }

        public void ClearDish()
        {
            State = DishState.Empty;
            HasBeenFlipped = false;
            StageTimer = 0f;
            UpdateVisuals();
        }

        public void HeatUpPanForPickup()
        {
            if (_worldItem != null && hotPanDefinition != null)
            {
                _worldItem.Initialize(hotPanDefinition);
                _worldItem.StartCooling();
            }
        }

        public float GetRemainingStageSeconds()
        {
            if (balanceConfig == null) return 0f;
            return State switch
            {
                DishState.Cooking => Mathf.Max(0f, (HasBeenFlipped ? balanceConfig.cookTimeAfterFlipSeconds : balanceConfig.cookTimeToFlipSeconds) - StageTimer),
                DishState.NeedsFlip => Mathf.Max(0f, balanceConfig.flipWindowSeconds - StageTimer),
                DishState.Ready => Mathf.Max(0f, balanceConfig.readyBeforeBurnSeconds - StageTimer),
                _ => 0f
            };
        }

        public void SimulateCookingStep(float deltaTime)
        {
            if (!IsOnActiveBurner || balanceConfig == null) return;
            if (State == DishState.Empty) return;

            if (State == DishState.Raw)
            {
                State = DishState.Cooking;
                StageTimer = 0f;
                UpdateVisuals();
            }

            if (State == DishState.Burned)
            {
                if (_stats != null)
                    _stats.ModifyTrash(balanceConfig.burnTrashPerSecond * deltaTime);

                if (smokeVisual != null)
                {
                    float pulse = 0.85f + 0.25f * Mathf.Sin(Time.time * 4.5f);
                    smokeVisual.transform.localScale = new Vector3(0.45f * pulse, 0.75f * pulse, 0.45f * pulse);
                }
                return;
            }

            StageTimer += deltaTime;

            if (State == DishState.Cooking)
            {
                float target = HasBeenFlipped ? balanceConfig.cookTimeAfterFlipSeconds : balanceConfig.cookTimeToFlipSeconds;
                if (StageTimer >= target)
                {
                    StageTimer = 0f;
                    if (!HasBeenFlipped)
                    {
                        State = DishState.NeedsFlip;
                        HudController.Instance?.ShowToast($"Кухня: {CurrentDishName} потребує перевертання [E]!");
                    }
                    else
                    {
                        State = DishState.Ready;
                        OnAnyMealCooked?.Invoke(CurrentDishName);
                        HudController.Instance?.ShowToast($"Кухня: {CurrentDishName} ГОТОВА! Зніміть, поки не згоріла!");
                    }
                    UpdateVisuals();
                }
            }
            else if (State == DishState.NeedsFlip)
            {
                if (StageTimer >= balanceConfig.flipWindowSeconds)
                {
                    EnterBurnedState();
                }
            }
            else if (State == DishState.Ready)
            {
                if (StageTimer >= balanceConfig.readyBeforeBurnSeconds)
                {
                    EnterBurnedState();
                }
            }
        }

        private void Update()
        {
            if (!IsOnActiveBurner) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            SimulateCookingStep(Time.deltaTime);
        }

        private void EnterBurnedState()
        {
            State = DishState.Burned;
            StageTimer = 0f;
            UpdateVisuals();
            HudController.Instance?.ShowToast("ДИМ НА КУХНІ! Страва підгоріла і підвищує Треш!");
        }

        private void UpdateVisuals()
        {
            bool showFood = State != DishState.Empty;
            if (foodVisual != null)
                foodVisual.SetActive(showFood);

            if (showFood && _foodRenderer != null)
            {
                Color c = State switch
                {
                    DishState.Raw => RawColor,
                    DishState.Cooking => CookingColor,
                    DishState.NeedsFlip => NeedsFlipColor,
                    DishState.Ready => ReadyColor,
                    DishState.Burned => BurnedColor,
                    _ => RawColor
                };
                var mpb = new MaterialPropertyBlock();
                _foodRenderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                _foodRenderer.SetPropertyBlock(mpb);
            }

            if (steamVisual != null)
                steamVisual.SetActive(IsOnActiveBurner && (State == DishState.Cooking || State == DishState.NeedsFlip || State == DishState.Ready));

            if (smokeVisual != null)
                smokeVisual.SetActive(State == DishState.Burned);
        }
    }
}
