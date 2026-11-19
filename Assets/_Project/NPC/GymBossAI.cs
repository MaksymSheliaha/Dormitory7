using System;
using Dormitory7.Audio;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Features.Gym;
using Dormitory7.Features.Quests;
using Dormitory7.Interaction;
using Dormitory7.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dormitory7.NPC
{
    public enum GymBossMood
    {
        Neutral = 0,
        Pleased = 1,
        Angry = 2
    }

    public enum GymBossScheduleSlot
    {
        MorningShower = 0,
        MorningWashroom = 1,
        MiddayKitchen = 2,
        AfternoonGym = 3
    }

    public class GymBossAI : BaseNpcAgent, IInteractable
    {
        [Header("Gym Boss Config")]
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Renderer moodIndicatorRenderer;
        [SerializeField] private TextMeshPro overheadLabel;

        public GymBossMood Mood { get; private set; } = GymBossMood.Neutral;
        public GymBossScheduleSlot CurrentSlot { get; private set; } = GymBossScheduleSlot.MorningShower;
        public bool GreetedToday { get; private set; }
        public int GiftsGivenToday { get; private set; }
        public bool HasCompletedFridayChallenge { get; private set; }
        public bool IsArmWrestlingActive { get; private set; }
        public float ArmWrestleMeter { get; private set; } // 0 = Boss wins, 0.5 = center, 1 = Player wins
        public int LastArmWrestleDay { get; private set; } = -10;

        public bool IsFridayClimax => dayTimeController != null && dayTimeController.CurrentDay >= 5;
        public bool IsFridayChallengeAvailable => IsFridayClimax && !HasCompletedFridayChallenge;

        public bool IsArmWrestleAvailable
        {
            get
            {
                if (!GreetedToday || IsArmWrestlingActive) return false;
                int curDay = dayTimeController != null ? dayTimeController.CurrentDay : 1;
                int cd = balanceConfig != null ? Mathf.Max(1, balanceConfig.armWrestleCooldownDays) : 2;
                return (curDay - LastArmWrestleDay) >= cd;
            }
        }

        public bool IsInMorningBonusWindow =>
            CurrentSlot == GymBossScheduleSlot.MorningShower ||
            CurrentSlot == GymBossScheduleSlot.MorningWashroom;

        public event Action<GymBossMood> OnMoodChanged;
        public event Action<bool, float> OnPlayerGreeted;
        public event Action<ItemDefinition, float> OnGiftReceived;
        public event Action OnFridayChallengeWon;
        public event Action<bool> OnArmWrestleFinished;

        private bool _scheduleInitialized;
        private float _handshakeAnimTimer;
        private Transform _playerTransform;
        private PlayerContext _armWrestleCtx;
        private float _armWrestleNeedle;
        private float _armWrestleNeedleDir = 1f;
        private InputAction _armPushAction;
        private InputAction _armCancelAction;

        private void OnEnable()
        {
            GymEquipment.OnAnyWorkoutCompleted += HandleWorkoutCompleted;
        }

        private void OnDisable()
        {
            if (IsArmWrestlingActive)
                StopArmWrestleCleanly();
            GymEquipment.OnAnyWorkoutCompleted -= HandleWorkoutCompleted;
        }

        private void OnDestroy()
        {
            _armPushAction?.Dispose();
            _armCancelAction?.Dispose();
        }

        private void HandleWorkoutCompleted(GymStationType station, bool wasBossChallenge)
        {
            if (wasBossChallenge && !HasCompletedFridayChallenge)
            {
                HasCompletedFridayChallenge = true;
                SetMood(GymBossMood.Pleased);
                _handshakeAnimTimer = 1.0f;
                OnFridayChallengeWon?.Invoke();
            }
        }

        private static readonly Color MoodNeutralColor = new Color(0.85f, 0.78f, 0.45f, 1f);
        private static readonly Color MoodPleasedColor = new Color(0.28f, 0.82f, 0.38f, 1f);
        private static readonly Color MoodAngryColor = new Color(0.88f, 0.22f, 0.18f, 1f);

        public void Configure(
            BalanceConfig config,
            DormStatsManager stats,
            Transform armPivot,
            Renderer moodRenderer,
            TextMeshPro label)
        {
            balanceConfig = config;
            statsManager = stats;
            rightArmPivot = armPivot;
            moodIndicatorRenderer = moodRenderer;
            overheadLabel = label;
        }

        protected override void Awake()
        {
            base.Awake();
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            walkSpeed = 2.4f;
            if (navAgent != null)
                navAgent.speed = walkSpeed;

            _armPushAction = new InputAction("ArmWrestlePush", InputActionType.Button);
            _armPushAction.AddBinding("<Keyboard>/space");
            _armPushAction.AddBinding("<Mouse>/leftButton");
            _armPushAction.AddBinding("<Gamepad>/buttonSouth");

            _armCancelAction = new InputAction("ArmWrestleCancel", InputActionType.Button);
            _armCancelAction.AddBinding("<Keyboard>/q");
            _armCancelAction.AddBinding("<Gamepad>/buttonEast");
        }

        protected override void Start()
        {
            base.Start();
            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();

            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
                _playerTransform = playerGo.transform;

            float startHour = dayTimeController != null ? dayTimeController.CurrentHour : 8f;
            EvaluateScheduleForHour(startHour, warpImmediately: true);
            UpdateMoodVisuals();
        }

        protected override void Update()
        {
            base.Update();

            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            if (IsArmWrestlingActive)
            {
                UpdateArmWrestlingQte();
                return;
            }

            // Face player politely when stopped nearby
            if (_playerTransform != null && HasArrived)
            {
                Vector3 toPlayer = _playerTransform.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude < 16f && toPlayer.sqrMagnitude > 0.04f)
                {
                    Quaternion lookRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
                }
            }

            // Keep overhead label readable toward camera
            if (overheadLabel != null && Camera.main != null)
            {
                Vector3 dir = overheadLabel.transform.position - Camera.main.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    overheadLabel.transform.rotation = Quaternion.LookRotation(dir);
            }

            // Simple handshake arm-pump animation
            if (rightArmPivot != null)
            {
                if (_handshakeAnimTimer > 0f)
                {
                    _handshakeAnimTimer = Mathf.Max(0f, _handshakeAnimTimer - Time.deltaTime);
                    float swing = Mathf.Sin(_handshakeAnimTimer * 18f) * 28f;
                    rightArmPivot.localRotation = Quaternion.Euler(-55f + swing, 0f, 0f);
                }
                else
                {
                    rightArmPivot.localRotation = Quaternion.Slerp(rightArmPivot.localRotation, Quaternion.identity, Time.deltaTime * 8f);
                }
            }
        }

        public void EvaluateScheduleForHour(float hour, bool warpImmediately = false)
        {
            GymBossScheduleSlot targetSlot = ResolveSlotForHour(hour);
            if (_scheduleInitialized && targetSlot == CurrentSlot && !warpImmediately)
                return;

            _scheduleInitialized = true;
            CurrentSlot = targetSlot;
            NpcZone zone = SlotToZone(targetSlot);

            if (warpImmediately)
                WarpToZone(zone);
            else
                MoveToZone(zone, walkSpeed);
        }

        private GymBossScheduleSlot ResolveSlotForHour(float hour)
        {
            if (balanceConfig == null)
                return GymBossScheduleSlot.AfternoonGym;

            if (hour < balanceConfig.gymBossShowerEndHour)
                return GymBossScheduleSlot.MorningShower;
            if (hour < balanceConfig.gymBossWashroomEndHour)
                return GymBossScheduleSlot.MorningWashroom;
            if (hour < balanceConfig.gymBossKitchenEndHour)
                return GymBossScheduleSlot.MiddayKitchen;
            return GymBossScheduleSlot.AfternoonGym;
        }

        private static NpcZone SlotToZone(GymBossScheduleSlot slot)
        {
            return slot switch
            {
                GymBossScheduleSlot.MorningShower => NpcZone.BasementShower,
                GymBossScheduleSlot.MorningWashroom => NpcZone.WashroomLeft,
                GymBossScheduleSlot.MiddayKitchen => NpcZone.KitchenLeft,
                _ => NpcZone.BasementGym
            };
        }

        protected override void OnGameTimeUpdated(int day, float hour)
        {
            EvaluateScheduleForHour(hour, warpImmediately: false);
        }

        protected override void OnNewDayStarted(int newDay)
        {
            ProcessEndOfDayEvaluation();
            float morningHour = dayTimeController != null ? dayTimeController.CurrentHour : 8f;
            EvaluateScheduleForHour(morningHour, warpImmediately: true);
        }

        public void ProcessEndOfDayEvaluation()
        {
            if (!GreetedToday)
            {
                SetMood(GymBossMood.Angry);
                float penalty = balanceConfig != null ? balanceConfig.gymBossIgnoreRespectPenalty : 8f;
                statsManager?.ModifyRespect(-penalty);
                HudController.Instance?.ShowToast(
                    $"Бос качалки розлючений: ти проігнорував привітання! (-{Mathf.RoundToInt(penalty)} Респекту)");
            }
            else
            {
                SetMood(GymBossMood.Neutral);
            }

            GreetedToday = false;
            GiftsGivenToday = 0;
        }

        private void SetMood(GymBossMood newMood)
        {
            if (Mood == newMood)
            {
                UpdateMoodVisuals();
                return;
            }
            Mood = newMood;
            UpdateMoodVisuals();
            OnMoodChanged?.Invoke(Mood);
        }

        private void UpdateMoodVisuals()
        {
            Color c = Mood switch
            {
                GymBossMood.Pleased => MoodPleasedColor,
                GymBossMood.Angry => MoodAngryColor,
                _ => MoodNeutralColor
            };

            if (moodIndicatorRenderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                moodIndicatorRenderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                moodIndicatorRenderer.SetPropertyBlock(mpb);
            }

            if (overheadLabel != null)
            {
                string moodTag = Mood switch
                {
                    GymBossMood.Pleased => "Задоволений",
                    GymBossMood.Angry => "Розлючений",
                    _ => "Нейтральний"
                };
                overheadLabel.text = $"БОС КАЧАЛКИ\n({moodTag})";
                overheadLabel.color = c;
            }
        }

        public string GetPrompt(PlayerContext context)
        {
            ItemDefinition active = context.ActiveItem;
            if (active != null && active.IsBossGift)
            {
                int bonus = Mathf.RoundToInt(Mathf.Max(5f, active.respectGiftBonus));
                return $"[E] Подарувати Босу: {active.displayName} (+{bonus} Респекту)";
            }

            if (!GreetedToday)
            {
                if (IsInMorningBonusWindow)
                {
                    int x2 = Mathf.RoundToInt(balanceConfig.gymBossGreetRespect * balanceConfig.gymBossMorningShowerMultiplier);
                    return $"[E] Чоловіче рукостискання з Босом (РАНКОВИЙ x2 БОНУС: +{x2} Респекту!)";
                }
                int normal = Mathf.RoundToInt(balanceConfig.gymBossGreetRespect);
                return $"[E] Привітатися з Босом качалки за руку (+{normal} Респекту)";
            }

            if (IsFridayChallengeAvailable)
            {
                if (context.Stats != null && !context.Stats.CanTrainInGym)
                    return "П'ятничний виклик Боса: потрібно мінімум 15% Енергії для фінального жиму!";
                return "[E] П'ЯТНИЧНИЙ ВИКЛИК БОСА: Фінальний підхід на жимі (+18 Респекту!)";
            }

            if (IsArmWrestleAvailable)
            {
                if (context.IsCarryingTwoHanded)
                    return "Покладіть предмет ([G]), щоб поборотися з Босом на армреслінгу";
                int winResp = balanceConfig != null ? Mathf.RoundToInt(balanceConfig.armWrestleWinRespect) : 12;
                return $"[E] Армреслінг із Босом качалки (QTE • +{winResp} Респекту, раз на 2 дні)";
            }

            string moodStr = Mood switch
            {
                GymBossMood.Pleased => "Задоволений",
                GymBossMood.Angry => "Розлючений",
                _ => "Нейтральний"
            };
            return $"Бос качалки ({moodStr}) — віталися сьогодні. Принесіть підгон (яйця / згущонку / протеїн / каву)!";
        }

        public bool CanInteract(PlayerContext context)
        {
            if (IsArmWrestlingActive) return false;
            ItemDefinition active = context.ActiveItem;
            if (active != null && active.IsBossGift)
                return true;
            if (!GreetedToday)
                return true;
            if (IsFridayChallengeAvailable && !context.IsCarryingTwoHanded && context.Stats != null && context.Stats.CanTrainInGym)
                return true;
            if (IsArmWrestleAvailable && !context.IsCarryingTwoHanded)
                return true;
            return false;
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;

            DormStatsManager stats = context.Stats ?? statsManager;
            ItemDefinition active = context.ActiveItem;

            // Priority 1: Give Boss Gift from hand
            if (active != null && active.IsBossGift && context.Inventory != null)
            {
                float giftBonus = Mathf.Max(5f, active.respectGiftBonus);
                string giftName = active.displayName;
                if (context.Inventory.TryConsumeActiveItem())
                {
                    GiftsGivenToday++;
                    stats?.ModifyRespect(giftBonus);
                    SetMood(GymBossMood.Pleased);
                    _handshakeAnimTimer = 0.7f;
                    OnGiftReceived?.Invoke(active, giftBonus);
                    HudController.Instance?.ShowToast(
                        $"Бос качалки: «Оце підгон — {giftName}! Повага, першокурсник!» (+{Mathf.RoundToInt(giftBonus)} Респекту)");
                }
                return;
            }

            // Priority 2: Daily Manly Handshake
            if (!GreetedToday)
            {
                GreetedToday = true;
                bool morningBonus = IsInMorningBonusWindow;
                float mult = morningBonus ? balanceConfig.gymBossMorningShowerMultiplier : 1f;
                float gain = balanceConfig.gymBossGreetRespect * mult;

                stats?.ModifyRespect(gain);
                SetMood(GymBossMood.Pleased);
                _handshakeAnimTimer = 0.85f;
                AudioManager.Instance?.PlayBossHandshake(transform.position + Vector3.up * 1.3f);
                OnPlayerGreeted?.Invoke(morningBonus, gain);

                string msg = morningBonus
                    ? $"ЛЯПАС ДОЛОНЯМИ! Бос у душі оцінив сміливість та етикет! (x2: +{Mathf.RoundToInt(gain)} Респекту)"
                    : $"Чоловіче рукостискання з Босом качалки! (+{Mathf.RoundToInt(gain)} Респекту)";
                HudController.Instance?.ShowToast(msg);
                return;
            }

            // Priority 3: Friday Climax — Final Bench Press Showdown!
            if (IsFridayChallengeAvailable && !context.IsCarryingTwoHanded)
            {
                GymEquipment bench = FindBenchPress();
                if (bench != null)
                {
                    HudController.Instance?.ShowToast("БОС КАЧАЛКИ: «П'ятничний екзамен! Покажи, на що здатен на жимі!»");
                    bench.StartBossChallenge(context);
                }
                return;
            }

            // Priority 4: MoSCoW Should — Arm-Wrestling QTE mini-game (once every 2 days)
            if (IsArmWrestleAvailable && !context.IsCarryingTwoHanded)
            {
                StartArmWrestling(context);
            }
        }

        public void StartArmWrestling(PlayerContext context)
        {
            _armWrestleCtx = context;
            IsArmWrestlingActive = true;
            ArmWrestleMeter = 0.5f;
            _armWrestleNeedle = 0.1f;
            _armWrestleNeedleDir = 1f;

            StopMovement();
            _armWrestleCtx.Controller?.SetMovementLocked(true);
            _armWrestleCtx.Inventory?.SetInputLocked(true);
            GameStateManager.Instance?.RegisterActiveQte(CancelArmWrestleFromEsc);

            _armPushAction?.Enable();
            _armCancelAction?.Enable();

            AudioManager.Instance?.PlayBossHandshake(transform.position + Vector3.up * 1.2f);
            PushArmWrestleHud();
        }

        private void CancelArmWrestleFromEsc()
        {
            if (!IsArmWrestlingActive) return;
            StopArmWrestleCleanly();
            HudController.Instance?.ShowToast("Армреслінг скасовано. Бос усміхнувся: «Іншим разом!»");
        }

        private void UpdateArmWrestlingQte()
        {
            if (_armCancelAction != null && _armCancelAction.WasPressedThisFrame())
            {
                CancelArmWrestleFromEsc();
                return;
            }

            // Boss pushes steadily downward; player Strength & Coffee Buff reduce Boss pressure!
            DormStatsManager stats = _armWrestleCtx.Stats ?? statsManager;
            int strength = stats != null ? stats.Strength : 1;
            bool buffed = stats != null && stats.HasGymBuff;
            float bossPushRate = Mathf.Max(0.11f, 0.25f - (strength - 1) * 0.032f) * (buffed ? 0.80f : 1f);
            ArmWrestleMeter -= bossPushRate * Time.deltaTime;

            // Oscillating timing needle for sweet-spot bonus surges
            _armWrestleNeedle += _armWrestleNeedleDir * 1.35f * Time.deltaTime;
            if (_armWrestleNeedle >= 1f) { _armWrestleNeedle = 1f; _armWrestleNeedleDir = -1f; }
            else if (_armWrestleNeedle <= 0f) { _armWrestleNeedle = 0f; _armWrestleNeedleDir = 1f; }

            if (_armPushAction != null && _armPushAction.WasPressedThisFrame())
            {
                bool inSweet = _armWrestleNeedle >= 0.34f && _armWrestleNeedle <= 0.66f;
                ApplyArmWrestlePush(inSweet);
            }

            if (ArmWrestleMeter >= 1f)
            {
                FinishArmWrestling(playerWon: true);
                return;
            }
            if (ArmWrestleMeter <= 0f)
            {
                FinishArmWrestling(playerWon: false);
                return;
            }

            PushArmWrestleHud();
        }

        public void ApplyArmWrestlePush(bool inSweetSpot)
        {
            if (!IsArmWrestlingActive) return;
            DormStatsManager stats = _armWrestleCtx.Stats ?? statsManager;
            float strMult = stats != null ? stats.StrengthSpeedMultiplier : 1f;
            float push = (inSweetSpot ? 0.14f : 0.06f) * strMult;
            ArmWrestleMeter = Mathf.Clamp01(ArmWrestleMeter + push);
            _handshakeAnimTimer = 0.22f;
            if (ArmWrestleMeter >= 1f)
                FinishArmWrestling(playerWon: true);
        }

        public void FinishArmWrestling(bool playerWon)
        {
            if (!IsArmWrestlingActive) return;
            StopArmWrestleCleanly();

            LastArmWrestleDay = dayTimeController != null ? dayTimeController.CurrentDay : 1;
            DormStatsManager stats = _armWrestleCtx.Stats ?? statsManager;

            if (playerWon)
            {
                float winResp = balanceConfig != null ? balanceConfig.armWrestleWinRespect : 12f;
                stats?.ModifyRespect(winResp);
                SetMood(GymBossMood.Pleased);
                _handshakeAnimTimer = 0.9f;
                AudioManager.Instance?.PlayBossHandshake(transform.position + Vector3.up * 1.3f);
                HudController.Instance?.ShowToast(
                    $"ПЕРЕМОГА В АРМРЕСЛІНГУ! Ти поклав руку Боса качалки! (+{Mathf.RoundToInt(winResp)} Респекту)");
                QuestManager.Instance?.AddChatMessage(
                    "Бос качалки",
                    "Оце хватка! Першокурсник поклав мене на армреслінгу! Повага!",
                    isAlert: false);
            }
            else
            {
                float loseResp = balanceConfig != null ? balanceConfig.armWrestleLoseRespect : 3f;
                float loseEnergy = balanceConfig != null ? balanceConfig.armWrestleLoseEnergy : 8f;
                stats?.ModifyRespect(-loseResp);
                stats?.ModifyEnergy(-loseEnergy);
                HudController.Instance?.ShowToast(
                    $"Бос поклав твою руку! (-{Mathf.RoundToInt(loseEnergy)}% Енергії, -{Mathf.RoundToInt(loseResp)} Респекту). Підкачай Силу на штанзі!");
            }

            OnArmWrestleFinished?.Invoke(playerWon);
        }

        private void StopArmWrestleCleanly()
        {
            GameStateManager.Instance?.UnregisterActiveQte(CancelArmWrestleFromEsc);
            IsArmWrestlingActive = false;
            _armPushAction?.Disable();
            _armCancelAction?.Disable();
            _armWrestleCtx.Controller?.SetMovementLocked(false);
            _armWrestleCtx.Inventory?.SetInputLocked(false);
            HudController.Instance?.HideQte();
        }

        private void PushArmWrestleHud()
        {
            DormStatsManager stats = _armWrestleCtx.Stats ?? statsManager;
            int str = stats != null ? stats.Strength : 1;
            HudController.Instance?.ShowQte(new QteDisplayState
            {
                Title = $"АРМРЕСЛІНГ ІЗ БОСОМ (Сила {str}/5)",
                Instruction = "Тисніть [Space / ЛКМ] (у зеленій зоні — подвійний ривок!) • [Esc] Здатись",
                ProgressNormalized = Mathf.Clamp01(ArmWrestleMeter),
                ShowTimingBar = true,
                TimingCursor = _armWrestleNeedle,
                SweetSpotMin = 0.34f,
                SweetSpotMax = 0.66f,
                ShowBalanceBar = false
            });
        }

        private static GymEquipment FindBenchPress()
        {
            var stations = FindObjectsByType<GymEquipment>();
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].StationType == GymStationType.BenchPress)
                    return stations[i];
            }
            return stations.Length > 0 ? stations[0] : null;
        }
    }
}
