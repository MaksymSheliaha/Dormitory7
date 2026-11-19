using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Player;
using TMPro;
using UnityEngine;

namespace Dormitory7.UI
{
    public class HudController : MonoBehaviour
    {
        [Header("Core References")]
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private DayTimeController dayTimeController;
        [SerializeField] private QuickInventory quickInventory;
        [SerializeField] private PlayerInteractor playerInteractor;
        [SerializeField] private FirstPersonController playerController;
        [SerializeField] private GameStateManager gameStateManager;

        [Header("Top-Left Status Panel")]
        [SerializeField] private UnityEngine.UI.Image energyFillImage;
        [SerializeField] private TextMeshProUGUI energyValueText;
        [SerializeField] private TextMeshProUGUI respectValueText;
        [SerializeField] private UnityEngine.UI.Image trashFillImage;
        [SerializeField] private TextMeshProUGUI trashValueText;
        [SerializeField] private UnityEngine.UI.Image[] reprimandStampImages;
        [SerializeField] private TextMeshProUGUI clockText;
        [SerializeField] private TextMeshProUGUI stealthBadgeText;

        [Header("Center Crosshair & Prompts")]
        [SerializeField] private UnityEngine.UI.Image crosshairDot;
        [SerializeField] private TextMeshProUGUI interactionPromptText;
        [SerializeField] private TextMeshProUGUI controlsHintText;
        [SerializeField] private UnityEngine.UI.Image holdProgressBarFill;
        [SerializeField] private GameObject holdProgressContainer;

        [Header("Bottom 4-Slot Quick Inventory")]
        [SerializeField] private UnityEngine.UI.Image[] slotBackgrounds;
        [SerializeField] private TextMeshProUGUI[] slotItemLabels;
        [SerializeField] private GameObject carryBannerRoot;
        [SerializeField] private TextMeshProUGUI carryBannerText;

        [Header("Toast & State Overlay")]
        [SerializeField] private GameObject toastBannerRoot;
        [SerializeField] private TextMeshProUGUI toastBannerText;
        [SerializeField] private GameObject stateOverlayRoot;
        [SerializeField] private TextMeshProUGUI stateOverlayTitle;
        [SerializeField] private TextMeshProUGUI stateOverlayBody;
        [SerializeField] private UnityEngine.UI.Button resumeButton;
        [SerializeField] private UnityEngine.UI.Button restartButton;

        private static readonly Color EnergyNormalColor = new Color(0.96f, 0.65f, 0.14f, 1f);
        private static readonly Color EnergyLowColor = new Color(0.92f, 0.22f, 0.18f, 1f);
        private static readonly Color ClockNormalColor = new Color(0.90f, 0.94f, 0.88f, 1f);
        private static readonly Color ClockWarningColor = new Color(0.98f, 0.72f, 0.20f, 1f);
        private static readonly Color ClockCurfewColor = new Color(0.95f, 0.25f, 0.22f, 1f);
        private static readonly Color SlotNormalColor = new Color(0.10f, 0.12f, 0.11f, 0.78f);
        private static readonly Color SlotSelectedColor = new Color(0.28f, 0.44f, 0.34f, 0.94f);
        private static readonly Color StampInactiveColor = new Color(0.25f, 0.25f, 0.25f, 0.45f);
        private static readonly Color StampActiveColor = new Color(0.84f, 0.16f, 0.16f, 1f);

        private float _toastTimer;

        public void BindCore(
            DormStatsManager stats,
            DayTimeController dayTime,
            QuickInventory inv,
            PlayerInteractor interactor,
            FirstPersonController fpc,
            GameStateManager stateMgr)
        {
            statsManager = stats;
            dayTimeController = dayTime;
            quickInventory = inv;
            playerInteractor = interactor;
            playerController = fpc;
            gameStateManager = stateMgr;
        }

        public void BindTopLeft(
            UnityEngine.UI.Image energyFill,
            TextMeshProUGUI energyText,
            TextMeshProUGUI respectText,
            UnityEngine.UI.Image trashFill,
            TextMeshProUGUI trashText,
            UnityEngine.UI.Image[] stamps,
            TextMeshProUGUI clock,
            TextMeshProUGUI stealthBadge)
        {
            energyFillImage = energyFill;
            energyValueText = energyText;
            respectValueText = respectText;
            trashFillImage = trashFill;
            trashValueText = trashText;
            reprimandStampImages = stamps;
            clockText = clock;
            stealthBadgeText = stealthBadge;
        }

        public void BindCenter(
            UnityEngine.UI.Image crosshair,
            TextMeshProUGUI prompt,
            TextMeshProUGUI hint,
            GameObject holdRoot,
            UnityEngine.UI.Image holdFill)
        {
            crosshairDot = crosshair;
            interactionPromptText = prompt;
            controlsHintText = hint;
            holdProgressContainer = holdRoot;
            holdProgressBarFill = holdFill;
        }

        public void BindInventory(
            UnityEngine.UI.Image[] backgrounds,
            TextMeshProUGUI[] labels,
            GameObject carryRoot,
            TextMeshProUGUI carryText)
        {
            slotBackgrounds = backgrounds;
            slotItemLabels = labels;
            carryBannerRoot = carryRoot;
            carryBannerText = carryText;
        }

        public void BindOverlays(
            GameObject toastRoot,
            TextMeshProUGUI toastText,
            GameObject overlayRoot,
            TextMeshProUGUI overlayTitle,
            TextMeshProUGUI overlayBody,
            UnityEngine.UI.Button resumeBtn,
            UnityEngine.UI.Button restartBtn)
        {
            toastBannerRoot = toastRoot;
            toastBannerText = toastText;
            stateOverlayRoot = overlayRoot;
            stateOverlayTitle = overlayTitle;
            stateOverlayBody = overlayBody;
            resumeButton = resumeBtn;
            restartButton = restartBtn;
        }

        private void Awake()
        {
            if (statsManager == null) statsManager = FindAnyObjectByType<DormStatsManager>();
            if (dayTimeController == null) dayTimeController = FindAnyObjectByType<DayTimeController>();
            if (quickInventory == null) quickInventory = FindAnyObjectByType<QuickInventory>();
            if (playerInteractor == null) playerInteractor = FindAnyObjectByType<PlayerInteractor>();
            if (playerController == null) playerController = FindAnyObjectByType<FirstPersonController>();
            if (gameStateManager == null) gameStateManager = FindAnyObjectByType<GameStateManager>();
        }

        private void OnEnable()
        {
            if (statsManager != null)
            {
                statsManager.OnEnergyChanged += HandleEnergyChanged;
                statsManager.OnRespectChanged += HandleRespectChanged;
                statsManager.OnTrashChanged += HandleTrashChanged;
                statsManager.OnReprimandsChanged += HandleReprimandsChanged;
            }

            if (dayTimeController != null)
            {
                dayTimeController.OnTimeChanged += HandleTimeChanged;
                dayTimeController.OnCurfewWarning += HandleCurfewWarning;
                dayTimeController.OnCurfewStarted += HandleCurfewStarted;
                dayTimeController.OnDayAdvanced += HandleDayAdvanced;
            }

            if (quickInventory != null)
            {
                quickInventory.OnInventoryChanged += RefreshInventoryUI;
                quickInventory.OnInventoryNotification += ShowToast;
            }

            if (gameStateManager != null)
                gameStateManager.OnStateChanged += HandleGameStateChanged;

            if (resumeButton != null)
                resumeButton.onClick.AddListener(OnResumeClicked);
            if (restartButton != null)
                restartButton.onClick.AddListener(OnRestartClicked);
        }

        private void OnDisable()
        {
            if (statsManager != null)
            {
                statsManager.OnEnergyChanged -= HandleEnergyChanged;
                statsManager.OnRespectChanged -= HandleRespectChanged;
                statsManager.OnTrashChanged -= HandleTrashChanged;
                statsManager.OnReprimandsChanged -= HandleReprimandsChanged;
            }

            if (dayTimeController != null)
            {
                dayTimeController.OnTimeChanged -= HandleTimeChanged;
                dayTimeController.OnCurfewWarning -= HandleCurfewWarning;
                dayTimeController.OnCurfewStarted -= HandleCurfewStarted;
                dayTimeController.OnDayAdvanced -= HandleDayAdvanced;
            }

            if (quickInventory != null)
            {
                quickInventory.OnInventoryChanged -= RefreshInventoryUI;
                quickInventory.OnInventoryNotification -= ShowToast;
            }

            if (gameStateManager != null)
                gameStateManager.OnStateChanged -= HandleGameStateChanged;

            if (resumeButton != null)
                resumeButton.onClick.RemoveListener(OnResumeClicked);
            if (restartButton != null)
                restartButton.onClick.RemoveListener(OnRestartClicked);
        }

        private void Start()
        {
            if (statsManager != null)
            {
                HandleEnergyChanged(statsManager.Energy, statsManager.Config != null ? statsManager.Config.maxEnergy : 100f);
                HandleRespectChanged(statsManager.Respect);
                HandleTrashChanged(statsManager.Trash, statsManager.Config != null ? statsManager.Config.maxTrash : 100f);
                HandleReprimandsChanged(statsManager.Reprimands, statsManager.Config != null ? statsManager.Config.maxReprimands : 3);
            }

            if (dayTimeController != null)
                HandleTimeChanged(dayTimeController.CurrentDay, dayTimeController.CurrentHour, dayTimeController.FormattedDayAndTime);

            RefreshInventoryUI();
            if (toastBannerRoot != null) toastBannerRoot.SetActive(false);
            if (stateOverlayRoot != null) stateOverlayRoot.SetActive(false);
            if (holdProgressContainer != null) holdProgressContainer.SetActive(false);

            ShowToast("Кімната №104 — Гуртожиток №7. Дослідіть поверх і підвал! [WASD] Рух • [E] Дія • [G/ПКМ] Викинути/Кинути");
        }

        private void Update()
        {
            UpdateEnergyPulse();
            UpdateInteractionUI();
            UpdateStealthIndicator();
            UpdateCarryCoolingText();
            UpdateToastTimer();
        }

        private void UpdateEnergyPulse()
        {
            if (energyFillImage == null || statsManager == null) return;
            if (statsManager.IsLowEnergy)
            {
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5.5f));
                Color c = EnergyLowColor;
                c.a = pulse;
                energyFillImage.color = c;
            }
            else
            {
                energyFillImage.color = EnergyNormalColor;
            }
        }

        private void UpdateInteractionUI()
        {
            if (playerInteractor == null) return;

            bool hasPrompt = !string.IsNullOrEmpty(playerInteractor.CurrentPrompt);
            if (interactionPromptText != null)
            {
                interactionPromptText.text = playerInteractor.CurrentPrompt;
                interactionPromptText.color = playerInteractor.CanInteractCurrent
                    ? Color.white
                    : new Color(0.95f, 0.55f, 0.45f, 1f);
            }

            if (crosshairDot != null)
            {
                float scale = hasPrompt && playerInteractor.CanInteractCurrent ? 1.45f : 1f;
                crosshairDot.rectTransform.localScale = Vector3.one * scale;
                crosshairDot.color = hasPrompt && playerInteractor.CanInteractCurrent
                    ? new Color(1f, 0.85f, 0.35f, 0.95f)
                    : new Color(1f, 1f, 1f, 0.72f);
            }

            if (holdProgressContainer != null && holdProgressBarFill != null)
            {
                holdProgressContainer.SetActive(playerInteractor.IsHolding);
                if (playerInteractor.IsHolding)
                    holdProgressBarFill.fillAmount = playerInteractor.HoldProgress;
            }

            if (controlsHintText != null && quickInventory != null)
            {
                ItemDefinition active = quickInventory.GetActiveItemDefinition();
                if (active == null)
                {
                    controlsHintText.text = "";
                }
                else
                {
                    string useLabel = active.HasTag(ItemTag.Food) ? "[ЛКМ] Вжити  " : "[ЛКМ] Застосувати  ";
                    controlsHintText.text = $"{useLabel}[G] Покласти  [ПКМ] Кинути";
                }
            }
        }

        private void UpdateStealthIndicator()
        {
            if (stealthBadgeText == null || playerController == null) return;
            if (playerController.IsHidden)
                stealthBadgeText.text = "● СХОВАНКА (натисніть [E] щоб вийти)";
            else if (playerController.IsCrouching)
                stealthBadgeText.text = "● СТЕЛС (тихий крок)";
            else if (playerController.IsSprinting)
                stealthBadgeText.text = "▲ СПРИНТ (витрата Енергії)";
            else
                stealthBadgeText.text = "";
        }

        private void UpdateCarryCoolingText()
        {
            if (quickInventory == null || carryBannerRoot == null || carryBannerText == null) return;
            if (!quickInventory.IsCarryingTwoHanded) return;

            var carried = quickInventory.CarriedTwoHandedItem;
            if (carried != null && carried.Definition != null)
            {
                string suffix = carried.IsCooling
                    ? $" — охолоне за {Mathf.CeilToInt(carried.RemainingCoolingTime)}с"
                    : " — [G] поставити / [ПКМ] кинути";
                carryBannerText.text = $"В РУКАХ: {carried.Definition.displayName}{suffix}";
            }
        }

        private void UpdateToastTimer()
        {
            if (_toastTimer <= 0f) return;
            _toastTimer -= Time.unscaledDeltaTime;
            if (_toastTimer <= 0f && toastBannerRoot != null)
                toastBannerRoot.SetActive(false);
        }

        private void HandleEnergyChanged(float current, float max)
        {
            float pct = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (energyFillImage != null) energyFillImage.fillAmount = pct;
            if (energyValueText != null) energyValueText.text = $"ЕНЕРГІЯ: {Mathf.RoundToInt(current)}%";
        }

        private void HandleRespectChanged(float current)
        {
            if (respectValueText != null) respectValueText.text = $"РЕСПЕКТ: {Mathf.RoundToInt(current)}";
        }

        private void HandleTrashChanged(float current, float max)
        {
            float pct = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (trashFillImage != null) trashFillImage.fillAmount = pct;
            if (trashValueText != null) trashValueText.text = $"ТРЕШ: {Mathf.RoundToInt(current)}%";
        }

        private void HandleReprimandsChanged(int count, int max)
        {
            if (reprimandStampImages == null) return;
            for (int i = 0; i < reprimandStampImages.Length; i++)
            {
                if (reprimandStampImages[i] != null)
                    reprimandStampImages[i].color = i < count ? StampActiveColor : StampInactiveColor;
            }
        }

        private void HandleTimeChanged(int day, float hour, string formatted)
        {
            if (clockText == null) return;
            clockText.text = formatted;
            if (dayTimeController != null && dayTimeController.IsCurfewActive)
                clockText.color = ClockCurfewColor;
            else if (dayTimeController != null && dayTimeController.IsCurfewWarningTriggered)
                clockText.color = ClockWarningColor;
            else
                clockText.color = ClockNormalColor;
        }

        private void HandleCurfewWarning()
        {
            ShowToast("21:30 — УВАГА! Зінаїда Петрівна готується до вечірнього обходу о 22:00!");
        }

        private void HandleCurfewStarted()
        {
            ShowToast("22:00 — ВІДБІЙ! Поверніться в кімнату №104 та лягайте спати.");
        }

        private void HandleDayAdvanced(int newDay, string message)
        {
            ShowToast(message);
        }

        private void RefreshInventoryUI()
        {
            if (quickInventory == null) return;

            bool carryingTwoHanded = quickInventory.IsCarryingTwoHanded;
            if (carryBannerRoot != null)
                carryBannerRoot.SetActive(carryingTwoHanded);

            if (carryingTwoHanded)
                UpdateCarryCoolingText();

            if (slotBackgrounds != null && slotItemLabels != null)
            {
                int count = Mathf.Min(slotBackgrounds.Length, slotItemLabels.Length);
                for (int i = 0; i < count; i++)
                {
                    bool selected = !carryingTwoHanded && i == quickInventory.ActiveSlotIndex;
                    if (slotBackgrounds[i] != null)
                        slotBackgrounds[i].color = selected ? SlotSelectedColor : SlotNormalColor;

                    ItemDefinition itemDef = quickInventory.GetSlotItemDefinition(i);
                    if (slotItemLabels[i] != null)
                        slotItemLabels[i].text = itemDef != null ? $"[{i + 1}]\n{itemDef.displayName}" : $"[{i + 1}]\n—";
                }
            }
        }

        private void ShowToast(string message)
        {
            if (toastBannerRoot == null || toastBannerText == null) return;
            toastBannerText.text = message;
            toastBannerRoot.SetActive(true);
            _toastTimer = 5.5f;
        }

        private void HandleGameStateChanged(GameState state, string message)
        {
            if (stateOverlayRoot == null) return;

            bool showOverlay = state != GameState.Playing;
            stateOverlayRoot.SetActive(showOverlay);
            if (!showOverlay) return;

            if (stateOverlayTitle != null)
            {
                stateOverlayTitle.text = state switch
                {
                    GameState.Paused => "ПАУЗА",
                    GameState.GameOver => "НАКАЗ ПРО ВИСЕЛЕННЯ",
                    GameState.Victory => "ТИЖДЕНЬ ЗАКРИТО!",
                    _ => ""
                };
            }

            if (stateOverlayBody != null)
                stateOverlayBody.text = message;

            if (resumeButton != null)
                resumeButton.gameObject.SetActive(state == GameState.Paused);
        }

        private void OnResumeClicked() => gameStateManager?.ResumeGame();
        private void OnRestartClicked() => gameStateManager?.RestartScene();
    }
}
