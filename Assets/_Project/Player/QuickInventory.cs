using System;
using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dormitory7.Player
{
    public class QuickInventory : MonoBehaviour
    {
        public const int SlotCount = 4;

        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Transform smallHandAnchor;
        [SerializeField] private Transform twoHandAnchor;
        [SerializeField] private Transform holsterRoot;
        [SerializeField] private DormStatsManager statsManager;

        private readonly WorldItem[] _slots = new WorldItem[SlotCount];
        private int _activeSlotIndex;
        private WorldItem _carriedTwoHandedItem;

        private InputAction _slot1Action;
        private InputAction _slot2Action;
        private InputAction _slot3Action;
        private InputAction _slot4Action;
        private InputAction _scrollAction;
        private InputAction _dropAction;
        private InputAction _throwAction;
        private InputAction _useAction;

        private float _swingAnimTimer;
        private Vector3 _smallAnchorRestPos;

        public int ActiveSlotIndex => _activeSlotIndex;
        public bool IsCarryingTwoHanded => _carriedTwoHandedItem != null;
        public WorldItem CarriedTwoHandedItem => _carriedTwoHandedItem;

        public event Action OnInventoryChanged;
        public event Action<int> OnActiveSlotChanged;
        public event Action<WorldItem> OnItemUsed;
        public event Action<string> OnInventoryNotification;

        public void SetReferences(
            BalanceConfig config,
            Transform camTransform,
            Transform smallAnchor,
            Transform twoHandsAnchor,
            Transform holster,
            DormStatsManager stats)
        {
            balanceConfig = config;
            cameraTransform = camTransform;
            smallHandAnchor = smallAnchor;
            twoHandAnchor = twoHandsAnchor;
            holsterRoot = holster;
            statsManager = stats;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            if (smallHandAnchor != null)
                _smallAnchorRestPos = smallHandAnchor.localPosition;

            SetupInputActions();
        }

        private void SetupInputActions()
        {
            _slot1Action = new InputAction("Slot1", InputActionType.Button, "<Keyboard>/1");
            _slot2Action = new InputAction("Slot2", InputActionType.Button, "<Keyboard>/2");
            _slot3Action = new InputAction("Slot3", InputActionType.Button, "<Keyboard>/3");
            _slot4Action = new InputAction("Slot4", InputActionType.Button, "<Keyboard>/4");

            _scrollAction = new InputAction("SlotScroll", InputActionType.Value, "<Mouse>/scroll");

            _dropAction = new InputAction("Drop", InputActionType.Button);
            _dropAction.AddBinding("<Keyboard>/g");
            _dropAction.AddBinding("<Gamepad>/buttonNorth");

            _throwAction = new InputAction("Throw", InputActionType.Button);
            _throwAction.AddBinding("<Mouse>/rightButton");
            _throwAction.AddBinding("<Gamepad>/leftTrigger");

            _useAction = new InputAction("UseItem", InputActionType.Button);
            _useAction.AddBinding("<Mouse>/leftButton");
            _useAction.AddBinding("<Gamepad>/rightTrigger");
        }

        private void OnEnable()
        {
            _slot1Action?.Enable();
            _slot2Action?.Enable();
            _slot3Action?.Enable();
            _slot4Action?.Enable();
            _scrollAction?.Enable();
            _dropAction?.Enable();
            _throwAction?.Enable();
            _useAction?.Enable();
        }

        private void OnDisable()
        {
            _slot1Action?.Disable();
            _slot2Action?.Disable();
            _slot3Action?.Disable();
            _slot4Action?.Disable();
            _scrollAction?.Disable();
            _dropAction?.Disable();
            _throwAction?.Disable();
            _useAction?.Disable();
        }

        private void OnDestroy()
        {
            _slot1Action?.Dispose();
            _slot2Action?.Dispose();
            _slot3Action?.Dispose();
            _slot4Action?.Dispose();
            _scrollAction?.Dispose();
            _dropAction?.Dispose();
            _throwAction?.Dispose();
            _useAction?.Dispose();
        }

        private void Update()
        {
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            HandleSlotSelection();

            if (_dropAction.WasPressedThisFrame())
                DropActiveOrCarriedItem();

            if (_throwAction.WasPressedThisFrame())
                ThrowActiveOrCarriedItem();

            if (_useAction.WasPressedThisFrame())
                UseActiveItem();

            UpdateHandSwingAnimation();
        }

        private void HandleSlotSelection()
        {
            if (_slot1Action.WasPressedThisFrame()) SelectSlot(0);
            else if (_slot2Action.WasPressedThisFrame()) SelectSlot(1);
            else if (_slot3Action.WasPressedThisFrame()) SelectSlot(2);
            else if (_slot4Action.WasPressedThisFrame()) SelectSlot(3);

            float scrollY = _scrollAction.ReadValue<Vector2>().y;
            if (Mathf.Abs(scrollY) > 0.1f)
            {
                int dir = scrollY > 0f ? -1 : 1;
                int next = (_activeSlotIndex + dir + SlotCount) % SlotCount;
                SelectSlot(next);
            }
        }

        public void SelectSlot(int index)
        {
            if (index < 0 || index >= SlotCount) return;
            if (_activeSlotIndex == index) return;

            _activeSlotIndex = index;
            RefreshHeldVisuals();
            OnActiveSlotChanged?.Invoke(_activeSlotIndex);
            OnInventoryChanged?.Invoke();
        }

        public bool HasFreeSmallSlot() => FindFreeSlotIndex() >= 0;

        public ItemDefinition GetSlotItemDefinition(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return null;
            return _slots[slotIndex] != null ? _slots[slotIndex].Definition : null;
        }

        public WorldItem GetActiveWorldItem()
        {
            if (_carriedTwoHandedItem != null) return _carriedTwoHandedItem;
            return _slots[_activeSlotIndex];
        }

        public ItemDefinition GetActiveItemDefinition()
        {
            var item = GetActiveWorldItem();
            return item != null ? item.Definition : null;
        }

        public bool TryPickUp(WorldItem item)
        {
            if (item == null || item.Definition == null) return false;

            if (item.Definition.carryType == ItemCarryType.TwoHandedCarry)
            {
                if (_carriedTwoHandedItem != null)
                    return false;

                _carriedTwoHandedItem = item;
                _carriedTwoHandedItem.OnItemCooled += HandleCarriedItemCooled;
                RefreshHeldVisuals();
                OnInventoryChanged?.Invoke();
                return true;
            }

            if (_carriedTwoHandedItem != null)
                return false;

            int targetSlot = _slots[_activeSlotIndex] == null ? _activeSlotIndex : FindFreeSlotIndex();
            if (targetSlot < 0)
                return false;

            _slots[targetSlot] = item;
            RefreshHeldVisuals();
            OnInventoryChanged?.Invoke();
            return true;
        }

        private void HandleCarriedItemCooled(WorldItem item, ItemDefinition cooledDef)
        {
            if (item != _carriedTwoHandedItem) return;
            item.OnItemCooled -= HandleCarriedItemCooled;

            // Fixed decision #1: once the hot frying pan cools down, it becomes a SmallInventory item
            if (cooledDef != null && cooledDef.carryType == ItemCarryType.SmallInventory)
            {
                int freeSlot = _slots[_activeSlotIndex] == null ? _activeSlotIndex : FindFreeSlotIndex();
                if (freeSlot >= 0)
                {
                    _carriedTwoHandedItem = null;
                    _slots[freeSlot] = item;
                    SelectSlot(freeSlot);
                    RefreshHeldVisuals();
                    OnInventoryNotification?.Invoke($"{cooledDef.displayName} охолола й переміщена у слот {freeSlot + 1}.");
                }
                else
                {
                    OnInventoryNotification?.Invoke($"{cooledDef.displayName} охолола! Звільніть слот, щоб прибрати її.");
                }
                OnInventoryChanged?.Invoke();
            }
        }

        public bool DropActiveOrCarriedItem()
        {
            return ReleaseCurrentItem(isThrow: false);
        }

        public bool ThrowActiveOrCarriedItem()
        {
            return ReleaseCurrentItem(isThrow: true);
        }

        private bool ReleaseCurrentItem(bool isThrow)
        {
            WorldItem item = null;
            if (_carriedTwoHandedItem != null)
            {
                item = _carriedTwoHandedItem;
                _carriedTwoHandedItem.OnItemCooled -= HandleCarriedItemCooled;
                _carriedTwoHandedItem = null;
            }
            else if (_slots[_activeSlotIndex] != null)
            {
                item = _slots[_activeSlotIndex];
                _slots[_activeSlotIndex] = null;
            }

            if (item == null) return false;

            Transform origin = cameraTransform != null ? cameraTransform : transform;
            Vector3 spawnPos;
            Quaternion spawnRot;
            Vector3 linearVel;
            Vector3 angularVel;

            if (isThrow)
            {
                float spawnDist = balanceConfig.dropForwardOffset;
                if (TryRaycastNonPlayer(origin.position, origin.forward, spawnDist + 0.25f, out RaycastHit wallHit))
                    spawnDist = Mathf.Max(0.35f, wallHit.distance - 0.25f);

                spawnPos = origin.position + origin.forward * spawnDist;
                spawnRot = Quaternion.LookRotation(origin.forward, Vector3.up);
                float mult = item.Definition != null ? Mathf.Max(0.35f, item.Definition.throwForceMultiplier) : 1f;
                linearVel = origin.forward * (balanceConfig.baseThrowForce * mult) + Vector3.up * balanceConfig.throwUpwardBoost;
                angularVel = origin.right * 4.5f;
            }
            else
            {
                // Neat placement: if aiming at an upward surface (table, stove, floor) within reach, set it down cleanly
                float halfHeight = EstimateItemHalfHeight(item);
                spawnRot = Quaternion.Euler(0f, origin.eulerAngles.y, 0f);
                linearVel = Vector3.zero;
                angularVel = Vector3.zero;

                if (TryRaycastNonPlayer(origin.position, origin.forward, balanceConfig.interactDistance, out RaycastHit surfaceHit)
                    && surfaceHit.normal.y >= 0.65f)
                {
                    spawnPos = surfaceHit.point + Vector3.up * (halfHeight + 0.015f);
                }
                else
                {
                    float dropDist = Mathf.Min(0.8f, balanceConfig.dropForwardOffset);
                    if (TryRaycastNonPlayer(origin.position, origin.forward, dropDist + 0.25f, out RaycastHit obstacleHit))
                        dropDist = Mathf.Max(0.35f, obstacleHit.distance - 0.25f);

                    spawnPos = origin.position + origin.forward * dropDist - Vector3.up * 0.2f;
                    linearVel = origin.forward * 0.25f;
                }
            }

            item.ReleaseToWorld(spawnPos, spawnRot, linearVel, angularVel);
            RefreshHeldVisuals();
            OnInventoryChanged?.Invoke();
            return true;
        }

        private bool TryRaycastNonPlayer(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit closestHit)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            closestHit = default;
            if (hits == null || hits.Length == 0) return false;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null) continue;
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                closestHit = hits[i];
                return true;
            }
            return false;
        }

        private static float EstimateItemHalfHeight(WorldItem item)
        {
            if (item == null) return 0.12f;
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combined.Encapsulate(renderers[i].bounds);
                if (combined.extents.y > 0.01f)
                    return combined.extents.y;
            }
            return Mathf.Max(0.06f, item.transform.lossyScale.y * 0.5f);
        }

        public bool UseActiveItem()
        {
            WorldItem active = GetActiveWorldItem();
            if (active == null || active.Definition == null)
                return false;

            _swingAnimTimer = 0.18f;

            // Consumable food/coffee restores Energy right away
            if (active.Definition.HasTag(ItemTag.Food) && active.Definition.energyRestore > 0f && statsManager != null)
            {
                float restore = active.Definition.energyRestore;
                string itemName = active.Definition.displayName;
                statsManager.ModifyEnergy(restore);

                if (active == _carriedTwoHandedItem)
                {
                    _carriedTwoHandedItem.OnItemCooled -= HandleCarriedItemCooled;
                    _carriedTwoHandedItem = null;
                }
                else
                {
                    _slots[_activeSlotIndex] = null;
                }

                Destroy(active.gameObject);
                RefreshHeldVisuals();
                OnInventoryChanged?.Invoke();
                OnInventoryNotification?.Invoke($"Вжито: {itemName} (+{Mathf.RoundToInt(restore)}% Енергії)");
                return true;
            }

            OnItemUsed?.Invoke(active);
            return true;
        }

        private void RefreshHeldVisuals()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                WorldItem slotItem = _slots[i];
                if (slotItem == null) continue;

                bool showInRightHand = _carriedTwoHandedItem == null && i == _activeSlotIndex;
                if (showInRightHand && smallHandAnchor != null)
                {
                    slotItem.gameObject.SetActive(true);
                    slotItem.SetHeldState(smallHandAnchor, Vector3.zero, Vector3.zero);
                }
                else
                {
                    slotItem.SetHeldState(holsterRoot != null ? holsterRoot : transform, Vector3.zero, Vector3.zero);
                    slotItem.gameObject.SetActive(false);
                }
            }

            if (_carriedTwoHandedItem != null && twoHandAnchor != null)
            {
                _carriedTwoHandedItem.gameObject.SetActive(true);
                _carriedTwoHandedItem.SetHeldState(twoHandAnchor, Vector3.zero, Vector3.zero);
            }
        }

        private void UpdateHandSwingAnimation()
        {
            if (smallHandAnchor == null) return;
            if (_swingAnimTimer > 0f)
            {
                _swingAnimTimer = Mathf.Max(0f, _swingAnimTimer - Time.deltaTime);
                float t = Mathf.Sin((_swingAnimTimer / 0.18f) * Mathf.PI);
                smallHandAnchor.localPosition = _smallAnchorRestPos + new Vector3(-0.06f * t, -0.04f * t, 0.16f * t);
            }
            else
            {
                smallHandAnchor.localPosition = _smallAnchorRestPos;
            }
        }

        private int FindFreeSlotIndex()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == null)
                    return i;
            }
            return -1;
        }
    }
}
