using System;
using System.Collections.Generic;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Features.Cooking;
using Dormitory7.Features.Repairs;
using Dormitory7.Interaction;
using Dormitory7.Player;
using Dormitory7.UI;
using TMPro;
using UnityEngine;

namespace Dormitory7.NPC
{
    public enum KomendantState
    {
        Waiting = 0,
        Patrolling = 1,
        Investigating = 2,
        Chasing = 3,
        BribeWindow = 4,
        Reprimanding = 5
    }

    public class KomendantAI : BaseNpcAgent, IInteractable
    {
        [Header("Komendant Config")]
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private Light flashlightSpot;
        [SerializeField] private Transform eyePoint;
        [SerializeField] private TextMeshPro overheadLabel;
        [SerializeField] private LightSwitchInteractable room104LightSwitch;

        public KomendantState State { get; private set; } = KomendantState.Waiting;
        public int BribesUsedThisWeek { get; private set; }
        public float DangerLevel { get; private set; }
        public bool IsPatrolActive => State != KomendantState.Waiting;
        public bool HasCheckedRoom104ThisPatrol { get; private set; }
        public bool IsFridayClimax => dayTimeController != null && dayTimeController.CurrentDay >= 5;

        public float EffectivePatrolSpeed =>
            (balanceConfig != null ? balanceConfig.komendantPatrolSpeed : 2.6f) *
            (IsFridayClimax && balanceConfig != null ? balanceConfig.fridayKomendantSpeedMultiplier : 1f);

        public float EffectiveChaseSpeed =>
            (balanceConfig != null ? balanceConfig.komendantChaseSpeed : 4.4f) *
            (IsFridayClimax && balanceConfig != null ? balanceConfig.fridayKomendantSpeedMultiplier : 1f);

        public float EffectiveVisionBaseRange =>
            (balanceConfig != null ? balanceConfig.komendantVisionRange : 11.5f) *
            (IsFridayClimax && balanceConfig != null ? balanceConfig.fridayKomendantVisionMultiplier : 1f);

        public bool CanAcceptBribe =>
            BribesUsedThisWeek < (balanceConfig != null ? balanceConfig.komendantWeeklyBribeLimit : 1);

        public event Action<KomendantState> OnStateChanged;
        public event Action<string> OnReprimandIssued;

        private FirstPersonController _playerController;
        private QuickInventory _playerInventory;
        private Transform _playerTransform;
        private Vector3 _investigateTarget;
        private float _suspicionTimer;
        private float _bribeWindowTimer;
        private float _postBribeGraceTimer;
        private float _perceptionTimer;
        private bool _sawPlayerEnterHiding;
        private bool _wasPlayerHiddenLastCheck;
        private bool _reprimandIssuedTonight;

        private readonly List<NpcWaypoint> _activeRoute = new List<NpcWaypoint>();
        private int _routeIndex = -1;
        private float _routeWaitTimer;
        private bool _waitingAtRoutePoint;

        private static readonly Color PatrolColor = new Color(0.95f, 0.78f, 0.30f, 1f);
        private static readonly Color InvestigateColor = new Color(1.0f, 0.55f, 0.16f, 1f);
        private static readonly Color ChaseColor = new Color(0.95f, 0.20f, 0.18f, 1f);
        private static readonly Color BribeColor = new Color(0.45f, 0.90f, 0.55f, 1f);

        public void Configure(
            BalanceConfig config,
            DormStatsManager stats,
            Light flashlight,
            Transform eye,
            TextMeshPro label,
            LightSwitchInteractable room104Switch)
        {
            balanceConfig = config;
            statsManager = stats;
            flashlightSpot = flashlight;
            eyePoint = eye;
            overheadLabel = label;
            room104LightSwitch = room104Switch;
        }

        protected override void Awake()
        {
            base.Awake();
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();

            walkSpeed = balanceConfig.komendantPatrolSpeed;
            runSpeed = balanceConfig.komendantChaseSpeed;
            if (navAgent != null)
                navAgent.speed = walkSpeed;
        }

        protected override void Start()
        {
            base.Start();
            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();
            if (room104LightSwitch == null)
                room104LightSwitch = FindAnyObjectByType<LightSwitchInteractable>();

            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
            {
                _playerTransform = playerGo.transform;
                _playerController = playerGo.GetComponent<FirstPersonController>();
                _playerInventory = playerGo.GetComponent<QuickInventory>();
            }

            EnterWaitingState(warpHome: true);
        }

        protected override void OnCurfewWarningTriggered()
        {
            // HUD already shows the 21:30 warning toast via HudController.HandleCurfewWarning
            UpdateOverheadLabel();
        }

        protected override void OnCurfewStartedTriggered()
        {
            BeginCurfewPatrol();
        }

        protected override void OnNewDayStarted(int newDay)
        {
            EnterWaitingState(warpHome: true);
        }

        public void EnterWaitingState(bool warpHome)
        {
            _reprimandIssuedTonight = false;
            HasCheckedRoom104ThisPatrol = false;
            _sawPlayerEnterHiding = false;
            _bribeWindowTimer = 0f;
            _postBribeGraceTimer = 0f;
            _routeIndex = -1;
            _activeRoute.Clear();
            DangerLevel = 0f;

            StopMovement();
            SetState(KomendantState.Waiting);
            SetFlashlight(false);

            if (warpHome)
                WarpToZone(NpcZone.CorridorRight);
        }

        public void BeginCurfewPatrol()
        {
            if (State == KomendantState.Reprimanding) return;

            _reprimandIssuedTonight = false;
            HasCheckedRoom104ThisPatrol = false;
            _waitingAtRoutePoint = false;

            // Fixed Decision #5: Standard patrol covers Corridor -> Kitchens -> Washrooms -> Room 104.
            // Toilets & Shower are NOT included unless triggered by active hazard / noise / sabotage!
            _activeRoute.Clear();
            var standardZones = new[]
            {
                NpcZone.CorridorRight,
                NpcZone.KitchenRight,
                NpcZone.WashroomRight,
                NpcZone.CorridorCenter,
                NpcZone.Room104,
                NpcZone.CorridorLeft,
                NpcZone.KitchenLeft,
                NpcZone.WashroomLeft
            };

            for (int i = 0; i < standardZones.Length; i++)
            {
                var wp = NpcWaypoint.GetFirstInZone(standardZones[i]);
                if (wp != null) _activeRoute.Add(wp);
            }

            // Append sanitary zone ONLY if an active broken hazard exists there!
            AppendSanitaryZonesIfTriggered();

            SetFlashlight(true);
            SetState(KomendantState.Patrolling);

            if (_activeRoute.Count > 0)
            {
                _routeIndex = 0;
                MoveToWaypoint(_activeRoute[0], EffectivePatrolSpeed);
            }

            if (IsFridayClimax)
            {
                HudController.Instance?.ShowToast("П'ЯТНИЧНА ГЕНЕРАЛЬНА ПЕРЕВІРКА! Зінаїда Петрівна йде швидше й дивиться уважніше!");
            }
        }

        private void AppendSanitaryZonesIfTriggered()
        {
            var hazards = FindObjectsByType<RepairableHazard>();
            bool leftToiletTriggered = false;
            bool rightToiletTriggered = false;

            for (int i = 0; i < hazards.Length; i++)
            {
                var h = hazards[i];
                if (h == null || !h.IsBroken || h.Type != HazardType.CloggedToilet) continue;
                if (h.transform.position.x < 0f) leftToiletTriggered = true;
                else rightToiletTriggered = true;
            }

            if (leftToiletTriggered)
            {
                var wp = NpcWaypoint.GetFirstInZone(NpcZone.ToiletLeftMen);
                if (wp != null && !_activeRoute.Contains(wp)) _activeRoute.Add(wp);
            }
            if (rightToiletTriggered)
            {
                var wp = NpcWaypoint.GetFirstInZone(NpcZone.ToiletRightWomen);
                if (wp != null && !_activeRoute.Contains(wp)) _activeRoute.Add(wp);
            }
        }

        /// <summary>
        /// External hook for sanitary sabotage (e.g. yeast in toilet) or loud noise inside a bathroom.
        /// </summary>
        public void NotifyDisturbance(Vector3 worldPos, string reason = "")
        {
            if (State == KomendantState.Chasing || State == KomendantState.BribeWindow || State == KomendantState.Reprimanding)
                return;

            SetFlashlight(true);
            _investigateTarget = worldPos;
            _suspicionTimer = balanceConfig.komendantSuspicionSeconds;
            SetState(KomendantState.Investigating);
            MoveToPosition(worldPos, balanceConfig.komendantPatrolSpeed * 1.2f);

            if (!string.IsNullOrEmpty(reason))
                HudController.Instance?.ShowToast($"Зінаїда Петрівна почула тривогу: {reason}!");
        }

        protected override void Update()
        {
            base.Update();

            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            UpdateLabelOrientation();

            if (_postBribeGraceTimer > 0f)
                _postBribeGraceTimer = Mathf.Max(0f, _postBribeGraceTimer - Time.deltaTime);

            UpdateDangerLevel();

            if (State == KomendantState.Waiting)
            {
                CheckBurnedSmokeTrigger();
                return;
            }

            if (State == KomendantState.BribeWindow)
            {
                UpdateBribeWindow();
                return;
            }

            if (State == KomendantState.Reprimanding)
                return;

            // Active states: Patrolling / Investigating / Chasing
            UpdatePerception();

            if (State == KomendantState.Patrolling)
                UpdateCurfewPatrol();
            else if (State == KomendantState.Investigating)
                UpdateInvestigation();
            else if (State == KomendantState.Chasing)
                UpdateChasing();
        }

        private void UpdatePerception()
        {
            _perceptionTimer -= Time.deltaTime;
            if (_perceptionTimer > 0f) return;
            _perceptionTimer = 0.12f;

            if (_playerTransform == null || _postBribeGraceTimer > 0f)
                return;

            bool isHiddenNow = _playerController != null && _playerController.IsHidden;
            if (isHiddenNow && !_wasPlayerHiddenLastCheck)
            {
                // Player just entered a hiding spot — remember whether Zinaida saw them enter!
                _sawPlayerEnterHiding = State == KomendantState.Chasing || CanSeePlayerDirectly(ignoreHidingFlag: true);
            }
            else if (!isHiddenNow)
            {
                _sawPlayerEnterHiding = false;
            }
            _wasPlayerHiddenLastCheck = isHiddenNow;

            // 1. Direct Flashlight Vision Cone
            if (CanSeePlayerDirectly(ignoreHidingFlag: false))
            {
                if (State != KomendantState.Chasing)
                {
                    SetState(KomendantState.Chasing);
                    HudController.Instance?.ShowToast("Зінаїда Петрівна ПОМІТИЛА ВАС! Тікайте або відкупіться Шоколадкою/Кавою!");
                }
                MoveToPosition(_playerTransform.position, EffectiveChaseSpeed);
                return;
            }

            // 2. Hearing (scales with DormStatsManager.CurrentNoiseLevel; crouching = nearly silent!)
            if (!isHiddenNow && statsManager != null && IsPlayerOnSameFloor())
            {
                float hearingRadius = balanceConfig.komendantHearingMaxRadius * statsManager.CurrentNoiseLevel;
                float dist = Vector3.Distance(transform.position, _playerTransform.position);
                if (hearingRadius > 1.8f && dist <= hearingRadius)
                {
                    if (State != KomendantState.Chasing)
                    {
                        _investigateTarget = _playerTransform.position;
                        _suspicionTimer = balanceConfig.komendantSuspicionSeconds;
                        if (State != KomendantState.Investigating)
                            SetState(KomendantState.Investigating);
                        MoveToPosition(_investigateTarget, balanceConfig.komendantPatrolSpeed * 1.15f);
                    }
                    return;
                }
            }

            // 3. Burned Food Smoke
            CheckBurnedSmokeTrigger();
        }

        private void CheckBurnedSmokeTrigger()
        {
            if (State == KomendantState.Chasing || State == KomendantState.Investigating) return;
            var pans = FindObjectsByType<FryingPanState>();
            for (int i = 0; i < pans.Length; i++)
            {
                if (pans[i] != null && pans[i].State == DishState.Burned && pans[i].IsOnActiveBurner)
                {
                    NotifyDisturbance(pans[i].transform.position, "дим від згорілої їжі на кухні");
                    return;
                }
            }
        }

        public bool CanSeePlayerDirectly(bool ignoreHidingFlag = false)
        {
            if (_playerTransform == null || balanceConfig == null) return false;
            if (!IsPlayerOnSameFloor()) return false;

            bool isHidden = _playerController != null && _playerController.IsHidden;
            if (!ignoreHidingFlag && isHidden && !_sawPlayerEnterHiding)
                return false;

            Vector3 origin = eyePoint != null ? eyePoint.position : (transform.position + Vector3.up * 1.55f);
            Vector3 target = _playerTransform.position + Vector3.up * 1.1f;
            Vector3 toTarget = target - origin;
            float dist = toTarget.magnitude;

            float effectiveRange = EffectiveVisionBaseRange;

            // Turning OFF Room 104 light significantly lowers visibility inside Room 104
            if (IsPositionInsideRoom104(_playerTransform.position) && room104LightSwitch != null && !room104LightSwitch.IsOn)
                effectiveRange *= balanceConfig.komendantDarkRoomVisionMultiplier;

            if (_playerController != null && _playerController.IsCrouching)
                effectiveRange *= 0.8f;

            if (dist > effectiveRange || dist < 0.05f) return false;

            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;
            Vector3 flatToTarget = toTarget;
            flatToTarget.y = 0f;
            float angle = Vector3.Angle(flatForward, flatToTarget);
            if (angle > balanceConfig.komendantVisionHalfAngle)
                return false;

            // Wall line-of-sight check (cannot see through walls!)
            if (Physics.Raycast(origin, toTarget / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && !hit.collider.transform.IsChildOf(_playerTransform) && !hit.collider.transform.IsChildOf(transform))
                    return false;
            }

            return true;
        }

        private bool IsPlayerOnSameFloor()
        {
            if (_playerTransform == null) return false;
            return Mathf.Abs(_playerTransform.position.y - transform.position.y) < 1.8f;
        }

        private static bool IsPositionInsideRoom104(Vector3 pos)
        {
            return pos.x >= -11.0f && pos.x <= -5.0f && pos.z >= 1.6f && pos.z <= 7.6f && pos.y >= -0.5f;
        }

        private void UpdateCurfewPatrol()
        {
            if (_routeIndex < 0 || _routeIndex >= _activeRoute.Count) return;

            if (_waitingAtRoutePoint)
            {
                _routeWaitTimer -= Time.deltaTime;
                if (_routeWaitTimer <= 0f)
                {
                    _waitingAtRoutePoint = false;
                    AdvanceCurfewRoute();
                }
                return;
            }

            if (HasArrived)
            {
                NpcWaypoint reached = _activeRoute[_routeIndex];
                if (reached != null && reached.Zone == NpcZone.Room104)
                {
                    HasCheckedRoom104ThisPatrol = true;
                    EvaluateRoom104AndFloorTrash();
                    if (State != KomendantState.Patrolling) return;
                }

                _waitingAtRoutePoint = true;
                _routeWaitTimer = reached != null ? Mathf.Max(0.8f, reached.DefaultWaitSeconds) : 1.5f;
            }
        }

        private void AdvanceCurfewRoute()
        {
            _routeIndex++;
            if (_routeIndex >= _activeRoute.Count)
            {
                FinishCurfewPatrol();
                return;
            }

            MoveToWaypoint(_activeRoute[_routeIndex], balanceConfig.komendantPatrolSpeed);
        }

        private void EvaluateRoom104AndFloorTrash()
        {
            if (_reprimandIssuedTonight || statsManager == null || balanceConfig == null) return;

            if (statsManager.Trash > balanceConfig.komendantTrashThreshold)
            {
                IssueReprimand($"Критичний безлад на поверсі (Треш {Mathf.RoundToInt(statsManager.Trash)}% > 50%)");
            }
        }

        private void FinishCurfewPatrol()
        {
            if (!_reprimandIssuedTonight && statsManager != null && balanceConfig != null &&
                statsManager.Trash > balanceConfig.komendantTrashThreshold)
            {
                // GDD 5.6 Edge Case: even if player hid in basement, Trash > 50% gives absentee reprimand
                IssueReprimand($"Заочна догана: після обходу Треш на 1-му поверсі {Mathf.RoundToInt(statsManager.Trash)}% > 50%");
                return;
            }

            HudController.Instance?.ShowToast("Обхід Зінаїди Петрівни пройдено чисто! Можна спати в кімнаті №104.");
            EnterWaitingState(warpHome: false);
        }

        private void UpdateInvestigation()
        {
            if (HasArrived)
            {
                _suspicionTimer -= Time.deltaTime;
                transform.Rotate(Vector3.up, 42f * Time.deltaTime); // Scans room with flashlight
                if (_suspicionTimer <= 0f)
                {
                    if (dayTimeController != null && dayTimeController.IsCurfewActive)
                    {
                        SetState(KomendantState.Patrolling);
                        if (_routeIndex >= 0 && _routeIndex < _activeRoute.Count)
                            MoveToWaypoint(_activeRoute[_routeIndex], balanceConfig.komendantPatrolSpeed);
                        else
                            BeginCurfewPatrol();
                    }
                    else
                    {
                        EnterWaitingState(warpHome: false);
                    }
                }
            }
        }

        private void UpdateChasing()
        {
            if (_playerTransform == null) return;

            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            if (dist <= balanceConfig.komendantCatchDistance && IsPlayerOnSameFloor())
            {
                HandlePlayerCaught();
                return;
            }

            if (CanSeePlayerDirectly(ignoreHidingFlag: false))
            {
                _investigateTarget = _playerTransform.position;
                _suspicionTimer = balanceConfig.komendantSuspicionSeconds;
                MoveToPosition(_playerTransform.position, EffectiveChaseSpeed);
            }
            else
            {
                // Lost sight (e.g. player turned corner or hid unobserved) -> investigate last seen position
                SetState(KomendantState.Investigating);
                MoveToPosition(_investigateTarget, balanceConfig.komendantPatrolSpeed * 1.25f);
            }
        }

        public void HandlePlayerCaught()
        {
            if (_reprimandIssuedTonight) return;

            StopMovement();

            // Check if player holds a Bribe item (Chocolate/Coffee) and weekly bribe limit not used
            if (CanAcceptBribe && PlayerHasBribeItem())
            {
                _bribeWindowTimer = balanceConfig.komendantBribeWindowSeconds;
                SetState(KomendantState.BribeWindow);
                HudController.Instance?.ShowToast(
                    $"Зінаїда Петрівна схопила вас! Швидко натисніть [E] на ній, щоб відкупитися Шоколадкою/Кавою ({Mathf.CeilToInt(_bribeWindowTimer)}с)!");
                return;
            }

            IssueReprimand("Спіймано після відбою (22:00)");
        }

        private void UpdateBribeWindow()
        {
            if (_playerTransform != null)
            {
                Vector3 toP = _playerTransform.position - transform.position;
                toP.y = 0f;
                if (toP.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toP.normalized), Time.deltaTime * 8f);
            }

            _bribeWindowTimer -= Time.deltaTime;
            if (_bribeWindowTimer <= 0f)
            {
                IssueReprimand("Не встигли пояснитися з комендантшею після відбою");
            }
        }

        public void IssueReprimand(string reason)
        {
            if (_reprimandIssuedTonight) return;
            _reprimandIssuedTonight = true;

            SetState(KomendantState.Reprimanding);
            StopMovement();

            OnReprimandIssued?.Invoke(reason);
            statsManager?.AddReprimand(reason);

            if (statsManager != null && statsManager.IsGameOver)
                return;

            // Force end of day and return player to Room 104
            ReturnPlayerToRoom104();
            string msg = $"ДОГАНА! ({reason}). День примусово завершено — ранок наступного дня (+25% Енергії).";
            if (dayTimeController != null)
                dayTimeController.SleepAndAdvanceDay(forcedByKomendant: true, customSummary: msg);
            else
                EnterWaitingState(warpHome: true);
        }

        private void ReturnPlayerToRoom104()
        {
            if (_playerController == null) return;
            if (_playerController.IsHidden && _playerController.ActiveHidingSpot != null)
                _playerController.ActiveHidingSpot.ExitHiding();

            var cc = _playerController.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _playerController.transform.position = new Vector3(-7.8f, 0.05f, 4.4f);
            if (cc != null) cc.enabled = true;
        }

        private bool PlayerHasBribeItem()
        {
            if (_playerInventory == null) return false;
            return _playerInventory.HasItemWithId("chocolate") || _playerInventory.HasItemWithId("coffee");
        }

        private bool TryConsumeBribeFromInventory(QuickInventory inv, out string consumedName)
        {
            consumedName = "Шоколадка";
            if (inv == null) return false;

            if (inv.HasItemWithId("chocolate") && inv.TryConsumeItemById("chocolate"))
            {
                consumedName = "Шоколадка";
                return true;
            }
            if (inv.HasItemWithId("coffee") && inv.TryConsumeItemById("coffee"))
            {
                consumedName = "Кава";
                return true;
            }
            return false;
        }

        // ==================== IInteractable (Bribe with Chocolate / Coffee) ====================

        public string GetPrompt(PlayerContext context)
        {
            if (!CanAcceptBribe)
                return "Комендантша Зінаїда Петрівна (відкуп уже використано на цей тиждень!)";

            if (PlayerHasBribeItem())
                return "[E] Передати «хабар» Зінаїді Петрівні (Шоколадка / Кава — 1 раз/тиждень)";

            return "Комендантша Зінаїда Петрівна — тримайтеся подалі після 22:00!";
        }

        public bool CanInteract(PlayerContext context)
        {
            return CanAcceptBribe && PlayerHasBribeItem();
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;

            QuickInventory inv = context.Inventory ?? _playerInventory;
            if (TryConsumeBribeFromInventory(inv, out string itemName))
            {
                BribesUsedThisWeek++;
                _postBribeGraceTimer = 12f;
                float relief = balanceConfig != null ? balanceConfig.komendantBribeTrashRelief : 10f;
                statsManager?.ModifyTrash(-relief);

                HudController.Instance?.ShowToast(
                    $"Зінаїда Петрівна прийняла «{itemName}»! Догану скасовано (-{Mathf.RoundToInt(relief)}% Трешу, 12с спокою).");

                if (dayTimeController != null && dayTimeController.IsCurfewActive)
                {
                    SetState(KomendantState.Patrolling);
                    if (_routeIndex >= 0 && _routeIndex < _activeRoute.Count)
                        MoveToWaypoint(_activeRoute[_routeIndex], balanceConfig.komendantPatrolSpeed);
                }
                else
                {
                    EnterWaitingState(warpHome: false);
                }
            }
        }

        // ==================== Visuals & Helpers ====================

        private void SetState(KomendantState newState)
        {
            State = newState;
            UpdateOverheadLabel();
            OnStateChanged?.Invoke(newState);
        }

        private void SetFlashlight(bool on)
        {
            if (flashlightSpot != null)
                flashlightSpot.enabled = on;
        }

        private void UpdateDangerLevel()
        {
            if (State == KomendantState.Waiting || _playerTransform == null || !IsPlayerOnSameFloor())
            {
                DangerLevel = 0f;
                return;
            }
            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            float proximity = Mathf.Clamp01(1f - dist / 14f);
            DangerLevel = State == KomendantState.Chasing ? Mathf.Max(0.75f, proximity) : proximity * 0.65f;
        }

        private void UpdateOverheadLabel()
        {
            if (overheadLabel == null) return;
            string status = State switch
            {
                KomendantState.Waiting => "Очікування (до 22:00)",
                KomendantState.Patrolling => "ОБХІД ПОВЕРХУ",
                KomendantState.Investigating => "ПІДОЗРА!",
                KomendantState.Chasing => "ПЕРЕСЛІДУВАННЯ!",
                KomendantState.BribeWindow => "ВІДКУП [E]!",
                KomendantState.Reprimanding => "ДОГАНА!",
                _ => ""
            };
            overheadLabel.text = $"ЗІНАЇДА ПЕТРІВНА\n({status})";
            overheadLabel.color = State switch
            {
                KomendantState.Chasing or KomendantState.Reprimanding => ChaseColor,
                KomendantState.Investigating => InvestigateColor,
                KomendantState.BribeWindow => BribeColor,
                _ => PatrolColor
            };
        }

        private void UpdateLabelOrientation()
        {
            if (overheadLabel == null || Camera.main == null) return;
            Vector3 dir = overheadLabel.transform.position - Camera.main.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                overheadLabel.transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
