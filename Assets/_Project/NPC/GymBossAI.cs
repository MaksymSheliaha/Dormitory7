using System;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using TMPro;
using UnityEngine;

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

        public bool IsInMorningBonusWindow =>
            CurrentSlot == GymBossScheduleSlot.MorningShower ||
            CurrentSlot == GymBossScheduleSlot.MorningWashroom;

        public event Action<GymBossMood> OnMoodChanged;
        public event Action<bool, float> OnPlayerGreeted;
        public event Action<ItemDefinition, float> OnGiftReceived;

        private bool _scheduleInitialized;
        private float _handshakeAnimTimer;
        private Transform _playerTransform;

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
            ItemDefinition active = context.ActiveItem;
            if (active != null && active.IsBossGift)
                return true;
            return !GreetedToday;
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
                OnPlayerGreeted?.Invoke(morningBonus, gain);

                string msg = morningBonus
                    ? $"ЛЯПАС ДОЛОНЯМИ! Бос у душі оцінив сміливість та етикет! (x2: +{Mathf.RoundToInt(gain)} Респекту)"
                    : $"Чоловіче рукостискання з Босом качалки! (+{Mathf.RoundToInt(gain)} Респекту)";
                HudController.Instance?.ShowToast(msg);
            }
        }
    }
}
