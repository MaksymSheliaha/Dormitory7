using Dormitory7.Core;
using Dormitory7.Data;
using Dormitory7.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dormitory7.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private BalanceConfig balanceConfig;
        [SerializeField] private Transform cameraRoot;
        [SerializeField] private DormStatsManager statsManager;
        [SerializeField] private float headBobFrequency = 8.5f;
        [SerializeField] private float headBobAmplitude = 0.032f;

        private CharacterController _cc;
        private float _pitch;
        private float _verticalVelocity;
        private float _currentHeight;
        private float _currentCameraY;
        private float _bobTimer;
        private bool _movementLocked;

        private InputAction _moveAction;
        private InputAction _lookMouseAction;
        private InputAction _lookGamepadAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;
        private InputAction _jumpAction;

        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsHidden { get; private set; }
        public HidingSpotInteractable ActiveHidingSpot { get; private set; }
        public Transform CameraRoot => cameraRoot;

        public void SetReferences(BalanceConfig config, Transform camRoot, DormStatsManager stats)
        {
            balanceConfig = config;
            cameraRoot = camRoot;
            statsManager = stats;
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (balanceConfig == null)
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            if (statsManager == null)
                statsManager = FindAnyObjectByType<DormStatsManager>();
            if (cameraRoot == null && Camera.main != null)
                cameraRoot = Camera.main.transform;

            _currentHeight = balanceConfig.standingHeight;
            _currentCameraY = balanceConfig.standingCameraY;
            ApplyControllerHeight(_currentHeight);

            SetupInputActions();
        }

        private void SetupInputActions()
        {
            _moveAction = new InputAction("Move", InputActionType.Value);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");

            _lookMouseAction = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            _lookGamepadAction = new InputAction("LookGamepad", InputActionType.Value, "<Gamepad>/rightStick");

            _sprintAction = new InputAction("Sprint", InputActionType.Button);
            _sprintAction.AddBinding("<Keyboard>/leftShift");
            _sprintAction.AddBinding("<Gamepad>/leftStickPress");

            _crouchAction = new InputAction("Crouch", InputActionType.Button);
            _crouchAction.AddBinding("<Keyboard>/leftCtrl");
            _crouchAction.AddBinding("<Keyboard>/c");
            _crouchAction.AddBinding("<Gamepad>/buttonEast");

            _jumpAction = new InputAction("Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _lookMouseAction?.Enable();
            _lookGamepadAction?.Enable();
            _sprintAction?.Enable();
            _crouchAction?.Enable();
            _jumpAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _lookMouseAction?.Disable();
            _lookGamepadAction?.Disable();
            _sprintAction?.Disable();
            _crouchAction?.Disable();
            _jumpAction?.Disable();
        }

        private void OnDestroy()
        {
            _moveAction?.Dispose();
            _lookMouseAction?.Dispose();
            _lookGamepadAction?.Dispose();
            _sprintAction?.Dispose();
            _crouchAction?.Dispose();
            _jumpAction?.Dispose();
        }

        private void Update()
        {
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            HandleLook();

            if (_movementLocked || IsHidden)
            {
                if (statsManager != null)
                    statsManager.SetNoiseLevel(balanceConfig.idleNoise);
                return;
            }

            HandleCrouchAndMovement();
        }

        private void HandleLook()
        {
            Vector2 mouseDelta = _lookMouseAction.ReadValue<Vector2>() * balanceConfig.mouseSensitivity;
            Vector2 padDelta = _lookGamepadAction.ReadValue<Vector2>() * (balanceConfig.gamepadLookSpeed * Time.deltaTime);
            Vector2 totalLook = mouseDelta + padDelta;

            if (totalLook.sqrMagnitude < 0.00001f) return;

            transform.Rotate(Vector3.up, totalLook.x);

            _pitch = Mathf.Clamp(_pitch - totalLook.y, -85f, 85f);
            if (cameraRoot != null)
                cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void HandleCrouchAndMovement()
        {
            bool wantsCrouch = _crouchAction.IsPressed();
            if (!wantsCrouch && IsCrouching && !CanStandUp())
                wantsCrouch = true;

            IsCrouching = wantsCrouch;

            float targetHeight = IsCrouching ? balanceConfig.crouchHeight : balanceConfig.standingHeight;
            float targetCamY = IsCrouching ? balanceConfig.crouchCameraY : balanceConfig.standingCameraY;

            _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, Time.deltaTime * balanceConfig.crouchTransitionSpeed);
            _currentCameraY = Mathf.Lerp(_currentCameraY, targetCamY, Time.deltaTime * balanceConfig.crouchTransitionSpeed);
            ApplyControllerHeight(_currentHeight);

            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            if (moveInput.sqrMagnitude > 1f)
                moveInput.Normalize();

            bool isMoving = moveInput.sqrMagnitude > 0.01f;
            bool hasEnergyForSprint = statsManager == null || statsManager.Energy > 0.5f;
            IsSprinting = isMoving && !IsCrouching && _sprintAction.IsPressed() && hasEnergyForSprint;

            float speed = IsCrouching
                ? balanceConfig.crouchSpeed
                : (IsSprinting ? balanceConfig.sprintSpeed : balanceConfig.walkSpeed);

            if (IsSprinting && statsManager != null)
                statsManager.ConsumeSprintEnergy(Time.deltaTime);

            Vector3 moveDir = (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;

            if (_cc.isGrounded)
            {
                if (_verticalVelocity < 0f)
                    _verticalVelocity = -2f;

                if (_jumpAction.WasPressedThisFrame() && !IsCrouching)
                    _verticalVelocity = Mathf.Sqrt(balanceConfig.jumpHeight * -2f * balanceConfig.gravity);
            }

            _verticalVelocity += balanceConfig.gravity * Time.deltaTime;
            moveDir.y = _verticalVelocity;

            _cc.Move(moveDir * Time.deltaTime);

            // Subtle headbob while moving on ground
            float bobOffset = 0f;
            if (_cc.isGrounded && isMoving)
            {
                float freq = IsSprinting ? headBobFrequency * 1.35f : (IsCrouching ? headBobFrequency * 0.7f : headBobFrequency);
                float amp = IsSprinting ? headBobAmplitude * 1.35f : (IsCrouching ? headBobAmplitude * 0.45f : headBobAmplitude);
                _bobTimer += Time.deltaTime * freq;
                bobOffset = Mathf.Sin(_bobTimer) * amp;
            }
            else
            {
                _bobTimer = 0f;
            }

            if (cameraRoot != null)
            {
                Vector3 lp = cameraRoot.localPosition;
                lp.y = _currentCameraY + bobOffset;
                cameraRoot.localPosition = lp;
            }

            if (statsManager != null)
            {
                float noise = !isMoving
                    ? balanceConfig.idleNoise
                    : (IsCrouching ? balanceConfig.crouchNoise : (IsSprinting ? balanceConfig.sprintNoise : balanceConfig.walkNoise));
                statsManager.SetNoiseLevel(noise);
            }
        }

        private bool CanStandUp()
        {
            float extraHeight = balanceConfig.standingHeight - _currentHeight;
            if (extraHeight <= 0.05f) return true;
            Vector3 origin = transform.position + Vector3.up * (_currentHeight - _cc.radius);
            return !Physics.SphereCast(origin, _cc.radius * 0.9f, Vector3.up, out _, extraHeight + 0.05f, ~0, QueryTriggerInteraction.Ignore);
        }

        private void ApplyControllerHeight(float height)
        {
            _cc.height = height;
            _cc.center = new Vector3(0f, height * 0.5f, 0f);
        }

        public void SetMovementLocked(bool locked)
        {
            _movementLocked = locked;
            if (locked)
            {
                IsSprinting = false;
                _verticalVelocity = 0f;
            }
        }

        public void EnterHiding(HidingSpotInteractable spot, Vector3 hidePosition, Quaternion hideRotation)
        {
            IsHidden = true;
            ActiveHidingSpot = spot;
            _verticalVelocity = 0f;
            _cc.enabled = false;
            transform.SetPositionAndRotation(hidePosition, hideRotation);
            if (cameraRoot != null)
            {
                _pitch = 0f;
                cameraRoot.localPosition = new Vector3(0f, balanceConfig.crouchCameraY + 0.25f, 0f);
                cameraRoot.localRotation = Quaternion.identity;
            }
        }

        public void ExitHiding(Vector3 exitPosition)
        {
            IsHidden = false;
            ActiveHidingSpot = null;
            transform.position = exitPosition;
            _cc.enabled = true;
        }
    }
}
