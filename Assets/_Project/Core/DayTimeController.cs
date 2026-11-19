using System;
using Dormitory7.Data;
using UnityEngine;

namespace Dormitory7.Core
{
    public class DayTimeController : MonoBehaviour
    {
        private static readonly string[] DayShortNames = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд" };
        private static readonly string[] DayFullNames = { "Понеділок", "Вівторок", "Середа", "Четвер", "П'ятниця", "Субота", "Неділя" };

        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private DormStatsManager statsManager;

        public int CurrentDay { get; private set; } = 1;
        public float CurrentHour { get; private set; } = 8f;
        public bool IsCurfewWarningTriggered { get; private set; }
        public bool IsCurfewActive { get; private set; }
        public bool IsWeekCompleted { get; private set; }

        public float GameHoursPerRealSecond
        {
            get
            {
                if (balanceConfig == null || balanceConfig.realSecondsFromStartToCurfew <= 0f)
                    return 14f / 360f;
                float span = Mathf.Max(1f, balanceConfig.curfewHour - balanceConfig.dayStartHour);
                return span / balanceConfig.realSecondsFromStartToCurfew;
            }
        }

        public string FormattedTime
        {
            get
            {
                int totalMinutes = Mathf.FloorToInt(CurrentHour * 60f);
                int hours = (totalMinutes / 60) % 24;
                int minutes = totalMinutes % 60;
                return $"{hours:00}:{minutes:00}";
            }
        }

        public string CurrentDayShortName => DayShortNames[(Mathf.Max(1, CurrentDay) - 1) % DayShortNames.Length];
        public string CurrentDayFullName => DayFullNames[(Mathf.Max(1, CurrentDay) - 1) % DayFullNames.Length];
        public string FormattedDayAndTime => $"День {CurrentDay} ({CurrentDayShortName}) • {FormattedTime}";

        public event Action<int, float, string> OnTimeChanged;
        public event Action OnCurfewWarning;
        public event Action OnCurfewStarted;
        public event Action<int, string> OnDayAdvanced;
        public event Action OnWeekCompleted;

        private int _lastReportedMinute = -1;

        public void SetReferences(BalanceConfig config, DormStatsManager stats)
        {
            balanceConfig = config;
            statsManager = stats;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();

            ResetWeek();
        }

        public void ResetWeek()
        {
            CurrentDay = 1;
            CurrentHour = balanceConfig.dayStartHour;
            IsCurfewWarningTriggered = false;
            IsCurfewActive = false;
            IsWeekCompleted = false;
            _lastReportedMinute = -1;
            BroadcastTimeIfChanged(force: true);
        }

        private void Update()
        {
            if (IsWeekCompleted || balanceConfig == null) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            CurrentHour += GameHoursPerRealSecond * Time.deltaTime;
            if (CurrentHour >= 24f)
                CurrentHour = 23.98f; // Holds at 23:58 until player sleeps or curfew ends day

            if (!IsCurfewWarningTriggered && CurrentHour >= balanceConfig.curfewWarningHour)
            {
                IsCurfewWarningTriggered = true;
                OnCurfewWarning?.Invoke();
            }

            if (!IsCurfewActive && CurrentHour >= balanceConfig.curfewHour)
            {
                IsCurfewActive = true;
                OnCurfewStarted?.Invoke();
            }

            BroadcastTimeIfChanged(force: false);
        }

        public void SleepAndAdvanceDay()
        {
            SleepAndAdvanceDay(forcedByKomendant: false, customSummary: null);
        }

        public void SleepAndAdvanceDay(bool forcedByKomendant, string customSummary)
        {
            if (IsWeekCompleted || balanceConfig == null) return;

            // GDD 5.6: If sleeping cleanly before 22:00 with Trash <= 50%, curfew inspection is skipped!
            // If Trash > 50% when going to sleep (and not already reprimanded by Komendant), issue absentee reprimand.
            if (!forcedByKomendant && statsManager != null && statsManager.Trash > balanceConfig.komendantTrashThreshold)
            {
                statsManager.AddReprimand($"Критичний Треш ({Mathf.RoundToInt(statsManager.Trash)}% > 50%) на поверсі");
                if (statsManager.IsGameOver)
                    return;
            }

            if (statsManager != null)
                statsManager.ApplySleepRecovery();

            if (CurrentDay >= balanceConfig.daysInWeek)
            {
                IsWeekCompleted = true;
                OnWeekCompleted?.Invoke();
                return;
            }

            CurrentDay++;
            CurrentHour = balanceConfig.dayStartHour;
            IsCurfewWarningTriggered = false;
            IsCurfewActive = false;
            _lastReportedMinute = -1;

            BroadcastTimeIfChanged(force: true);
            string summary = !string.IsNullOrEmpty(customSummary)
                ? customSummary
                : $"День {CurrentDay} — {CurrentDayFullName}. Сон відновив +{Mathf.RoundToInt(balanceConfig.sleepEnergyRestore)}% Енергії.";
            OnDayAdvanced?.Invoke(CurrentDay, summary);
        }

        public void SetHourForTesting(float hour)
        {
            CurrentHour = Mathf.Clamp(hour, 0f, 23.98f);
            if (!IsCurfewWarningTriggered && balanceConfig != null && CurrentHour >= balanceConfig.curfewWarningHour)
            {
                IsCurfewWarningTriggered = true;
                OnCurfewWarning?.Invoke();
            }
            if (!IsCurfewActive && balanceConfig != null && CurrentHour >= balanceConfig.curfewHour)
            {
                IsCurfewActive = true;
                OnCurfewStarted?.Invoke();
            }
            BroadcastTimeIfChanged(force: true);
        }

        private void BroadcastTimeIfChanged(bool force)
        {
            int currentMinute = Mathf.FloorToInt(CurrentHour * 60f);
            if (!force && currentMinute == _lastReportedMinute)
                return;

            _lastReportedMinute = currentMinute;
            OnTimeChanged?.Invoke(CurrentDay, CurrentHour, FormattedDayAndTime);
        }
    }
}
