using System;
using Dormitory7.Data;
using UnityEngine;

namespace Dormitory7.Interaction
{
    [RequireComponent(typeof(Rigidbody))]
    public class WorldItem : MonoBehaviour, IInteractable
    {
        [SerializeField] private ItemDefinition definition;
        [SerializeField] private Renderer[] tintRenderers;
        [SerializeField] private bool startCoolingOnlyAfterPickup = true;

        private Rigidbody _rb;
        private Collider[] _colliders;
        private float _coolingTimer;
        private bool _hasStartedCooling;
        private bool _isHeld;

        public ItemDefinition Definition => definition;
        public bool IsHeld => _isHeld;
        public bool IsCooling => definition != null && definition.IsHot && _hasStartedCooling && _coolingTimer > 0f;
        public float RemainingCoolingTime => _coolingTimer;

        public event Action<WorldItem, ItemDefinition> OnItemCooled;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);

            if (tintRenderers == null || tintRenderers.Length == 0)
                tintRenderers = GetComponentsInChildren<Renderer>(true);

            ApplyDefinition(definition);
        }

        public void Initialize(ItemDefinition itemDef)
        {
            definition = itemDef;
            ApplyDefinition(definition);
        }

        private void ApplyDefinition(ItemDefinition def)
        {
            if (def == null) return;

            if (_rb != null)
                _rb.mass = Mathf.Max(0.2f, def.physicsMass);

            _coolingTimer = def.IsHot ? Mathf.Max(1f, def.coolingDurationSeconds) : 0f;
            _hasStartedCooling = def.IsHot && !startCoolingOnlyAfterPickup;
            UpdateTintColor(def.tintColor);
        }

        public void StartCooling()
        {
            if (definition != null && definition.IsHot)
                _hasStartedCooling = true;
        }

        private void Update()
        {
            if (definition == null || !definition.IsHot || !_hasStartedCooling) return;

            _coolingTimer -= Time.deltaTime;
            if (_coolingTimer <= 0f)
            {
                _coolingTimer = 0f;
                _hasStartedCooling = false;
                ItemDefinition cooledDef = definition.coolsToItem;
                definition = cooledDef;
                ApplyDefinition(definition);
                OnItemCooled?.Invoke(this, cooledDef);
            }
        }

        public string GetPrompt(PlayerContext context)
        {
            if (definition == null) return "[E] Підібрати";

            if (definition.carryType == ItemCarryType.TwoHandedCarry)
            {
                if (context.IsCarryingTwoHanded)
                    return $"Руки зайняті ({definition.displayName})";

                string hotSuffix = definition.IsHot
                    ? (_hasStartedCooling ? $" (гаряча, {Mathf.CeilToInt(_coolingTimer)}с)" : " (гаряча!)")
                    : "";
                return $"[E] Взяти в руки: {definition.displayName}{hotSuffix}";
            }

            if (context.IsCarryingTwoHanded)
                return $"Спершу покладіть предмет із рук ([G])";

            if (context.Inventory != null && !context.Inventory.HasFreeSmallSlot())
                return $"Інвентар повний ({definition.displayName})";

            return $"[E] Підібрати: {definition.displayName}";
        }

        public bool CanInteract(PlayerContext context)
        {
            if (definition == null || _isHeld || context.Inventory == null)
                return false;

            if (definition.carryType == ItemCarryType.TwoHandedCarry)
                return !context.IsCarryingTwoHanded;

            return !context.IsCarryingTwoHanded && context.Inventory.HasFreeSmallSlot();
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            context.Inventory.TryPickUp(this);
        }

        public void SetHeldState(Transform anchor, Vector3 localOffset, Vector3 localEuler)
        {
            _isHeld = true;
            if (definition != null && definition.IsHot)
                _hasStartedCooling = true;

            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
                _rb.detectCollisions = false;
            }

            SetCollidersEnabled(false);

            transform.SetParent(anchor, false);
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.Euler(localEuler);
        }

        public void ReleaseToWorld(Vector3 worldPos, Quaternion worldRot, Vector3 linearVel, Vector3 angularVel)
        {
            _isHeld = false;
            gameObject.SetActive(true);
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(worldPos, worldRot);

            SetCollidersEnabled(true);

            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.detectCollisions = true;
                _rb.linearVelocity = linearVel;
                _rb.angularVelocity = angularVel;
            }
        }

        private void SetCollidersEnabled(bool enabledState)
        {
            if (_colliders == null) return;
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                    _colliders[i].enabled = enabledState;
            }
        }

        private void UpdateTintColor(Color color)
        {
            if (tintRenderers == null) return;
            for (int i = 0; i < tintRenderers.Length; i++)
            {
                if (tintRenderers[i] == null) continue;
                var mpb = new MaterialPropertyBlock();
                tintRenderers[i].GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", color);
                tintRenderers[i].SetPropertyBlock(mpb);
            }
        }
    }
}
