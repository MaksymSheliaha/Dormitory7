using System;
using System.Collections.Generic;
using Dormitory7.Audio;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Features.Cockroaches;
using Dormitory7.Features.Cooking;
using Dormitory7.Features.Quests;
using Dormitory7.Interaction;
using Dormitory7.UI;
using TMPro;
using UnityEngine;

namespace Dormitory7.NPC
{
    public enum NeighborState
    {
        Roaming = 0,
        SneakingToPan = 1,
        CarryingStolenPan = 2,
        Fleeing = 3
    }

    public class HungryNeighborAI : BaseNpcAgent, IInteractable
    {
        [Header("Neighbor Identity & Territory")]
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private string neighborName = "Голодний сусід Толік";
        [SerializeField] private NpcZone homeZone = NpcZone.Room106;
        [SerializeField] private NpcZone preferredKitchenZone = NpcZone.KitchenLeft;
        [SerializeField] private NpcZone preferredWashroomZone = NpcZone.WashroomLeft;
        [SerializeField] private Transform carryAnchor;
        [SerializeField] private TextMeshPro overheadLabel;
        [SerializeField] private GameObject messPrefab;
        [SerializeField] private ItemDefinition[] barterRewards;

        public NeighborState State { get; private set; } = NeighborState.Roaming;
        public string NeighborName => neighborName;
        public bool IsCarryingPan => _stolenPan != null;
        public WorldItem CarriedPan => _stolenPan;
        public int SpawnedMessCount { get; private set; }
        public int BarterCompletedCount { get; private set; }

        public ItemDefinition CurrentBarterOffer =>
            barterRewards != null && barterRewards.Length > 0
                ? barterRewards[_barterIndex % barterRewards.Length]
                : null;

        public event Action<HungryNeighborAI, WorldItem> OnPanStolen;
        public event Action<HungryNeighborAI, WorldItem> OnPanReturned;
        public event Action<HungryNeighborAI, ItemDefinition> OnBarterCompleted;

        private int _barterIndex;

        private Transform _playerTransform;
        private StoveBurner _targetBurner;
        private WorldItem _stolenPan;
        private float _scanTimer;
        private float _messTimer;
        private float _roamWaitTimer;
        private bool _waitingAtRoamPoint;
        private int _roamIndex;
        private readonly List<NpcZone> _roamZones = new List<NpcZone>();

        private static readonly Color RoamColor = new Color(0.84f, 0.88f, 0.72f, 1f);
        private static readonly Color ThiefColor = new Color(0.98f, 0.56f, 0.18f, 1f);
        private static readonly Color FleeColor = new Color(0.95f, 0.30f, 0.28f, 1f);

        public void ConfigureBarterRewards(ItemDefinition[] rewards)
        {
            barterRewards = rewards;
            _barterIndex = 0;
        }

        public void Configure(
            BalanceConfig config,
            DormStatsManager stats,
            string displayName,
            NpcZone home,
            NpcZone kitchen,
            NpcZone washroom,
            Transform handAnchor,
            TextMeshPro label,
            GameObject litterPrefab)
        {
            balanceConfig = config;
            statsManager = stats;
            neighborName = displayName;
            homeZone = home;
            preferredKitchenZone = kitchen;
            preferredWashroomZone = washroom;
            carryAnchor = handAnchor;
            overheadLabel = label;
            messPrefab = litterPrefab;
        }

        protected override void Awake()
        {
            base.Awake();
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            walkSpeed = balanceConfig.neighborWalkSpeed;
            runSpeed = balanceConfig.neighborFleeSpeed;
        }

        private void OnEnable()
        {
            PlayerToolController.OnPlayerMeleeStrike += HandlePlayerMeleeStrike;
            StoveBurner.OnPlayerTookPanFromBurner += HandlePlayerTookPanFromBurner;
        }

        private void OnDisable()
        {
            PlayerToolController.OnPlayerMeleeStrike -= HandlePlayerMeleeStrike;
            StoveBurner.OnPlayerTookPanFromBurner -= HandlePlayerTookPanFromBurner;
        }

        protected override void Start()
        {
            base.Start();
            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();

            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
                _playerTransform = playerGo.transform;

            _roamZones.Clear();
            _roamZones.Add(preferredKitchenZone);
            _roamZones.Add(preferredWashroomZone);
            _roamZones.Add(NpcZone.CorridorCenter);
            _roamZones.Add(homeZone);

            _messTimer = balanceConfig != null ? balanceConfig.neighborMessCooldownSeconds : 55f;
            WarpToZone(homeZone);
            StartRoaming();
        }

        protected override void OnNewDayStarted(int newDay)
        {
            if (_stolenPan != null)
                DropStolenPanAtFeet();
            SpawnedMessCount = 0;
            if (barterRewards != null && barterRewards.Length > 0)
                _barterIndex = (newDay - 1) % barterRewards.Length;
            WarpToZone(homeZone);
            StartRoaming();
        }

        protected override void Update()
        {
            base.Update();

            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            UpdateLabelOrientation();

            switch (State)
            {
                case NeighborState.Roaming:
                    UpdateRoaming();
                    UpdatePanScan();
                    UpdateLitterTimer();
                    break;

                case NeighborState.SneakingToPan:
                    UpdateSneakingToPan();
                    break;

                case NeighborState.CarryingStolenPan:
                    UpdateCarryingStolenPan();
                    break;

                case NeighborState.Fleeing:
                    UpdateFleeing();
                    break;
            }
        }

        private void StartRoaming()
        {
            _targetBurner = null;
            _waitingAtRoamPoint = false;
            SetState(NeighborState.Roaming);
            if (_roamZones.Count > 0)
            {
                _roamIndex = (_roamIndex + 1) % _roamZones.Count;
                MoveToZone(_roamZones[_roamIndex], balanceConfig.neighborWalkSpeed);
            }
        }

        private void UpdateRoaming()
        {
            if (_waitingAtRoamPoint)
            {
                _roamWaitTimer -= Time.deltaTime;
                if (_roamWaitTimer <= 0f)
                {
                    _waitingAtRoamPoint = false;
                    _roamIndex = (_roamIndex + 1) % _roamZones.Count;
                    MoveToZone(_roamZones[_roamIndex], balanceConfig.neighborWalkSpeed);
                }
                return;
            }

            if (HasArrived)
            {
                _waitingAtRoamPoint = true;
                _roamWaitTimer = 3.0f;
            }
        }

        private void UpdateLitterTimer()
        {
            if (messPrefab == null || SpawnedMessCount >= 2) return;
            _messTimer -= Time.deltaTime;
            if (_messTimer > 0f) return;

            _messTimer = balanceConfig != null ? balanceConfig.neighborMessCooldownSeconds : 55f;
            // Drop litter only when near kitchen or washroom
            var kWp = NpcWaypoint.GetFirstInZone(preferredKitchenZone);
            var wWp = NpcWaypoint.GetFirstInZone(preferredWashroomZone);
            bool nearMessZone =
                (kWp != null && Vector3.Distance(transform.position, kWp.Position) < 4.5f) ||
                (wWp != null && Vector3.Distance(transform.position, wWp.Position) < 4.5f);

            if (nearMessZone)
                SpawnLitterHere();
        }

        public MessSpot SpawnLitterHere()
        {
            if (messPrefab == null) return null;
            SpawnedMessCount++;
            Vector3 pos = new Vector3(transform.position.x, 0.12f, transform.position.z);
            GameObject go = Instantiate(messPrefab, pos, Quaternion.identity);
            go.name = $"NeighborMess_{neighborName}_{SpawnedMessCount}";
            return go.GetComponent<MessSpot>();
        }

        private void UpdatePanScan()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = 0.75f;

            StoveBurner candidate = FindUnattendedCookingBurner();
            if (candidate != null)
                BeginStealingFromBurner(candidate);
        }

        public StoveBurner FindUnattendedCookingBurner()
        {
            float unattendedDist = balanceConfig != null ? balanceConfig.neighborUnattendedPlayerDist : 7.5f;

            for (int i = 0; i < StoveBurner.AllBurners.Count; i++)
            {
                var burner = StoveBurner.AllBurners[i];
                if (burner == null || !burner.HasPan || burner.MountedPanState == null) continue;
                if (!burner.MountedPanState.HasEdibleFood) continue;

                // Prefer burners in reasonable distance on 1st floor
                if (Vector3.Distance(transform.position, burner.transform.position) > 26f)
                    continue;

                bool playerFarEnough = _playerTransform == null ||
                    Vector3.Distance(_playerTransform.position, burner.transform.position) >= unattendedDist;

                if (playerFarEnough)
                    return burner;
            }
            return null;
        }

        public void BeginStealingFromBurner(StoveBurner burner)
        {
            if (burner == null || !burner.HasPan) return;
            _targetBurner = burner;
            SetState(NeighborState.SneakingToPan);
            MoveToPosition(burner.transform.position, balanceConfig.neighborWalkSpeed * 1.15f);
        }

        private void UpdateSneakingToPan()
        {
            if (_targetBurner == null || !_targetBurner.HasPan || _targetBurner.MountedPanState == null || !_targetBurner.MountedPanState.HasEdibleFood)
            {
                StartRoaming();
                return;
            }

            if (Vector3.Distance(transform.position, _targetBurner.transform.position) <= 1.9f || HasArrived)
            {
                StealPanFromTargetBurner();
            }
        }

        public bool StealPanFromTargetBurner()
        {
            if (_targetBurner == null || !_targetBurner.HasPan) return false;

            WorldItem pan = _targetBurner.DetachPanForNpc();
            _targetBurner = null;
            if (pan == null) return false;

            _stolenPan = pan;
            Transform anchor = carryAnchor != null ? carryAnchor : transform;
            _stolenPan.gameObject.SetActive(true);
            _stolenPan.SetHeldState(anchor, Vector3.zero, Vector3.zero);

            SetState(NeighborState.CarryingStolenPan);
            MoveToZone(homeZone, balanceConfig.neighborWalkSpeed * 1.25f);
            OnPanStolen?.Invoke(this, _stolenPan);

            HudController.Instance?.ShowToast(
                $"ТРИВОГА НА КУХНІ! {neighborName} поцупив сковорідку з їжею! Перехопіть ([ЛКМ] або [E])!");
            return true;
        }

        private void UpdateCarryingStolenPan()
        {
            if (_stolenPan == null)
            {
                StartRoaming();
                return;
            }

            if (HasArrived)
            {
                // Neighbor stashed the stolen pan in their room!
                WorldItem stashed = _stolenPan;
                _stolenPan = null;
                Vector3 dropPos = transform.position + transform.forward * 0.6f + Vector3.up * 0.35f;
                stashed.ReleaseToWorld(dropPos, Quaternion.identity, Vector3.zero, Vector3.zero);

                var panState = stashed.GetComponent<FryingPanState>();
                if (panState != null)
                    panState.OwnerNpcName = neighborName;

                HudController.Instance?.ShowToast($"{neighborName} доніс вашу сковорідку до себе в кімнату! Шукайте її там.");
                StartRoaming();
            }
        }

        private void UpdateFleeing()
        {
            if (HasArrived)
                StartRoaming();
        }

        private void HandlePlayerMeleeStrike(WorldItem weapon, Vector3 hitPoint, Collider hitCollider)
        {
            if (State == NeighborState.Fleeing) return;

            bool directHit = hitCollider != null && hitCollider.transform.IsChildOf(transform);
            bool closeEnough = _playerTransform != null && Vector3.Distance(_playerTransform.position, transform.position) <= 3.1f &&
                               Vector3.Distance(hitPoint, transform.position + Vector3.up * 0.9f) <= 1.8f;

            if (!directHit && !closeEnough) return;

            OnStruckByPlayer();
        }

        public void OnStruckByPlayer()
        {
            if (_stolenPan != null)
            {
                WorldItem returned = DropStolenPanAtFeet();
                float bonus = balanceConfig != null ? balanceConfig.neighborReclaimRespectBonus : 4f;
                statsManager?.ModifyRespect(bonus);
                OnPanReturned?.Invoke(this, returned);
                HudController.Instance?.ShowToast(
                    $"Влучно! {neighborName} кинув сковорідку й чкурнув! (+{Mathf.RoundToInt(bonus)} Респекту)");
            }
            else
            {
                HudController.Instance?.ShowToast($"{neighborName}: «Ей, першокурсник, ти чого махаєшся?!»");
            }

            SetState(NeighborState.Fleeing);
            MoveToZone(homeZone, balanceConfig.neighborFleeSpeed);
        }

        private void HandlePlayerTookPanFromBurner(StoveBurner burner, WorldItem pan, FryingPanState panState)
        {
            if (panState == null || string.IsNullOrEmpty(panState.OwnerNpcName)) return;
            if (panState.OwnerNpcName != neighborName) return;

            // Player stole this neighbor's own pan while neighbor is watching!
            if (_playerTransform != null && Vector3.Distance(transform.position, _playerTransform.position) <= 6.5f)
            {
                if (!IsPlayerStrongerOrRespected())
                {
                    float pen = balanceConfig != null ? balanceConfig.neighborTheftRespectPenalty : 6f;
                    statsManager?.ModifyRespect(-pen);
                    HudController.Instance?.ShowToast(
                        $"{neighborName} помітив крадіжку своєї сковорідки! Ви слабші за нього (-{Mathf.RoundToInt(pen)} Респекту).");
                }
                else
                {
                    HudController.Instance?.ShowToast($"{neighborName} бачив, як ви забрали сковорідку, але побоявся сперечатись!");
                }
                panState.OwnerNpcName = "";
            }
        }

        public WorldItem DropStolenPanAtFeet()
        {
            if (_stolenPan == null) return null;
            WorldItem pan = _stolenPan;
            _stolenPan = null;
            Vector3 dropPos = transform.position + transform.forward * 0.55f + Vector3.up * 0.45f;
            pan.ReleaseToWorld(dropPos, Quaternion.identity, transform.forward * 0.5f, Vector3.zero);
            return pan;
        }

        private bool IsPlayerStrongerOrRespected()
        {
            if (statsManager == null || balanceConfig == null) return false;
            return statsManager.Strength >= balanceConfig.neighborIntimidateMinStrength ||
                   statsManager.Respect >= balanceConfig.neighborIntimidateMinRespect;
        }

        // ==================== IInteractable ====================

        public string GetPrompt(PlayerContext context)
        {
            if (State == NeighborState.CarryingStolenPan && _stolenPan != null)
            {
                return IsPlayerStrongerOrRespected()
                    ? $"[E] Відібрати сковорідку у {neighborName} (Сила/Респект достатні)"
                    : $"[E] Спробувати забрати сковорідку (ви слабші — краще вдарте ЛКМ!)";
            }

            ItemDefinition offer = CurrentBarterOffer;
            if (offer != null && context.Inventory != null && context.Inventory.HasItemWithId("dorm_junk"))
            {
                return $"[E] Бартер із {neighborName}: Мотлох → «{offer.displayName}»";
            }

            return offer != null
                ? $"[E] Поговорити: {neighborName} (обміняє Мотлох на «{offer.displayName}»)"
                : $"[E] Поговорити: {neighborName}";
        }

        public bool CanInteract(PlayerContext context) => State != NeighborState.Fleeing;

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;

            if (State == NeighborState.CarryingStolenPan && _stolenPan != null)
            {
                if (IsPlayerStrongerOrRespected())
                {
                    WorldItem pan = _stolenPan;
                    _stolenPan = null;
                    if (context.Inventory != null && !context.IsCarryingTwoHanded)
                        context.Inventory.TryPickUp(pan);
                    else
                        pan.ReleaseToWorld(transform.position + transform.forward * 0.6f + Vector3.up * 0.4f, Quaternion.identity, Vector3.zero, Vector3.zero);

                    float bonus = balanceConfig != null ? balanceConfig.neighborReclaimRespectBonus : 4f;
                    statsManager?.ModifyRespect(bonus);
                    OnPanReturned?.Invoke(this, pan);
                    HudController.Instance?.ShowToast(
                        $"{neighborName} злякався вашого авторитету і повернув сковорідку! (+{Mathf.RoundToInt(bonus)} Респекту)");

                    SetState(NeighborState.Fleeing);
                    MoveToZone(homeZone, balanceConfig.neighborFleeSpeed);
                }
                else
                {
                    float pen = balanceConfig != null ? balanceConfig.neighborTheftRespectPenalty : 6f;
                    statsManager?.ModifyRespect(-pen);
                    HudController.Instance?.ShowToast(
                        $"{neighborName} відштовхнув вас! (-{Mathf.RoundToInt(pen)} Респекту). Прокачайте Силу або вдарте капцем [ЛКМ]!");
                }
                return;
            }

            // MoSCoW Should: Simple Barter (Dorm Junk -> Coffee / Chocolate / Blue Tape / Food!)
            ItemDefinition offer = CurrentBarterOffer;
            if (offer != null && context.Inventory != null && context.Inventory.HasItemWithId("dorm_junk"))
            {
                if (context.Inventory.TryConsumeItemById("dorm_junk"))
                {
                    bool added = context.Inventory.TrySpawnAndAddItem(offer, new Vector3(0.16f, 0.16f, 0.16f));
                    if (!added)
                    {
                        // Drop right in front of player if slots full
                        GameObject fallback = offer.worldPrefab != null
                            ? Instantiate(offer.worldPrefab)
                            : GameObject.CreatePrimitive(PrimitiveType.Cube);
                        fallback.name = $"WorldItem_{offer.itemId}_Barter";
                        fallback.transform.position = transform.position + transform.forward * 0.6f + Vector3.up * 0.8f;
                        if (offer.worldPrefab == null)
                            fallback.transform.localScale = new Vector3(0.16f, 0.16f, 0.16f);
                        if (fallback.GetComponent<Rigidbody>() == null) fallback.AddComponent<Rigidbody>();
                        var wi = fallback.GetComponent<WorldItem>() ?? fallback.AddComponent<WorldItem>();
                        wi.Initialize(offer);
                    }

                    BarterCompletedCount++;
                    _barterIndex++;
                    float bonus = balanceConfig != null ? balanceConfig.barterRespectBonus : 2f;
                    statsManager?.ModifyRespect(bonus);
                    AudioManager.Instance?.PlayQuestChime();
                    OnBarterCompleted?.Invoke(this, offer);

                    HudController.Instance?.ShowToast(
                        $"БАРТЕР із {neighborName}: Мотлох → «{offer.displayName}»! (+{Mathf.RoundToInt(bonus)} Респекту)");
                    QuestManager.Instance?.AddChatMessage(
                        neighborName,
                        $"Дякую за підгон! Тримай «{offer.displayName}», ще знайдеш мотлох — заходь!",
                        isAlert: false);
                }
                return;
            }

            string barterHint = offer != null
                ? $"Принеси якийсь знайдений Мотлох — обміняю на «{offer.displayName}»!"
                : "Слухай, на кухні часом ніхто картоплю без нагляду не лишив?";
            HudController.Instance?.ShowToast($"{neighborName}: «{barterHint}»");
        }

        private void SetState(NeighborState newState)
        {
            State = newState;
            UpdateOverheadLabel();
        }

        private void UpdateOverheadLabel()
        {
            if (overheadLabel == null) return;
            string status = State switch
            {
                NeighborState.SneakingToPan => "КРАДЕТЬСЯ НА КУХНЮ!",
                NeighborState.CarryingStolenPan => "УКРАВ СКОВОРІДКУ!",
                NeighborState.Fleeing => "ТІКАЄ!",
                _ => "Голодний сусід"
            };
            overheadLabel.text = $"{neighborName}\n({status})";
            overheadLabel.color = State switch
            {
                NeighborState.CarryingStolenPan or NeighborState.SneakingToPan => ThiefColor,
                NeighborState.Fleeing => FleeColor,
                _ => RoamColor
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
