using System;
using Dormitory7.Audio;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace Dormitory7.Features.Gym
{
    public enum GymStationType
    {
        BenchPress = 0,
        PullUpBar = 1
    }

    public class GymEquipment : MonoBehaviour, IInteractable
    {
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private GymStationType stationType = GymStationType.BenchPress;
        [SerializeField] private Transform animatedBarTransform;

        public static event Action<GymStationType, bool> OnAnyWorkoutCompleted;
        public static float ActiveExertionPulse { get; private set; }

        public GymStationType StationType => stationType;
        public bool IsWorkoutActive { get; private set; }
        public bool IsBossChallengeActive { get; private set; }
        public int CompletedReps { get; private set; }
        public int TargetReps => IsBossChallengeActive
            ? (balanceConfig != null ? Mathf.Max(4, balanceConfig.fridayBossChallengeReps) : 6)
            : (balanceConfig != null ? Mathf.Max(2, balanceConfig.gymRepsPerSet) : 4);
        public float BalanceValue { get; private set; } // -1..+1

        private DormStatsManager _stats;
        private PlayerContext _ctx;
        private float _timingCursor;
        private float _timingDir = 1f;
        private float _driftVelocity;
        private float _driftChangeTimer;
        private int _missCount;
        private float _safeHalfWidth;
        private float _sweetMin;
        private float _sweetMax;
        private Vector3 _barRestLocalPos;
        private float _barLiftVisual;

        private InputAction _balanceAxisAction;
        private InputAction _pushAction;
        private InputAction _cancelAction;

        public void Configure(BalanceConfig config, GymStationType type, Transform animBar = null)
        {
            balanceConfig = config;
            stationType = type;
            animatedBarTransform = animBar;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();

            if (animatedBarTransform != null)
                _barRestLocalPos = animatedBarTransform.localPosition;

            _balanceAxisAction = new InputAction("GymBalance", InputActionType.Value);
            _balanceAxisAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            _balanceAxisAction.AddBinding("<Gamepad>/leftStick/x");

            _pushAction = new InputAction("GymPush", InputActionType.Button);
            _pushAction.AddBinding("<Keyboard>/space");
            _pushAction.AddBinding("<Mouse>/leftButton");
            _pushAction.AddBinding("<Gamepad>/buttonSouth");

            _cancelAction = new InputAction("GymCancel", InputActionType.Button);
            _cancelAction.AddBinding("<Keyboard>/q");
            _cancelAction.AddBinding("<Gamepad>/buttonEast");
        }

        private void OnDisable()
        {
            if (IsWorkoutActive)
                StopWorkoutCleanly();
            ActiveExertionPulse = 0f;
        }

        private void OnDestroy()
        {
            ActiveExertionPulse = 0f;
            _balanceAxisAction?.Dispose();
            _pushAction?.Dispose();
            _cancelAction?.Dispose();
        }

        private void Start()
        {
            _stats = FindAnyObjectByType<DormStatsManager>();
        }

        public string GetPrompt(PlayerContext context)
        {
            if (IsWorkoutActive) return "";

            if (context.IsCarryingTwoHanded)
                return "Покладіть предмет із рук ([G]), щоб зайняти тренажер";

            if (context.Stats != null && !context.Stats.CanTrainInGym)
                return $"Замало енергії (<{Mathf.RoundToInt(balanceConfig.gymMinEnergyThreshold)}%) для тренування! Поїжте або випийте кави.";

            string buffTag = (context.Stats != null && context.Stats.HasGymBuff) ? " [БАФ КАВИ!]" : "";
            return stationType == GymStationType.BenchPress
                ? $"[E] Жим штанги з батарей (+Сила, +Респект){buffTag}"
                : $"[E] Підтягування на турніку (+Витривалість, +Респект){buffTag}";
        }

        public bool CanInteract(PlayerContext context)
        {
            if (IsWorkoutActive || context.IsCarryingTwoHanded) return false;
            return context.Stats != null && context.Stats.CanTrainInGym;
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            StartWorkout(context);
        }

        public void StartBossChallenge(PlayerContext context)
        {
            StartWorkout(context, isBossChallenge: true);
        }

        public void StartWorkout(PlayerContext context, bool isBossChallenge = false)
        {
            _ctx = context;
            if (_stats == null) _stats = context.Stats ?? FindAnyObjectByType<DormStatsManager>();

            IsWorkoutActive = true;
            IsBossChallengeActive = isBossChallenge;
            CompletedReps = 0;
            _missCount = 0;
            BalanceValue = 0f;
            _timingCursor = 0.05f;
            _timingDir = 1f;
            _driftVelocity = 0.42f;
            _driftChangeTimer = 0.9f;

            bool buffed = _stats != null && _stats.HasGymBuff;
            float bonus = buffed && balanceConfig != null ? balanceConfig.gymBuffZoneBonus : 0f;
            _safeHalfWidth = Mathf.Clamp(0.38f + bonus, 0.25f, 0.46f);
            float sweetHalf = Mathf.Clamp(0.16f + bonus * 0.6f, 0.12f, 0.28f);
            _sweetMin = 0.5f - sweetHalf;
            _sweetMax = 0.5f + sweetHalf;

            _ctx.Controller?.SetMovementLocked(true);
            _ctx.Inventory?.SetInputLocked(true);
            GameStateManager.Instance?.RegisterActiveQte(CancelWorkoutFromEsc);

            _balanceAxisAction.Enable();
            _pushAction.Enable();
            _cancelAction.Enable();

            PushHudQte();
        }

        private void CancelWorkoutFromEsc()
        {
            if (!IsWorkoutActive) return;
            StopWorkoutCleanly();
            HudController.Instance?.ShowToast("Тренування перервано.");
        }

        private void Update()
        {
            if (!IsWorkoutActive) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            if (_cancelAction.WasPressedThisFrame())
            {
                StopWorkoutCleanly();
                HudController.Instance?.ShowToast("Тренування перервано.");
                return;
            }

            // 1. Balance simulation (A / D steers against barbell tilt)
            _driftChangeTimer -= Time.deltaTime;
            if (_driftChangeTimer <= 0f)
            {
                _driftChangeTimer = Random.Range(0.75f, 1.35f);
                float intensity = 0.45f + CompletedReps * 0.10f;
                _driftVelocity = (Random.value < 0.5f ? -1f : 1f) * intensity;
            }

            float playerSteer = _balanceAxisAction.ReadValue<float>() * 1.45f;
            BalanceValue += (_driftVelocity + playerSteer) * Time.deltaTime;

            // Fail if barbell completely loses balance!
            if (Mathf.Abs(BalanceValue) >= 1.0f)
            {
                TriggerWorkoutFail();
                return;
            }

            // 2. Timing needle oscillation
            float needleSpeed = 1.05f + CompletedReps * 0.14f;
            _timingCursor += _timingDir * needleSpeed * Time.deltaTime;
            if (_timingCursor >= 1f) { _timingCursor = 1f; _timingDir = -1f; }
            else if (_timingCursor <= 0f) { _timingCursor = 0f; _timingDir = 1f; }

            if (_pushAction.WasPressedThisFrame())
            {
                bool inBalance = Mathf.Abs(BalanceValue) <= _safeHalfWidth * 2f;
                bool inTiming = _timingCursor >= _sweetMin && _timingCursor <= _sweetMax;
                ProcessRepAttempt(inBalance && inTiming);
            }

            // Smooth barbell lift animation & final-reps screen exertion pulse
            if (animatedBarTransform != null)
            {
                _barLiftVisual = Mathf.MoveTowards(_barLiftVisual, 0f, Time.deltaTime * 2.2f);
                animatedBarTransform.localPosition = _barRestLocalPos + Vector3.up * (_barLiftVisual * 0.35f);
            }

            bool isFinalReps = CompletedReps >= Mathf.Max(1, TargetReps - 2);
            if (isFinalReps)
            {
                float progressBoost = Mathf.Clamp01((float)(CompletedReps - (TargetReps - 3)) / 2f);
                ActiveExertionPulse = Mathf.Clamp01(0.45f + progressBoost * 0.45f + _barLiftVisual * 0.25f);
            }
            else
            {
                ActiveExertionPulse = Mathf.MoveTowards(ActiveExertionPulse, 0f, Time.deltaTime * 3f);
            }

            if (IsWorkoutActive)
                PushHudQte();
        }

        public void ProcessRepAttempt(bool success)
        {
            if (!IsWorkoutActive) return;

            if (success)
            {
                CompletedReps++;
                _barLiftVisual = 1f;
                AudioManager.Instance?.PlayBarbellClang(transform.position);
                if (CompletedReps >= TargetReps)
                    TriggerWorkoutSuccess();
            }
            else
            {
                _missCount++;
                AudioManager.Instance?.PlayBarbellClang(transform.position);
                BalanceValue += (BalanceValue >= 0f ? 0.25f : -0.25f);
                if (_missCount >= 3 || Mathf.Abs(BalanceValue) >= 1.0f)
                    TriggerWorkoutFail();
            }
        }

        public void TriggerWorkoutSuccess()
        {
            bool wasBossChallenge = IsBossChallengeActive;
            StopWorkoutCleanly();
            if (_stats == null || balanceConfig == null) return;

            bool buffed = _stats.HasGymBuff;
            float energyCost = balanceConfig.gymWorkoutEnergyCost * _stats.StaminaDrainMultiplier * (buffed ? 0.75f : 1f);
            _stats.ModifyEnergy(-energyCost);

            float respectReward = wasBossChallenge
                ? balanceConfig.fridayBossChallengeRespect
                : balanceConfig.gymSuccessRespect;
            _stats.ModifyRespect(respectReward);

            if (stationType == GymStationType.BenchPress)
            {
                _stats.ImproveStrength(1);
                string prefix = wasBossChallenge ? "ФІНАЛЬНИЙ ЖИМ З БОСОМ ВИГРАНО!" : "Жим виконано!";
                HudController.Instance?.ShowToast($"{prefix} Сила: {_stats.Strength}/5 • +{Mathf.RoundToInt(respectReward)} Респекту!");
            }
            else
            {
                _stats.ImproveStamina(1);
                HudController.Instance?.ShowToast($"Турнік виконано! Витривалість: {_stats.Stamina}/5 • +{Mathf.RoundToInt(respectReward)} Респекту!");
            }

            OnAnyWorkoutCompleted?.Invoke(stationType, wasBossChallenge);
        }

        public void TriggerWorkoutFail()
        {
            StopWorkoutCleanly();
            if (_stats == null || balanceConfig == null) return;

            _stats.ModifyEnergy(-balanceConfig.gymFailEnergyPenalty);
            _stats.ModifyRespect(-balanceConfig.gymFailRespectPenalty);

            string msg = stationType == GymStationType.BenchPress
                ? $"Придавило штангою! -{Mathf.RoundToInt(balanceConfig.gymFailEnergyPenalty)}% Енергії, -{Mathf.RoundToInt(balanceConfig.gymFailRespectPenalty)} Респекту!"
                : $"Зірвався з турніка! -{Mathf.RoundToInt(balanceConfig.gymFailEnergyPenalty)}% Енергії, -{Mathf.RoundToInt(balanceConfig.gymFailRespectPenalty)} Респекту!";
            HudController.Instance?.ShowToast(msg);
        }

        private void StopWorkoutCleanly()
        {
            GameStateManager.Instance?.UnregisterActiveQte(CancelWorkoutFromEsc);
            IsWorkoutActive = false;
            IsBossChallengeActive = false;
            ActiveExertionPulse = 0f;
            _balanceAxisAction.Disable();
            _pushAction.Disable();
            _cancelAction.Disable();
            _ctx.Controller?.SetMovementLocked(false);
            _ctx.Inventory?.SetInputLocked(false);
            if (animatedBarTransform != null)
                animatedBarTransform.localPosition = _barRestLocalPos;
            HudController.Instance?.HideQte();
        }

        private void PushHudQte()
        {
            string title = IsBossChallengeActive
                ? $"ФІНАЛЬНИЙ ВИКЛИК БОСА: ЖИМ ({CompletedReps}/{TargetReps})"
                : (stationType == GymStationType.BenchPress
                    ? $"ЖИМ ШТАНГИ ({CompletedReps}/{TargetReps})"
                    : $"ТУРНІК ({CompletedReps}/{TargetReps})");

            HudController.Instance?.ShowQte(new QteDisplayState
            {
                Title = title,
                Instruction = "[A / D] Тримайте баланс у центрі • [Space / ЛКМ] Ривок у зеленій зоні • [Esc] Вийти",
                ProgressNormalized = (float)CompletedReps / TargetReps,
                ShowTimingBar = true,
                TimingCursor = _timingCursor,
                SweetSpotMin = _sweetMin,
                SweetSpotMax = _sweetMax,
                ShowBalanceBar = true,
                BalanceValue = BalanceValue,
                BalanceSafeHalfWidth = _safeHalfWidth
            });
        }
    }
}
