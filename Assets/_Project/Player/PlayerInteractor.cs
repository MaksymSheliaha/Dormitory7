using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dormitory7.Player
{
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private FirstPersonController controller;
        [SerializeField] private QuickInventory inventory;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private DayTimeController dayTimeController;
        [SerializeField] private LayerMask interactMask = ~0;

        private InputAction _interactAction;
        private IInteractable _currentTarget;
        private IHoldInteractable _holdingTarget;
        private float _holdTimer;

        public IInteractable CurrentTarget => _currentTarget;
        public string CurrentPrompt { get; private set; } = "";
        public bool CanInteractCurrent { get; private set; }
        public float HoldProgress { get; private set; }
        public bool IsHolding => _holdingTarget != null;

        public void SetReferences(
            BalanceConfig config,
            Transform camTransform,
            FirstPersonController fpc,
            QuickInventory quickInv,
            DormStatsManager stats,
            DayTimeController dayTime)
        {
            balanceConfig = config;
            cameraTransform = camTransform;
            controller = fpc;
            inventory = quickInv;
            statsManager = stats;
            dayTimeController = dayTime;
        }

        private void Awake()
        {
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
            if (controller == null)
                controller = GetComponent<FirstPersonController>();
            if (inventory == null)
                inventory = GetComponent<QuickInventory>();
            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();
            if (dayTimeController == null)
                dayTimeController = FindAnyObjectByType<DayTimeController>();

            _interactAction = new InputAction("Interact", InputActionType.Button);
            _interactAction.AddBinding("<Keyboard>/e");
            _interactAction.AddBinding("<Gamepad>/buttonWest");
        }

        private void OnEnable() => _interactAction?.Enable();
        private void OnDisable() => _interactAction?.Disable();
        private void OnDestroy() => _interactAction?.Dispose();

        private void Update()
        {
            if (GameStateManager.Instance != null &&
                (GameStateManager.Instance.CurrentState != GameState.Playing || GameStateManager.Instance.IsAnyQteActive))
            {
                CancelActiveHold(BuildContext());
                _currentTarget = null;
                CurrentPrompt = "";
                CanInteractCurrent = false;
                return;
            }

            PlayerContext ctx = BuildContext();

            // If hidden inside a hiding spot, [E] exits hiding directly
            if (controller != null && controller.IsHidden && controller.ActiveHidingSpot != null)
            {
                _currentTarget = controller.ActiveHidingSpot;
                CurrentPrompt = _currentTarget.GetPrompt(ctx);
                CanInteractCurrent = true;
                HoldProgress = 0f;

                if (_interactAction.WasPressedThisFrame())
                    controller.ActiveHidingSpot.ExitHiding();
                return;
            }

            ScanForInteractable(ctx);
            ProcessInteractionInput(ctx);
        }

        private void ScanForInteractable(PlayerContext ctx)
        {
            _currentTarget = null;
            CurrentPrompt = "";
            CanInteractCurrent = false;

            if (cameraTransform == null) return;

            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, balanceConfig.interactDistance, interactMask, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null) continue;
                if (col.transform.IsChildOf(transform)) continue; // Ignore player's own colliders

                IInteractable interactable = col.GetComponentInParent<IInteractable>();
                if (interactable != null)
                {
                    _currentTarget = interactable;
                    CurrentPrompt = interactable.GetPrompt(ctx);
                    CanInteractCurrent = interactable.CanInteract(ctx);
                    return;
                }

                // Stop at first solid non-trigger wall/obstacle
                if (!col.isTrigger)
                    return;
            }
        }

        private void ProcessInteractionInput(PlayerContext ctx)
        {
            if (_holdingTarget != null)
            {
                if (_currentTarget != _holdingTarget || !_interactAction.IsPressed() || !_holdingTarget.CanInteract(ctx))
                {
                    CancelActiveHold(ctx);
                    return;
                }

                _holdTimer += Time.deltaTime;
                float duration = Mathf.Max(0.1f, _holdingTarget.HoldDuration);
                HoldProgress = Mathf.Clamp01(_holdTimer / duration);
                _holdingTarget.OnHoldProgress(ctx, HoldProgress);

                if (_holdTimer >= duration)
                {
                    IHoldInteractable completed = _holdingTarget;
                    _holdingTarget = null;
                    _holdTimer = 0f;
                    HoldProgress = 0f;
                    completed.Interact(ctx);
                }
                return;
            }

            HoldProgress = 0f;
            if (_currentTarget == null || !CanInteractCurrent)
                return;

            if (_interactAction.WasPressedThisFrame())
            {
                if (_currentTarget is IHoldInteractable holdTarget && holdTarget.HoldDuration > 0.05f)
                {
                    _holdingTarget = holdTarget;
                    _holdTimer = 0f;
                    _holdingTarget.OnHoldStart(ctx);
                }
                else
                {
                    _currentTarget.Interact(ctx);
                }
            }
        }

        private void CancelActiveHold(PlayerContext ctx)
        {
            if (_holdingTarget == null) return;
            _holdingTarget.OnHoldCancel(ctx);
            _holdingTarget = null;
            _holdTimer = 0f;
            HoldProgress = 0f;
        }

        private PlayerContext BuildContext()
        {
            return new PlayerContext(
                gameObject,
                cameraTransform,
                controller,
                inventory,
                statsManager,
                dayTimeController);
        }
    }
}
