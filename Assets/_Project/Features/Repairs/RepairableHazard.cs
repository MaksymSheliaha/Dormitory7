using System;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using Dormitory7.Player;
using Dormitory7.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dormitory7.Features.Repairs
{
    public enum HazardType
    {
        PipeBurst = 0,
        CloggedToilet = 1,
        ElectricalPanelTrip = 2
    }

    public enum HazardState
    {
        Working = 0,
        Broken = 1,
        Repairing = 2,
        Repaired = 3,
        TemporarilyPatched = 4
    }

    public class RepairableHazard : MonoBehaviour, IInteractable
    {
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private HazardType hazardType = HazardType.PipeBurst;
        [SerializeField] private bool startBroken = true;
        [SerializeField] private GameObject brokenEffectVisual;
        [SerializeField] private GameObject tempTapeVisual;
        [SerializeField] private Light[] affectedLights;

        public HazardType Type => hazardType;
        public HazardState State { get; private set; } = HazardState.Working;
        public float RepairProgress { get; private set; }
        public float TempPatchRemaining { get; private set; }
        public bool IsBroken => State == HazardState.Broken || State == HazardState.Repairing;

        public event Action<RepairableHazard, HazardState> OnHazardStateChanged;

        private DormStatsManager _stats;
        private PlayerContext _activeRepairCtx;
        private bool _isTemporaryTapeRepair;
        private float _qteCursor;
        private float _qteDir = 1f;
        private float[] _originalLightIntensities;

        private InputAction _qteHitAction;
        private InputAction _qteCancelAction;

        public void Configure(BalanceConfig config, HazardType type, bool brokenInitially, GameObject brokenVis, GameObject tapeVis, Light[] lights = null)
        {
            balanceConfig = config;
            hazardType = type;
            startBroken = brokenInitially;
            brokenEffectVisual = brokenVis;
            tempTapeVisual = tapeVis;
            affectedLights = lights;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();

            _qteHitAction = new InputAction("RepairHit", InputActionType.Button);
            _qteHitAction.AddBinding("<Mouse>/leftButton");
            _qteHitAction.AddBinding("<Keyboard>/space");
            _qteHitAction.AddBinding("<Gamepad>/buttonSouth");

            _qteCancelAction = new InputAction("RepairCancel", InputActionType.Button);
            _qteCancelAction.AddBinding("<Keyboard>/q");
            _qteCancelAction.AddBinding("<Gamepad>/buttonEast");
        }

        private void OnDestroy()
        {
            _qteHitAction?.Dispose();
            _qteCancelAction?.Dispose();
        }

        private void Start()
        {
            _stats = FindAnyObjectByType<DormStatsManager>();

            if (affectedLights != null && affectedLights.Length > 0)
            {
                _originalLightIntensities = new float[affectedLights.Length];
                for (int i = 0; i < affectedLights.Length; i++)
                    _originalLightIntensities[i] = affectedLights[i] != null ? affectedLights[i].intensity : 5.5f;
            }

            if (startBroken)
                TriggerBreakdown(silent: true);
            else
                SetState(HazardState.Working);
        }

        private void Update()
        {
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            if (State == HazardState.Broken || State == HazardState.Repairing)
            {
                if (_stats != null && balanceConfig != null)
                {
                    float perMin = hazardType switch
                    {
                        HazardType.PipeBurst => balanceConfig.pipeBurstTrashPerMinute,
                        HazardType.CloggedToilet => balanceConfig.cloggedToiletTrashPerMinute,
                        HazardType.ElectricalPanelTrip => balanceConfig.electricalTripTrashPerMinute,
                        _ => 2.0f
                    };
                    _stats.ModifyTrash((perMin / 60f) * Time.deltaTime);
                }

                AnimateBrokenVisual();
            }

            if (State == HazardState.TemporarilyPatched)
            {
                TempPatchRemaining -= Time.deltaTime;
                if (TempPatchRemaining <= 0f)
                {
                    TriggerBreakdown(silent: false);
                    HudController.Instance?.ShowToast("Синя ізолента не витримала! Трубу знову прорвало — потрібен Гайковий ключ!");
                }
            }

            if (State == HazardState.Repairing)
            {
                UpdateRepairQte();
            }
        }

        public void TriggerBreakdown(bool silent = false)
        {
            RepairProgress = 0f;
            TempPatchRemaining = 0f;
            SetState(HazardState.Broken);
            if (!silent)
                HudController.Instance?.ShowToast($"АВАРІЯ: {GetHazardDisplayName()}! Рівень Трешу зростає.");
        }

        private void SetState(HazardState newState)
        {
            State = newState;
            bool showBroken = newState == HazardState.Broken || newState == HazardState.Repairing;
            if (brokenEffectVisual != null)
                brokenEffectVisual.SetActive(showBroken);

            if (tempTapeVisual != null)
                tempTapeVisual.SetActive(newState == HazardState.TemporarilyPatched);

            UpdateAffectedLights(dimmed: showBroken && hazardType == HazardType.ElectricalPanelTrip);
            OnHazardStateChanged?.Invoke(this, newState);
        }

        private void UpdateAffectedLights(bool dimmed)
        {
            if (affectedLights == null || _originalLightIntensities == null) return;
            for (int i = 0; i < affectedLights.Length; i++)
            {
                if (affectedLights[i] == null) continue;
                affectedLights[i].intensity = dimmed ? _originalLightIntensities[i] * 0.42f : _originalLightIntensities[i];
            }
        }

        private void AnimateBrokenVisual()
        {
            if (brokenEffectVisual == null) return;
            float pulse = 0.85f + 0.25f * Mathf.Sin(Time.time * 12f);
            brokenEffectVisual.transform.localScale = Vector3.one * pulse;
        }

        public string GetHazardDisplayName()
        {
            return hazardType switch
            {
                HazardType.PipeBurst => "Прорив труби в умивальнику",
                HazardType.CloggedToilet => "Забитий туалет",
                HazardType.ElectricalPanelTrip => "Вибитий електрощиток",
                _ => "Побутова аварія"
            };
        }

        public string GetPrompt(PlayerContext context)
        {
            if (State == HazardState.Working || State == HazardState.Repaired)
                return $"{GetHazardDisplayName()} — справно";

            if (State == HazardState.TemporarilyPatched)
            {
                bool hasWrench = context.Inventory != null && context.Inventory.HasItemWithId("wrench");
                return hasWrench
                    ? $"[E] Капітально закрутити трубу ключем (ізолента тримає ще {Mathf.CeilToInt(TempPatchRemaining)}с)"
                    : $"Тимчасово замотано ізолентою ({Mathf.CeilToInt(TempPatchRemaining)}с) — знайдіть Гайковий ключ";
            }

            if (State == HazardState.Repairing)
                return "Ремонт у процесі...";

            return hazardType switch
            {
                HazardType.PipeBurst => GetPipePrompt(context),
                HazardType.CloggedToilet => HasTool(context, "plunger")
                    ? "[E] Прочистити унітаз вантузом (QTE)"
                    : "Унітаз забитий! Знайдіть Вантуз.",
                HazardType.ElectricalPanelTrip => "[E] Перезапустити електрощиток (QTE)",
                _ => "[E] Відремонтувати"
            };
        }

        private string GetPipePrompt(PlayerContext context)
        {
            if (HasTool(context, "wrench"))
                return "[E] Полагодити трубу Гайковим ключем надовго (QTE)";
            if (HasTool(context, "blue_tape"))
                return "[E] Тимчасово замотати трубу Синьою ізолентою (QTE)";
            return "Прорив труби! Потрібна Синя ізолента (тимчасово) або Гайковий ключ (надовго).";
        }

        private static bool HasTool(PlayerContext context, string itemId)
        {
            return context.Inventory != null && context.Inventory.HasItemWithId(itemId);
        }

        public bool CanInteract(PlayerContext context)
        {
            if (State == HazardState.Repairing || State == HazardState.Working || State == HazardState.Repaired)
                return false;

            if (State == HazardState.TemporarilyPatched)
                return hazardType == HazardType.PipeBurst && HasTool(context, "wrench");

            return hazardType switch
            {
                HazardType.PipeBurst => HasTool(context, "wrench") || HasTool(context, "blue_tape"),
                HazardType.CloggedToilet => HasTool(context, "plunger"),
                HazardType.ElectricalPanelTrip => true,
                _ => false
            };
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;

            bool useTempTape = hazardType == HazardType.PipeBurst && !HasTool(context, "wrench") && HasTool(context, "blue_tape");
            BeginRepairQte(context, useTempTape);
        }

        public void BeginRepairQte(PlayerContext context, bool temporaryTape)
        {
            _activeRepairCtx = context;
            _isTemporaryTapeRepair = temporaryTape;
            RepairProgress = 0f;
            _qteCursor = 0.1f;
            _qteDir = 1f;

            SetState(HazardState.Repairing);
            _activeRepairCtx.Controller?.SetMovementLocked(true);
            _activeRepairCtx.Inventory?.SetInputLocked(true);
            GameStateManager.Instance?.RegisterActiveQte(CancelRepairQte);

            _qteHitAction.Enable();
            _qteCancelAction.Enable();
            PushHudQte();
        }

        private void UpdateRepairQte()
        {
            float strengthMult = _stats != null ? _stats.StrengthSpeedMultiplier : 1f;

            // Oscillate timing needle
            _qteCursor += _qteDir * 1.15f * Time.deltaTime;
            if (_qteCursor >= 1f) { _qteCursor = 1f; _qteDir = -1f; }
            else if (_qteCursor <= 0f) { _qteCursor = 0f; _qteDir = 1f; }

            if (_qteCancelAction.WasPressedThisFrame())
            {
                CancelRepairQte();
                return;
            }

            if (_qteHitAction.WasPressedThisFrame())
            {
                bool inSweetSpot = _qteCursor >= 0.32f && _qteCursor <= 0.68f;
                ApplyQteHit(inSweetSpot);
            }
            else if (_qteHitAction.IsPressed())
            {
                // Holding LMB/Space also steadily turns the wrench / winds tape (scaled by Strength!)
                float rate = (balanceConfig != null ? balanceConfig.repairBaseProgressSpeed : 0.28f) * strengthMult;
                AdvanceRepairProgress(rate * Time.deltaTime);
            }

            if (State == HazardState.Repairing)
                PushHudQte();
        }

        public void ApplyQteHit(bool inSweetSpot)
        {
            if (State != HazardState.Repairing) return;
            float strengthMult = _stats != null ? _stats.StrengthSpeedMultiplier : 1f;
            float gain = (inSweetSpot ? 0.34f : 0.10f) * strengthMult;
            AdvanceRepairProgress(gain);
        }

        private void AdvanceRepairProgress(float amount)
        {
            RepairProgress = Mathf.Clamp01(RepairProgress + amount);
            if (RepairProgress >= 1f)
                CompleteRepair();
        }

        private void CompleteRepair()
        {
            EndRepairLocks();

            if (_stats != null && balanceConfig != null)
            {
                _stats.ModifyEnergy(-balanceConfig.repairEnergyCost);
                _stats.ModifyTrash(-balanceConfig.repairTrashRelief);
            }

            if (_isTemporaryTapeRepair)
            {
                TempPatchRemaining = balanceConfig != null ? balanceConfig.tempTapePatchSeconds : 55f;
                SetState(HazardState.TemporarilyPatched);
                HudController.Instance?.ShowToast($"Трубу тимчасово замотано ізолентою ({Mathf.RoundToInt(TempPatchRemaining)}с)! Знайдіть ключ.");
            }
            else
            {
                SetState(HazardState.Repaired);
                HudController.Instance?.ShowToast($"{GetHazardDisplayName()} — ВІДРЕМОНТОВАНО надовго! (-Треш)");
            }
        }

        public void CancelRepairQte()
        {
            if (State != HazardState.Repairing) return;
            EndRepairLocks();
            SetState(HazardState.Broken);
        }

        private void EndRepairLocks()
        {
            GameStateManager.Instance?.UnregisterActiveQte(CancelRepairQte);
            _qteHitAction.Disable();
            _qteCancelAction.Disable();
            _activeRepairCtx.Controller?.SetMovementLocked(false);
            _activeRepairCtx.Inventory?.SetInputLocked(false);
            HudController.Instance?.HideQte();
        }

        private void PushHudQte()
        {
            string modeLabel = _isTemporaryTapeRepair ? "Тимчасове намотування ізоленти" : $"Ремонт: {GetHazardDisplayName()}";
            int str = _stats != null ? _stats.Strength : 1;
            HudController.Instance?.ShowQte(new QteDisplayState
            {
                Title = modeLabel,
                Instruction = $"[ЛКМ / Space] у зеленій зоні або утримуйте (Сила {str}/5) • [Esc] Скасувати",
                ProgressNormalized = RepairProgress,
                ShowTimingBar = true,
                TimingCursor = _qteCursor,
                SweetSpotMin = 0.32f,
                SweetSpotMax = 0.68f,
                ShowBalanceBar = false
            });
        }
    }
}
