using System;
using System.Collections.Generic;
using Dormitory7.Core;
using Dormitory7.Data;
using UnityEngine;

namespace Dormitory7.Features.Repairs
{
    public class HazardDirector : MonoBehaviour
    {
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private DayTimeController dayTimeController;
        [SerializeField] private List<RepairableHazard> hazards = new List<RepairableHazard>();

        public IReadOnlyList<RepairableHazard> Hazards => hazards;
        public int HazardsTriggeredToday { get; private set; }
        public int DailyHazardQuota { get; private set; } = 1;
        public bool IsFridayEscalationActive => dayTimeController != null && dayTimeController.CurrentDay >= 5;

        public event Action<RepairableHazard> OnHazardTriggered;

        private float _nextHazardTimer = 70f;

        private void Awake()
        {
            if (hazards == null || hazards.Count == 0)
                hazards = new List<RepairableHazard>(FindObjectsByType<RepairableHazard>());
            if (dayTimeController == null)
                dayTimeController = FindAnyObjectByType<DayTimeController>();
            if (balanceConfig == null)
            {
                var stats = FindAnyObjectByType<DormStatsManager>();
                balanceConfig = stats != null && stats.Config != null
                    ? stats.Config
                    : ScriptableObject.CreateInstance<BalanceConfig>();
            }
        }

        private void OnEnable()
        {
            if (dayTimeController != null)
                dayTimeController.OnDayAdvanced += HandleDayAdvanced;
        }

        private void OnDisable()
        {
            if (dayTimeController != null)
                dayTimeController.OnDayAdvanced -= HandleDayAdvanced;
        }

        private void Start()
        {
            int day = dayTimeController != null ? dayTimeController.CurrentDay : 1;
            ConfigureForDay(day);
        }

        private void HandleDayAdvanced(int newDay, string _)
        {
            ConfigureForDay(newDay);
        }

        public void ConfigureForDay(int day)
        {
            HazardsTriggeredToday = 0;

            // Day 1: 1 scheduled random hazard, Day 2-3: 1-2, Day 4: 2, Day 5 (Friday Climax): 3
            DailyHazardQuota = day switch
            {
                <= 1 => 1,
                2 => 2,
                3 => 2,
                4 => 2,
                _ => 3
            };

            _nextHazardTimer = ComputeHazardIntervalSeconds(day) * 0.55f;

            // On Friday Climax (Day 5), immediately kick off a fresh hazard if fewer than 2 are broken
            if (day >= 5 && CountBrokenHazards() < 2)
                TriggerRandomWorkingHazard();
        }

        public float ComputeHazardIntervalSeconds(int day)
        {
            float baseInterval = balanceConfig != null ? balanceConfig.baseHazardIntervalSeconds : 110f;
            float dayScale = Mathf.Max(0.45f, 1f - (Mathf.Clamp(day, 1, 5) - 1) * 0.12f);
            if (day >= 5 && balanceConfig != null)
                dayScale *= balanceConfig.fridayHazardIntervalMultiplier;
            return Mathf.Max(22f, baseInterval * dayScale);
        }

        private void Update()
        {
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;
            if (HazardsTriggeredToday >= DailyHazardQuota)
                return;

            _nextHazardTimer -= Time.deltaTime;
            if (_nextHazardTimer <= 0f)
            {
                int day = dayTimeController != null ? dayTimeController.CurrentDay : 1;
                _nextHazardTimer = ComputeHazardIntervalSeconds(day);
                TriggerRandomWorkingHazard();
            }
        }

        public RepairableHazard TriggerRandomWorkingHazard()
        {
            var candidates = new List<RepairableHazard>();
            for (int i = 0; i < hazards.Count; i++)
            {
                var h = hazards[i];
                if (h != null && !h.IsBroken)
                    candidates.Add(h);
            }

            if (candidates.Count == 0)
                return null;

            RepairableHazard chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            chosen.TriggerBreakdown(silent: false);
            HazardsTriggeredToday++;
            OnHazardTriggered?.Invoke(chosen);
            return chosen;
        }

        public int CountBrokenHazards()
        {
            int count = 0;
            for (int i = 0; i < hazards.Count; i++)
            {
                if (hazards[i] != null && hazards[i].IsBroken)
                    count++;
            }
            return count;
        }

        public RepairableHazard FindByType(HazardType type)
        {
            for (int i = 0; i < hazards.Count; i++)
            {
                if (hazards[i] != null && hazards[i].Type == type)
                    return hazards[i];
            }
            return null;
        }
    }
}
