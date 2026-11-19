using System;
using Dormitory7.Data;
using UnityEngine;

namespace Dormitory7.Core
{
    public enum GameOverReason
    {
        None = 0,
        MaxReprimands = 1,
        MaxTrash = 2,
        Exhaustion = 3
    }

    public class DormStatsManager : MonoBehaviour
    {
        [SerializeField] private BalanceConfig balanceConfig;

        public BalanceConfig Config => balanceConfig;
        public float Energy { get; private set; }
        public float Respect { get; private set; }
        public float Trash { get; private set; }
        public int Reprimands { get; private set; }
        public int Strength { get; private set; } = 1;
        public int Stamina { get; private set; } = 1;
        public float GymBuffRemaining { get; private set; }
        public bool HasGymBuff => GymBuffRemaining > 0f;
        public float StrengthSpeedMultiplier => 1f + (Strength - 1) * 0.22f;
        public float StaminaDrainMultiplier => 1f - Mathf.Clamp01((Stamina - 1) * 0.12f);
        public float CurrentNoiseLevel { get; private set; }
        public bool IsGameOver { get; private set; }
        public GameOverReason LastGameOverReason { get; private set; }

        public float EnergyNormalized => balanceConfig != null && balanceConfig.maxEnergy > 0f
            ? Mathf.Clamp01(Energy / balanceConfig.maxEnergy)
            : 0f;

        public float TrashNormalized => balanceConfig != null && balanceConfig.maxTrash > 0f
            ? Mathf.Clamp01(Trash / balanceConfig.maxTrash)
            : 0f;

        public bool IsLowEnergy => balanceConfig != null && Energy < balanceConfig.lowEnergyWarningThreshold;
        public bool CanTrainInGym => balanceConfig != null && Energy >= balanceConfig.gymMinEnergyThreshold;

        public event Action<float, float> OnEnergyChanged;
        public event Action<float> OnRespectChanged;
        public event Action<float, float> OnTrashChanged;
        public event Action<int, int> OnReprimandsChanged;
        public event Action<int, int> OnAttributesChanged;
        public event Action<bool> OnGymBuffChanged;
        public event Action<GameOverReason, string> OnGameOver;

        private float _exhaustionTimer;
        private DayTimeController _dayTime;

        public void SetConfig(BalanceConfig config)
        {
            balanceConfig = config;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();

            _dayTime = FindAnyObjectByType<DayTimeController>();
            ResetStats();
        }

        public void ResetStats()
        {
            Energy = Mathf.Clamp(balanceConfig.startingEnergy, 0f, balanceConfig.maxEnergy);
            Respect = Mathf.Clamp(balanceConfig.startingRespect, balanceConfig.minRespect, balanceConfig.maxRespect);
            Trash = 0f;
            Reprimands = 0;
            Strength = 1;
            Stamina = 1;
            GymBuffRemaining = 0f;
            CurrentNoiseLevel = 0f;
            _exhaustionTimer = 0f;
            IsGameOver = false;
            LastGameOverReason = GameOverReason.None;

            NotifyAll();
        }

        private void Update()
        {
            if (IsGameOver || balanceConfig == null) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            if (GymBuffRemaining > 0f)
            {
                GymBuffRemaining = Mathf.Max(0f, GymBuffRemaining - Time.deltaTime);
                if (GymBuffRemaining <= 0f)
                    OnGymBuffChanged?.Invoke(false);
            }

            // Passive energy drain proportional to game hours passing
            float gameHoursPerRealSecond = _dayTime != null
                ? _dayTime.GameHoursPerRealSecond
                : 14f / Mathf.Max(1f, balanceConfig.realSecondsFromStartToCurfew);

            float drain = balanceConfig.passiveEnergyDrainPerGameHour * gameHoursPerRealSecond * Time.deltaTime;
            if (drain > 0f)
                ModifyEnergy(-drain);

            if (Energy <= 0.01f)
            {
                _exhaustionTimer += Time.deltaTime;
                if (_exhaustionTimer >= balanceConfig.exhaustionGameOverSeconds)
                {
                    TriggerGameOver(GameOverReason.Exhaustion, "Повне виснаження: ви знепритомніли в коридорі гуртожитку.");
                }
            }
            else
            {
                _exhaustionTimer = 0f;
            }
        }

        public void ModifyEnergy(float delta)
        {
            if (IsGameOver || balanceConfig == null) return;
            float prev = Energy;
            Energy = Mathf.Clamp(Energy + delta, 0f, balanceConfig.maxEnergy);
            if (!Mathf.Approximately(prev, Energy))
                OnEnergyChanged?.Invoke(Energy, balanceConfig.maxEnergy);
        }

        public void ConsumeSprintEnergy(float deltaTime)
        {
            if (balanceConfig == null) return;
            ModifyEnergy(-balanceConfig.sprintEnergyDrainPerSecond * StaminaDrainMultiplier * deltaTime);
        }

        public void ApplyGymBuff(float durationSeconds)
        {
            bool wasActive = HasGymBuff;
            GymBuffRemaining = Mathf.Max(GymBuffRemaining, Mathf.Max(1f, durationSeconds));
            if (!wasActive)
                OnGymBuffChanged?.Invoke(true);
        }

        public void ApplySleepRecovery()
        {
            if (balanceConfig == null) return;
            _exhaustionTimer = 0f;
            ModifyEnergy(balanceConfig.sleepEnergyRestore);
        }

        public void ModifyRespect(float delta)
        {
            if (IsGameOver || balanceConfig == null) return;
            float prev = Respect;
            Respect = Mathf.Clamp(Respect + delta, balanceConfig.minRespect, balanceConfig.maxRespect);
            if (!Mathf.Approximately(prev, Respect))
                OnRespectChanged?.Invoke(Respect);
        }

        public void ModifyTrash(float delta)
        {
            if (IsGameOver || balanceConfig == null) return;
            float prev = Trash;
            Trash = Mathf.Clamp(Trash + delta, 0f, balanceConfig.maxTrash);
            if (!Mathf.Approximately(prev, Trash))
                OnTrashChanged?.Invoke(Trash, balanceConfig.maxTrash);

            if (Trash >= balanceConfig.maxTrash)
                TriggerGameOver(GameOverReason.MaxTrash, "Рівень Трешу досяг 100%! Наказ про негайне виселення.");
        }

        public void AddReprimand(string reason = "")
        {
            if (IsGameOver || balanceConfig == null) return;
            Reprimands = Mathf.Clamp(Reprimands + 1, 0, balanceConfig.maxReprimands);
            OnReprimandsChanged?.Invoke(Reprimands, balanceConfig.maxReprimands);

            if (Reprimands >= balanceConfig.maxReprimands)
            {
                string detail = string.IsNullOrEmpty(reason)
                    ? "Отримано 3 офіційні догани від комендантші! Виселення з гуртожитку."
                    : $"3-тя догана ({reason}). Виселення з гуртожитку!";
                TriggerGameOver(GameOverReason.MaxReprimands, detail);
            }
        }

        public void ImproveStrength(int amount = 1)
        {
            Strength = Mathf.Clamp(Strength + amount, 1, 5);
            OnAttributesChanged?.Invoke(Strength, Stamina);
        }

        public void ImproveStamina(int amount = 1)
        {
            Stamina = Mathf.Clamp(Stamina + amount, 1, 5);
            OnAttributesChanged?.Invoke(Strength, Stamina);
        }

        public void SetNoiseLevel(float normalizedNoise)
        {
            CurrentNoiseLevel = Mathf.Clamp01(normalizedNoise);
        }

        private void TriggerGameOver(GameOverReason reason, string message)
        {
            if (IsGameOver) return;
            IsGameOver = true;
            LastGameOverReason = reason;
            OnGameOver?.Invoke(reason, message);
        }

        public void NotifyAll()
        {
            if (balanceConfig == null) return;
            OnEnergyChanged?.Invoke(Energy, balanceConfig.maxEnergy);
            OnRespectChanged?.Invoke(Respect);
            OnTrashChanged?.Invoke(Trash, balanceConfig.maxTrash);
            OnReprimandsChanged?.Invoke(Reprimands, balanceConfig.maxReprimands);
            OnAttributesChanged?.Invoke(Strength, Stamina);
            OnGymBuffChanged?.Invoke(HasGymBuff);
        }
    }
}
