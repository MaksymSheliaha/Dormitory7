using Dormitory7.Audio;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Features.Gym;
using Dormitory7.Features.Quests;
using Dormitory7.NPC;
using Dormitory7.Player;
using TMPro;
using UnityEngine;

namespace Dormitory7.UI
{
    public struct QteDisplayState
    {
        public string Title;
        public string Instruction;
        public float ProgressNormalized;     // 0..1 overall progress (repair or reps)
        public bool ShowTimingBar;
        public float TimingCursor;           // 0..1 needle position
        public float SweetSpotMin;           // 0..1 green zone start
        public float SweetSpotMax;           // 0..1 green zone end
        public bool ShowBalanceBar;
        public float BalanceValue;           // -1..+1 (0 is centered safe)
        public float BalanceSafeHalfWidth;   // e.g., 0.35
    }

    public class HudController : MonoBehaviour
    {
        public static HudController Instance { get; private set; }

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
        [SerializeField] private TextMeshProUGUI attributesText;
        [SerializeField] private TextMeshProUGUI stealthBadgeText;

        [Header("Center Crosshair & Prompts")]
        [SerializeField] private UnityEngine.UI.Image crosshairDot;
        [SerializeField] private TextMeshProUGUI interactionPromptText;
        [SerializeField] private TextMeshProUGUI controlsHintText;
        [SerializeField] private UnityEngine.UI.Image holdProgressBarFill;
        [SerializeField] private GameObject holdProgressContainer;

        [Header("Center QTE Indicator (Repairs / Gym)")]
        [SerializeField] private GameObject qtePanelRoot;
        [SerializeField] private TextMeshProUGUI qteTitleText;
        [SerializeField] private TextMeshProUGUI qteInstructionText;
        [SerializeField] private UnityEngine.UI.Image qteProgressFill;
        [SerializeField] private GameObject qteTimingBarRoot;
        [SerializeField] private RectTransform qteSweetSpotRect;
        [SerializeField] private RectTransform qteTimingCursorRect;
        [SerializeField] private GameObject qteBalanceBarRoot;
        [SerializeField] private RectTransform qteBalanceSafeRect;
        [SerializeField] private RectTransform qteBalanceCursorRect;

        [Header("Bottom 4-Slot Quick Inventory")]
        [SerializeField] private UnityEngine.UI.Image[] slotBackgrounds;
        [SerializeField] private TextMeshProUGUI[] slotItemLabels;
        [SerializeField] private GameObject carryBannerRoot;
        [SerializeField] private TextMeshProUGUI carryBannerText;

        [Header("UX Feedback Overlays (Danger / Reprimand Stamp / Gym Pulse)")]
        [SerializeField] private UnityEngine.UI.Image dangerVignetteImage;
        [SerializeField] private UnityEngine.UI.Image gymPulseImage;
        [SerializeField] private GameObject reprimandStampRoot;
        [SerializeField] private TextMeshProUGUI reprimandStampText;
        [SerializeField] private CanvasGroup reprimandStampCanvasGroup;

        [Header("Toast & State Overlay")]
        [SerializeField] private GameObject gameplayHudGroup;
        [SerializeField] private TextMeshProUGUI phoneHintText;
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
        private bool _isQteVisible;
        private bool _shownWelcomeToast;
        private QuestManager _questManager;
        private KomendantAI _komendant;
        private int _lastReprimandCount;
        private float _stampAnimTimer;
        private const float StampTotalDuration = 2.6f;

        public void BindPhoneHint(GameObject hudGroup, TextMeshProUGUI phoneHint)
        {
            gameplayHudGroup = hudGroup;
            phoneHintText = phoneHint;
        }

        public void BindFeedbackOverlays(
            UnityEngine.UI.Image dangerVig,
            UnityEngine.UI.Image gymPulse,
            GameObject stampRoot,
            TextMeshProUGUI stampText,
            CanvasGroup stampGroup)
        {
            dangerVignetteImage = dangerVig;
            gymPulseImage = gymPulse;
            reprimandStampRoot = stampRoot;
            reprimandStampText = stampText;
            reprimandStampCanvasGroup = stampGroup;
        }

        public bool IsReprimandStampVisible => reprimandStampRoot != null && reprimandStampRoot.activeSelf;

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
            TextMeshProUGUI stealthBadge,
            TextMeshProUGUI attrsText = null)
        {
            energyFillImage = energyFill;
            energyValueText = energyText;
            respectValueText = respectText;
            trashFillImage = trashFill;
            trashValueText = trashText;
            reprimandStampImages = stamps;
            clockText = clock;
            stealthBadgeText = stealthBadge;
            if (attrsText != null) attributesText = attrsText;
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

        public void BindQte(
            GameObject root,
            TextMeshProUGUI title,
            TextMeshProUGUI instruction,
            UnityEngine.UI.Image progressFill,
            GameObject timingRoot,
            RectTransform sweetSpot,
            RectTransform timingCursor,
            GameObject balanceRoot,
            RectTransform balanceSafe,
            RectTransform balanceCursor)
        {
            qtePanelRoot = root;
            qteTitleText = title;
            qteInstructionText = instruction;
            qteProgressFill = progressFill;
            qteTimingBarRoot = timingRoot;
            qteSweetSpotRect = sweetSpot;
            qteTimingCursorRect = timingCursor;
            qteBalanceBarRoot = balanceRoot;
            qteBalanceSafeRect = balanceSafe;
            qteBalanceCursorRect = balanceCursor;
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
            Instance = this;
            if (statsManager == null) statsManager = FindAnyObjectByType<DormStatsManager>();
            if (dayTimeController == null) dayTimeController = FindAnyObjectByType<DayTimeController>();
            if (quickInventory == null) quickInventory = FindAnyObjectByType<QuickInventory>();
            if (playerInteractor == null) playerInteractor = FindAnyObjectByType<PlayerInteractor>();
            if (playerController == null) playerController = FindAnyObjectByType<FirstPersonController>();
            if (gameStateManager == null) gameStateManager = FindAnyObjectByType<GameStateManager>();
            _questManager = FindAnyObjectByType<QuestManager>();
            _komendant = FindAnyObjectByType<KomendantAI>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (statsManager != null)
            {
                statsManager.OnEnergyChanged += HandleEnergyChanged;
                statsManager.OnRespectChanged += HandleRespectChanged;
                statsManager.OnTrashChanged += HandleTrashChanged;
                statsManager.OnReprimandsChanged += HandleReprimandsChanged;
                statsManager.OnAttributesChanged += HandleAttributesChanged;
                statsManager.OnGymBuffChanged += HandleGymBuffChanged;
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

            if (_questManager != null)
                _questManager.OnChatChanged += RefreshPhoneHint;

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
                statsManager.OnAttributesChanged -= HandleAttributesChanged;
                statsManager.OnGymBuffChanged -= HandleGymBuffChanged;
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

            if (_questManager != null)
                _questManager.OnChatChanged -= RefreshPhoneHint;

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
                RefreshAttributesText();
            }

            if (dayTimeController != null)
                HandleTimeChanged(dayTimeController.CurrentDay, dayTimeController.CurrentHour, dayTimeController.FormattedDayAndTime);

            RefreshInventoryUI();
            RefreshPhoneHint();
            if (toastBannerRoot != null) toastBannerRoot.SetActive(false);
            if (stateOverlayRoot != null) stateOverlayRoot.SetActive(false);
            if (holdProgressContainer != null) holdProgressContainer.SetActive(false);
            if (qtePanelRoot != null) qtePanelRoot.SetActive(false);
            if (reprimandStampRoot != null) reprimandStampRoot.SetActive(false);
            if (dangerVignetteImage != null) dangerVignetteImage.color = new Color(0.75f, 0.06f, 0.06f, 0f);
            if (gymPulseImage != null) gymPulseImage.color = new Color(1.0f, 0.42f, 0.12f, 0f);
            _lastReprimandCount = statsManager != null ? statsManager.Reprimands : 0;

            if (gameStateManager != null)
                HandleGameStateChanged(gameStateManager.CurrentState, gameStateManager.EndStateMessage);
        }

        private void RefreshPhoneHint()
        {
            if (phoneHintText == null) return;
            int unread = _questManager != null ? _questManager.UnreadChatCount : 0;
            phoneHintText.text = unread > 0
                ? $"[Tab] Смартфон  <color=#FF9F43>({unread} нов.)</color>"
                : "[Tab] Смартфон • Задачі та Чат";
        }

        private void Update()
        {
            UpdateEnergyPulse();
            UpdateInteractionUI();
            UpdateStealthIndicator();
            UpdateCarryCoolingText();
            UpdateToastTimer();
            UpdateDangerAndGymOverlays();
            UpdateReprimandStampAnim();
        }

        private void UpdateDangerAndGymOverlays()
        {
            // 1. Komendant approach danger vignette pulsing in rhythm with heartbeat
            if (dangerVignetteImage != null)
            {
                float danger = _komendant != null ? _komendant.DangerLevel : 0f;
                bool isPlaying = gameStateManager == null || gameStateManager.CurrentState == GameState.Playing;
                if (isPlaying && danger > 0.08f)
                {
                    float freq = Mathf.Lerp(2.4f, 6.2f, danger);
                    float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * freq));
                    float alpha = Mathf.Clamp01(danger * 0.68f * pulse);
                    dangerVignetteImage.color = new Color(0.68f, 0.04f, 0.05f, alpha);
                }
                else
                {
                    Color c = dangerVignetteImage.color;
                    if (c.a > 0.001f)
                        dangerVignetteImage.color = new Color(c.r, c.g, c.b, Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 2.5f));
                }
            }

            // 2. Gym final-reps screen exertion pulse
            if (gymPulseImage != null)
            {
                float exertion = GymEquipment.ActiveExertionPulse;
                if (exertion > 0.05f)
                {
                    float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 7.5f));
                    float alpha = Mathf.Clamp01(exertion * 0.45f * pulse);
                    gymPulseImage.color = new Color(0.98f, 0.38f, 0.10f, alpha);
                }
                else
                {
                    Color c = gymPulseImage.color;
                    if (c.a > 0.001f)
                        gymPulseImage.color = new Color(c.r, c.g, c.b, Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 3.0f));
                }
            }
        }

        public void TriggerReprimandStamp(int currentCount, int maxCount)
        {
            AudioManager.Instance?.PlayReprimandStamp();
            if (reprimandStampRoot == null) return;

            reprimandStampRoot.SetActive(true);
            _stampAnimTimer = StampTotalDuration;
            if (reprimandStampText != null)
                reprimandStampText.text = $"ДОГАНА\n№{currentCount} / {maxCount}";
            if (reprimandStampCanvasGroup != null)
                reprimandStampCanvasGroup.alpha = 1f;
            reprimandStampRoot.transform.localScale = Vector3.one * 2.2f;
        }

        private void UpdateReprimandStampAnim()
        {
            if (_stampAnimTimer <= 0f || reprimandStampRoot == null || !reprimandStampRoot.activeSelf)
                return;

            _stampAnimTimer -= Time.unscaledDeltaTime;
            float elapsed = StampTotalDuration - _stampAnimTimer;

            // Punch slam from 2.2x -> 1.0x in first 0.18s
            if (elapsed < 0.18f)
            {
                float t = elapsed / 0.18f;
                float scale = Mathf.Lerp(2.2f, 1.0f, t * t);
                reprimandStampRoot.transform.localScale = Vector3.one * scale;
            }
            else
            {
                reprimandStampRoot.transform.localScale = Vector3.one;
            }

            // Fade out cleanly in final 0.55s
            if (reprimandStampCanvasGroup != null)
            {
                reprimandStampCanvasGroup.alpha = _stampAnimTimer < 0.55f
                    ? Mathf.Clamp01(_stampAnimTimer / 0.55f)
                    : 1f;
            }

            if (_stampAnimTimer <= 0f)
                reprimandStampRoot.SetActive(false);
        }

        public void ShowQte(QteDisplayState state)
        {
            if (qtePanelRoot == null) return;
            _isQteVisible = true;
            qtePanelRoot.SetActive(true);

            if (qteTitleText != null) qteTitleText.text = state.Title ?? "";
            if (qteInstructionText != null) qteInstructionText.text = state.Instruction ?? "";
            if (qteProgressFill != null) qteProgressFill.fillAmount = Mathf.Clamp01(state.ProgressNormalized);

            if (qteTimingBarRoot != null)
            {
                qteTimingBarRoot.SetActive(state.ShowTimingBar);
                if (state.ShowTimingBar)
                {
                    if (qteSweetSpotRect != null)
                    {
                        qteSweetSpotRect.anchorMin = new Vector2(Mathf.Clamp01(state.SweetSpotMin), 0f);
                        qteSweetSpotRect.anchorMax = new Vector2(Mathf.Clamp01(state.SweetSpotMax), 1f);
                        qteSweetSpotRect.offsetMin = Vector2.zero;
                        qteSweetSpotRect.offsetMax = Vector2.zero;
                    }
                    if (qteTimingCursorRect != null)
                    {
                        float t = Mathf.Clamp01(state.TimingCursor);
                        qteTimingCursorRect.anchorMin = new Vector2(t, 0f);
                        qteTimingCursorRect.anchorMax = new Vector2(t, 1f);
                        qteTimingCursorRect.anchoredPosition = Vector2.zero;
                    }
                }
            }

            if (qteBalanceBarRoot != null)
            {
                qteBalanceBarRoot.SetActive(state.ShowBalanceBar);
                if (state.ShowBalanceBar)
                {
                    float half = Mathf.Clamp(state.BalanceSafeHalfWidth, 0.08f, 0.45f);
                    if (qteBalanceSafeRect != null)
                    {
                        qteBalanceSafeRect.anchorMin = new Vector2(0.5f - half, 0f);
                        qteBalanceSafeRect.anchorMax = new Vector2(0.5f + half, 1f);
                        qteBalanceSafeRect.offsetMin = Vector2.zero;
                        qteBalanceSafeRect.offsetMax = Vector2.zero;
                    }
                    if (qteBalanceCursorRect != null)
                    {
                        float norm = Mathf.Clamp01(0.5f + state.BalanceValue * 0.5f);
                        qteBalanceCursorRect.anchorMin = new Vector2(norm, 0f);
                        qteBalanceCursorRect.anchorMax = new Vector2(norm, 1f);
                        qteBalanceCursorRect.anchoredPosition = Vector2.zero;
                    }
                }
            }
        }

        public void HideQte()
        {
            _isQteVisible = false;
            if (qtePanelRoot != null)
                qtePanelRoot.SetActive(false);
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

            if (_isQteVisible)
            {
                if (interactionPromptText != null) interactionPromptText.text = "";
                return;
            }

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
                    string useLabel = active.HasTag(ItemTag.Food)
                        ? "[ЛКМ] Вжити  "
                        : (active.itemId == "dichlorvos" || active.itemId == "mop" ? "[Утрим. ЛКМ] Дія  " : "[ЛКМ] Удар/Дія  ");
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

        private void HandleAttributesChanged(int strength, int stamina) => RefreshAttributesText();
        private void HandleGymBuffChanged(bool active) => RefreshAttributesText();

        private void RefreshAttributesText()
        {
            if (attributesText == null || statsManager == null) return;
            string buff = statsManager.HasGymBuff ? "  [БАФ КАВИ]" : "";
            attributesText.text = $"СИЛА: {statsManager.Strength}/5  •  ВИТР.: {statsManager.Stamina}/5{buff}";
        }

        private void HandleTrashChanged(float current, float max)
        {
            float pct = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (trashFillImage != null) trashFillImage.fillAmount = pct;
            if (trashValueText != null) trashValueText.text = $"ТРЕШ: {Mathf.RoundToInt(current)}%";
        }

        private void HandleReprimandsChanged(int count, int max)
        {
            if (reprimandStampImages != null)
            {
                for (int i = 0; i < reprimandStampImages.Length; i++)
                {
                    if (reprimandStampImages[i] != null)
                        reprimandStampImages[i].color = i < count ? StampActiveColor : StampInactiveColor;
                }
            }

            if (count > _lastReprimandCount)
                TriggerReprimandStamp(count, max);
            _lastReprimandCount = count;
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
            ShowToast("21:30 — Зіна вийшла на полювання! Приберіть поверх і сховайтесь до 22:00!");
        }

        private void HandleCurfewStarted()
        {
            ShowToast("22:00 — ВІДБІЙ! Комендантша Зінаїда Петрівна почала вечірній обхід!");
        }

        private void HandleDayAdvanced(int newDay, string message) => ShowToast(message);

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

        public void ShowToast(string message)
        {
            if (toastBannerRoot == null || toastBannerText == null) return;
            toastBannerText.text = message;
            toastBannerRoot.SetActive(true);
            _toastTimer = 5.5f;
        }

        private void HandleGameStateChanged(GameState state, string message)
        {
            bool showGameplayHud = state == GameState.Playing || state == GameState.Smartphone || state == GameState.Paused;
            if (gameplayHudGroup != null)
                gameplayHudGroup.SetActive(showGameplayHud);

            if (state == GameState.Playing && !_shownWelcomeToast)
            {
                _shownWelcomeToast = true;
                ShowToast("День 1 — Гуртожиток №7! Натисніть [Tab], щоб відкрити Смартфон із задачами та чатом поверху.");
            }

            // Defer full-screen overlays to GameFlowUI when present
            if (GameFlowUI.Instance != null)
            {
                if (stateOverlayRoot != null)
                    stateOverlayRoot.SetActive(false);
                return;
            }

            if (stateOverlayRoot == null) return;

            bool showOverlay = state != GameState.Playing && state != GameState.Smartphone;
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
