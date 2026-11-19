using System;
using System.Collections.Generic;
using Dormitory7.Audio;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Features.Cockroaches;
using Dormitory7.Features.Cooking;
using Dormitory7.Features.Gym;
using Dormitory7.Features.Repairs;
using Dormitory7.NPC;
using Dormitory7.UI;
using UnityEngine;

namespace Dormitory7.Features.Quests
{
    [Serializable]
    public class QuestProgress
    {
        public QuestDefinition Definition;
        public int CurrentCount;
        public bool IsCompleted;

        public int TargetCount => Definition != null ? Mathf.Max(1, Definition.targetCount) : 1;
        public float NormalizedProgress => Mathf.Clamp01((float)CurrentCount / TargetCount);

        public QuestProgress(QuestDefinition def)
        {
            Definition = def;
            CurrentCount = 0;
            IsCompleted = false;
        }
    }

    [Serializable]
    public struct FloorChatMessage
    {
        public string Sender;
        public string Timestamp;
        public string Body;
        public bool IsAlert;
    }

    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private DayTimeController dayTimeController;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private HazardDirector hazardDirector;
        [SerializeField] private GymBossAI gymBoss;

        [Header("Quest Catalog")]
        [SerializeField] private List<QuestDefinition> dailyQuestPool = new List<QuestDefinition>();
        [SerializeField] private List<QuestDefinition> weeklyStepQuests = new List<QuestDefinition>();
        [SerializeField] private QuestDefinition fridayClimaxQuest;

        public IReadOnlyList<QuestProgress> ActiveQuests => _activeQuests;
        public IReadOnlyList<FloorChatMessage> FloorChat => _chatMessages;
        public int UnreadChatCount { get; private set; }
        public int TotalCompletedQuestsWeek { get; private set; }
        public bool FridayBossChallengeCompleted { get; private set; }
        public bool IsFridayClimaxActive => dayTimeController != null && dayTimeController.CurrentDay >= 5;

        public event Action OnQuestsChanged;
        public event Action OnChatChanged;

        private readonly List<QuestProgress> _activeQuests = new List<QuestProgress>();
        private readonly List<FloorChatMessage> _chatMessages = new List<FloorChatMessage>();
        private HungryNeighborAI[] _neighbors;

        // End-of-day tracking
        private float _dayStartRespect;
        private float _dayStartTrash;
        private List<string> _snapshotCompletedTitles = new List<string>();
        private List<string> _snapshotMissedTitles = new List<string>();
        private int _snapshotCompletedCount;
        private int _snapshotTotalCount;

        public void ConfigureCatalog(
            List<QuestDefinition> dailies,
            List<QuestDefinition> weeklies,
            QuestDefinition climax,
            DayTimeController dayTime,
            DormStatsManager stats,
            HazardDirector hazards,
            GymBossAI boss)
        {
            dailyQuestPool = dailies ?? new List<QuestDefinition>();
            weeklyStepQuests = weeklies ?? new List<QuestDefinition>();
            fridayClimaxQuest = climax;
            dayTimeController = dayTime;
            statsManager = stats;
            hazardDirector = hazards;
            gymBoss = boss;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (dayTimeController == null) dayTimeController = FindAnyObjectByType<DayTimeController>();
            if (statsManager == null) statsManager = FindAnyObjectByType<DormStatsManager>();
            if (hazardDirector == null) hazardDirector = FindAnyObjectByType<HazardDirector>();
            if (gymBoss == null) gymBoss = FindAnyObjectByType<GymBossAI>();
            _neighbors = FindObjectsByType<HungryNeighborAI>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (dayTimeController != null)
            {
                dayTimeController.OnDayEnding += HandleDayEnding;
                dayTimeController.OnDayAdvanced += HandleDayAdvanced;
                dayTimeController.OnDaySummaryReady += HandleDaySummaryReady;
                dayTimeController.OnCurfewWarning += HandleCurfewWarning;
                dayTimeController.OnCurfewStarted += HandleCurfewStarted;
            }

            if (hazardDirector != null)
                hazardDirector.OnHazardTriggered += HandleHazardTriggered;

            if (gymBoss != null)
            {
                gymBoss.OnPlayerGreeted += HandleBossGreeted;
                gymBoss.OnGiftReceived += HandleBossGiftReceived;
                gymBoss.OnFridayChallengeWon += HandleFridayChallengeWon;
            }

            if (_neighbors != null)
            {
                for (int i = 0; i < _neighbors.Length; i++)
                {
                    if (_neighbors[i] != null)
                        _neighbors[i].OnPanStolen += HandleNeighborStolePan;
                }
            }

            CockroachAI.OnAnyCockroachKilled += HandleCockroachKilled;
            MessSpot.OnAnyMessCleaned += HandleMessCleaned;
            TrashBinInteractable.OnAnyCockroachDisposed += HandleCockroachDisposed;
            FryingPanState.OnAnyMealCooked += HandleMealCooked;
            MivinaKettleStation.OnAnyMivinaBrewed += HandleMivinaBrewed;
            RepairableHazard.OnAnyHazardRepaired += HandleHazardRepaired;
            GymEquipment.OnAnyWorkoutCompleted += HandleWorkoutCompleted;
        }

        private void OnDisable()
        {
            if (dayTimeController != null)
            {
                dayTimeController.OnDayEnding -= HandleDayEnding;
                dayTimeController.OnDayAdvanced -= HandleDayAdvanced;
                dayTimeController.OnDaySummaryReady -= HandleDaySummaryReady;
                dayTimeController.OnCurfewWarning -= HandleCurfewWarning;
                dayTimeController.OnCurfewStarted -= HandleCurfewStarted;
            }

            if (hazardDirector != null)
                hazardDirector.OnHazardTriggered -= HandleHazardTriggered;

            if (gymBoss != null)
            {
                gymBoss.OnPlayerGreeted -= HandleBossGreeted;
                gymBoss.OnGiftReceived -= HandleBossGiftReceived;
                gymBoss.OnFridayChallengeWon -= HandleFridayChallengeWon;
            }

            if (_neighbors != null)
            {
                for (int i = 0; i < _neighbors.Length; i++)
                {
                    if (_neighbors[i] != null)
                        _neighbors[i].OnPanStolen -= HandleNeighborStolePan;
                }
            }

            CockroachAI.OnAnyCockroachKilled -= HandleCockroachKilled;
            MessSpot.OnAnyMessCleaned -= HandleMessCleaned;
            TrashBinInteractable.OnAnyCockroachDisposed -= HandleCockroachDisposed;
            FryingPanState.OnAnyMealCooked -= HandleMealCooked;
            MivinaKettleStation.OnAnyMivinaBrewed -= HandleMivinaBrewed;
            RepairableHazard.OnAnyHazardRepaired -= HandleHazardRepaired;
            GymEquipment.OnAnyWorkoutCompleted -= HandleWorkoutCompleted;
        }

        private void Start()
        {
            EnsureFallbackCatalogIfEmpty();

            _dayStartRespect = statsManager != null ? statsManager.Respect : 10f;
            _dayStartTrash = statsManager != null ? statsManager.Trash : 0f;

            int startDay = dayTimeController != null ? dayTimeController.CurrentDay : 1;
            GenerateQuestsForDay(startDay);

            AddChatMessage("Староста поверху", "Ласкаво просимо на 1-й поверх Гуртожитку №7! Тримай Треш нижче 50% і не проспи обхід Зіни о 22:00.", isAlert: false);
            AddChatMessage("Сусід Толік (к.106)", "Йоу, першокурсник! Якщо смажиш картоплю — не відходь далеко, тут народ голодний :)", isAlert: false);
            AddChatMessage("Бос качалки", "Зранку чекаю в душовій/умивальнику, а вдень у підвалі. Не ігноруй рукостискання!", isAlert: false);
        }

        public void MarkChatAsRead()
        {
            if (UnreadChatCount > 0)
            {
                UnreadChatCount = 0;
                OnChatChanged?.Invoke();
            }
        }

        public void AddChatMessage(string sender, string body, bool isAlert = false)
        {
            string stamp = dayTimeController != null ? $"Д{dayTimeController.CurrentDay} {dayTimeController.FormattedTime}" : "08:00";
            _chatMessages.Add(new FloorChatMessage
            {
                Sender = sender,
                Timestamp = stamp,
                Body = body,
                IsAlert = isAlert
            });

            // Keep latest 30 messages
            while (_chatMessages.Count > 30)
                _chatMessages.RemoveAt(0);

            if (GameStateManager.Instance == null || GameStateManager.Instance.CurrentState != GameState.Smartphone)
                UnreadChatCount++;

            OnChatChanged?.Invoke();
        }

        public void GenerateQuestsForDay(int day)
        {
            _activeQuests.Clear();

            // 1. Pick 1 weekly step for this day (Days 1..5)
            QuestDefinition weeklyForToday = null;
            if (weeklyStepQuests != null && weeklyStepQuests.Count > 0)
            {
                int idx = Mathf.Clamp(day - 1, 0, weeklyStepQuests.Count - 1);
                weeklyForToday = weeklyStepQuests[idx];
            }

            // 2. Pick 2 mandatory daily quests (avoid duplicating the weekly objective type)
            var eligibleDailies = new List<QuestDefinition>();
            if (dailyQuestPool != null)
            {
                for (int i = 0; i < dailyQuestPool.Count; i++)
                {
                    var q = dailyQuestPool[i];
                    if (q == null) continue;
                    if (day < q.minDay || day > q.maxDay) continue;
                    if (weeklyForToday != null && q.objectiveType == weeklyForToday.objectiveType) continue;
                    eligibleDailies.Add(q);
                }
            }

            // Rotate deterministically by day so every day gives a fresh pair of 2 daily quests
            int dailyCount = Mathf.Min(2, eligibleDailies.Count);
            int offset = ((Mathf.Max(1, day) - 1) * 2) % Mathf.Max(1, eligibleDailies.Count);
            for (int k = 0; k < dailyCount; k++)
            {
                QuestDefinition chosen = eligibleDailies[(offset + k) % eligibleDailies.Count];
                _activeQuests.Add(new QuestProgress(chosen));
            }

            if (weeklyForToday != null)
                _activeQuests.Add(new QuestProgress(weeklyForToday));

            // 3. Day 5 (Friday) Climax Quest!
            if (day >= 5 && fridayClimaxQuest != null)
            {
                _activeQuests.Add(new QuestProgress(fridayClimaxQuest));
                AddChatMessage(
                    "СТАРОСТА ПОВЕРХУ",
                    "П'ЯТНИЦЯ — ФІНАЛЬНИЙ ДЕНЬ! Аварії сиплються частіше, Зінаїда готує генеральний обхід о 22:00, а Бос качалки викликає тебе на фінальний жим у підвалі!",
                    isAlert: true);
            }

            OnQuestsChanged?.Invoke();
        }

        public void ReportObjectiveProgress(QuestObjectiveType type, int amount = 1)
        {
            if (amount <= 0) return;

            bool anyChanged = false;
            for (int i = 0; i < _activeQuests.Count; i++)
            {
                QuestProgress qp = _activeQuests[i];
                if (qp == null || qp.Definition == null || qp.IsCompleted) continue;
                if (qp.Definition.objectiveType != type) continue;

                qp.CurrentCount = Mathf.Min(qp.TargetCount, qp.CurrentCount + amount);
                anyChanged = true;

                if (qp.CurrentCount >= qp.TargetCount)
                {
                    CompleteQuest(qp);
                }
            }

            if (anyChanged)
                OnQuestsChanged?.Invoke();
        }

        private void CompleteQuest(QuestProgress qp)
        {
            qp.IsCompleted = true;
            TotalCompletedQuestsWeek++;

            if (qp.Definition.respectReward > 0f)
                statsManager?.ModifyRespect(qp.Definition.respectReward);
            if (qp.Definition.trashReliefReward > 0f)
                statsManager?.ModifyTrash(-qp.Definition.trashReliefReward);

            string rewardTag = qp.Definition.respectReward > 0f
                ? $" (+{Mathf.RoundToInt(qp.Definition.respectReward)} Респекту)"
                : "";
            HudController.Instance?.ShowToast($"ЗАДАЧУ ВИКОНАНО: {qp.Definition.title}{rewardTag}");
            AudioManager.Instance?.PlayQuestChime();
            AddChatMessage("Смартфон • Задачі", $"Виконано: {qp.Definition.title}{rewardTag}", isAlert: false);
        }

        // ==================== End-of-Day Summary Pipeline ====================

        private void HandleDayEnding(int finishedDay, bool forcedByKomendant, string endNote)
        {
            _snapshotCompletedTitles = new List<string>();
            _snapshotMissedTitles = new List<string>();
            _snapshotCompletedCount = 0;
            _snapshotTotalCount = _activeQuests.Count;

            for (int i = 0; i < _activeQuests.Count; i++)
            {
                var q = _activeQuests[i];
                if (q?.Definition == null) continue;
                if (q.IsCompleted)
                {
                    _snapshotCompletedCount++;
                    _snapshotCompletedTitles.Add(q.Definition.title);
                }
                else
                {
                    _snapshotMissedTitles.Add($"{q.Definition.title} ({q.CurrentCount}/{q.TargetCount})");
                }
            }
        }

        private void HandleDayAdvanced(int newDay, string _)
        {
            GenerateQuestsForDay(newDay);

            string morningFlavor = newDay switch
            {
                2 => "Вівторок! Хтось уночі знову насмітив біля кухні. Не забудь забігти до Боса.",
                3 => "Середа — екватор тижня! Схоже, сантехніка починає здавати, тримай ключ напоготові.",
                4 => "Четвер! До п'ятниці один крок. Поверх гуде, Зіна стала ще підозрілішою.",
                5 => "П'ЯТНИЦЯ! Останній ривок, щоб закрити тиждень у Гуртожитку №7!",
                _ => $"Новий день ({newDay}) у Гуртожитку №7!"
            };
            AddChatMessage("Сусід Вадік (к.103)", morningFlavor, isAlert: newDay >= 5);
        }

        private void HandleDaySummaryReady(int finishedDay, int nextDay, string endNote)
        {
            float currentRespect = statsManager != null ? statsManager.Respect : 0f;
            float currentTrash = statsManager != null ? statsManager.Trash : 0f;

            var summary = new DaySummaryData
            {
                CompletedDay = finishedDay,
                NextDay = nextDay,
                CompletedDayName = DayTimeController.GetFullDayName(finishedDay),
                NextDayName = DayTimeController.GetFullDayName(nextDay),
                EndNote = endNote,
                CompletedQuestsCount = _snapshotCompletedCount,
                TotalQuestsCount = _snapshotTotalCount,
                CompletedQuestTitles = new List<string>(_snapshotCompletedTitles),
                MissedQuestTitles = new List<string>(_snapshotMissedTitles),
                RespectDelta = currentRespect - _dayStartRespect,
                TrashDelta = currentTrash - _dayStartTrash,
                CurrentRespect = currentRespect,
                CurrentTrash = currentTrash,
                CurrentReprimands = statsManager != null ? statsManager.Reprimands : 0,
                Strength = statsManager != null ? statsManager.Strength : 1,
                Stamina = statsManager != null ? statsManager.Stamina : 1
            };

            // Reset baseline for the new day
            _dayStartRespect = currentRespect;
            _dayStartTrash = currentTrash;

            GameStateManager.Instance?.ShowDaySummary(summary);
        }

        // ==================== Event Handlers ====================

        private void HandleCurfewWarning()
        {
            string msg = IsFridayClimaxActive
                ? "21:30 — П'ЯТНИЧНА ТРИВОГА! Зіна вийшла на генеральне полювання! Ховайся або готуй шоколадку!"
                : "21:30 — Зіна вийшла на полювання! Прибирайте поверх і замикайтесь у 104 до 22:00!";
            AddChatMessage("ЧАТ ПОВЕРХУ (УВАГА!)", msg, isAlert: true);
        }

        private void HandleCurfewStarted()
        {
            AddChatMessage("ЧАТ ПОВЕРХУ (ВІДБІЙ)", "22:00! Зінаїда Петрівна з ліхтариком вже на поверсі!", isAlert: true);
        }

        private void HandleHazardTriggered(RepairableHazard hazard)
        {
            if (hazard == null) return;
            AddChatMessage("Сусід Вадік (к.103)", $"Хелп! {hazard.GetHazardDisplayName()} — Треш росте! Хтось полагодьте!", isAlert: true);
        }

        private void HandleNeighborStolePan(HungryNeighborAI neighbor, Interaction.WorldItem pan)
        {
            if (neighbor == null) return;
            AddChatMessage("Кухня 1-го поверху", $"Увага! {neighbor.NeighborName} поцупив сковорідку з їжею прямо з плити!", isAlert: true);
        }

        private void HandleCockroachKilled(CockroachAI roach) => ReportObjectiveProgress(QuestObjectiveType.KillCockroaches, 1);
        private void HandleMessCleaned(MessSpot spot) => ReportObjectiveProgress(QuestObjectiveType.CleanMessOrDispose, 1);
        private void HandleCockroachDisposed() => ReportObjectiveProgress(QuestObjectiveType.CleanMessOrDispose, 1);
        private void HandleMealCooked(string dishName) => ReportObjectiveProgress(QuestObjectiveType.CookMeal, 1);
        private void HandleMivinaBrewed() => ReportObjectiveProgress(QuestObjectiveType.CookMeal, 1);
        private void HandleHazardRepaired(RepairableHazard hazard, bool permanent) => ReportObjectiveProgress(QuestObjectiveType.RepairHazard, 1);
        private void HandleBossGreeted(bool morningBonus, float gain) => ReportObjectiveProgress(QuestObjectiveType.GreetGymBoss, 1);
        private void HandleBossGiftReceived(ItemDefinition gift, float gain) => ReportObjectiveProgress(QuestObjectiveType.GiveBossGift, 1);

        private void HandleWorkoutCompleted(GymStationType station, bool wasBossChallenge)
        {
            ReportObjectiveProgress(QuestObjectiveType.CompleteGymWorkout, 1);
            if (wasBossChallenge)
            {
                FridayBossChallengeCompleted = true;
                ReportObjectiveProgress(QuestObjectiveType.FridayBossBenchChallenge, 1);
            }
        }

        private void HandleFridayChallengeWon()
        {
            FridayBossChallengeCompleted = true;
            ReportObjectiveProgress(QuestObjectiveType.FridayBossBenchChallenge, 1);
            AddChatMessage("Бос качалки", "КРАСАВА! Ти витримав фінальний жим у п'ятницю! Повага від усього підвалу!", isAlert: false);
        }

        private void EnsureFallbackCatalogIfEmpty()
        {
            if (dailyQuestPool != null && dailyQuestPool.Count >= 2 && weeklyStepQuests != null && weeklyStepQuests.Count >= 5)
                return;

            dailyQuestPool ??= new List<QuestDefinition>();
            if (dailyQuestPool.Count < 2)
            {
                dailyQuestPool.Add(CreateRuntimeQuest("d_cook", "Гарячий обід", "Приготуйте їжу на плиті або заваріть Мівіну.", QuestCategory.Daily, QuestObjectiveType.CookMeal, 1, 5f, 2f));
                dailyQuestPool.Add(CreateRuntimeQuest("d_roaches", "Полювання на тарганів", "Знищіть 2 тарганів капцем або дихлофосом.", QuestCategory.Daily, QuestObjectiveType.KillCockroaches, 2, 6f, 4f));
                dailyQuestPool.Add(CreateRuntimeQuest("d_boss", "Етикет качалки", "Привітайтесь за руку з Босом качалки.", QuestCategory.Daily, QuestObjectiveType.GreetGymBoss, 1, 5f, 0f));
                dailyQuestPool.Add(CreateRuntimeQuest("d_gym", "Підхід у підвалі", "Виконайте тренування на жимі або турніку.", QuestCategory.Daily, QuestObjectiveType.CompleteGymWorkout, 1, 6f, 0f));
                dailyQuestPool.Add(CreateRuntimeQuest("d_repair", "Аварійна бригада", "Відремонтуйте поломку на поверсі.", QuestCategory.Daily, QuestObjectiveType.RepairHazard, 1, 7f, 5f));
                dailyQuestPool.Add(CreateRuntimeQuest("d_clean", "Чистий коридор", "Приберіть калюжу/сміття або викиньте тушку таргана.", QuestCategory.Daily, QuestObjectiveType.CleanMessOrDispose, 1, 5f, 4f));
            }

            weeklyStepQuests ??= new List<QuestDefinition>();
            if (weeklyStepQuests.Count < 5)
            {
                weeklyStepQuests.Clear();
                weeklyStepQuests.Add(CreateRuntimeQuest("w_day1", "Тиждень 1/5: Авторитет", "Привітайтеся з Босом качалки у перший день.", QuestCategory.Weekly, QuestObjectiveType.GreetGymBoss, 1, 6f, 2f));
                weeklyStepQuests.Add(CreateRuntimeQuest("w_day2", "Тиждень 2/5: Шеф-кухар", "Приготуйте страву на кухні або заваріть Мівіну.", QuestCategory.Weekly, QuestObjectiveType.CookMeal, 1, 6f, 2f));
                weeklyStepQuests.Add(CreateRuntimeQuest("w_day3", "Тиждень 3/5: Залізний хват", "Виконайте підхід у підвальній качалці.", QuestCategory.Weekly, QuestObjectiveType.CompleteGymWorkout, 1, 8f, 0f));
                weeklyStepQuests.Add(CreateRuntimeQuest("w_day4", "Тиждень 4/5: Майстер поверху", "Ліквідуйте аварію на 1-му поверсі.", QuestCategory.Weekly, QuestObjectiveType.RepairHazard, 1, 8f, 5f));
                weeklyStepQuests.Add(CreateRuntimeQuest("w_day5", "Тиждень 5/5: Генеральне прибирання", "Приберіть поверх перед фінальним обходом Зіни.", QuestCategory.Weekly, QuestObjectiveType.CleanMessOrDispose, 1, 10f, 6f));
            }

            if (fridayClimaxQuest == null)
            {
                fridayClimaxQuest = CreateRuntimeQuest("climax_fri", "П'ЯТНИЧНИЙ ВИКЛИК: Бос качалки", "Киньте виклик Босу [E] та виграйте фінальний підхід на жимі!", QuestCategory.Climax, QuestObjectiveType.FridayBossBenchChallenge, 1, 12f, 5f);
            }
        }

        private static QuestDefinition CreateRuntimeQuest(string id, string title, string desc, QuestCategory cat, QuestObjectiveType type, int target, float respect, float trashRelief)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.questId = id;
            q.title = title;
            q.description = desc;
            q.category = cat;
            q.objectiveType = type;
            q.targetCount = target;
            q.respectReward = respect;
            q.trashReliefReward = trashRelief;
            return q;
        }
    }
}
