using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NPCSystem
{
    [DefaultExecutionOrder(-350)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public class NPCNetworkPlayerController : NetworkBehaviour
    {
        static readonly int MoveXHash = Animator.StringToHash("MoveX");
        static readonly int MoveYHash = Animator.StringToHash("MoveY");
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int SprintingHash = Animator.StringToHash("Sprinting");
        static readonly int JumpHash = Animator.StringToHash("Jump");

        [Header("References")]
        public CharacterController characterController;
        public Animator animator;
        public Transform cameraFollowTarget;
        public PlayerInput playerInput;
        public InputActionAsset inputActions;
        public string actionMapName = "Player";
        public string moveActionName = "Move";
        public string lookActionName = "Look";
        public string jumpActionName = "Jump";
        public string sprintActionName = "Sprint";

        [Header("Movement")]
        public float walkSpeed = 3.5f;
        public float sprintSpeed = 6.0f;
        public float rotationSpeed = 720f;
        public float jumpHeight = 1.25f;
        public float gravity = -24f;
        public float groundedStickVelocity = -2f;

        [Header("Owner Camera")]
        public bool driveMainCameraForOwner = true;
        public Vector3 cameraOffset = new Vector3(0f, 4.5f, -6f);
        public float cameraFollowSharpness = 12f;
        public float lookYawSensitivity = 0.12f;
        public bool lockCursorForOwner = false;

        [Header("Input Robustness")]
        public bool usePlayerInputCopy = true;
        public bool allowKeyboardFallback = true;
        public bool logSpawnDiagnostics = true;

        Vector2 _moveInput;
        Vector2 _lookInput;
        float _verticalVelocity;
        float _yaw;
        InputAction _moveAction;
        InputAction _lookAction;
        InputAction _jumpAction;
        InputAction _sprintAction;
        Camera _mainCamera;
        bool _inputEnabled;

        public bool HasInputAuthority => !Application.isPlaying || IsOwner;

        void Reset()
        {
            ResolveReferences();
        }

        void Awake()
        {
            ResolveReferences();
            _yaw = transform.eulerAngles.y;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResolveReferences();

            if (IsOwner)
            {
                EnableInput();
                if (logSpawnDiagnostics)
                {
                    string resolvedMoveAction = _moveAction != null ? _moveAction.name : "missing";
                    Debug.Log($"NPCNetworkPlayerController owner input enabled. ownerClientId={OwnerClientId}, localClientId={NetworkManager.Singleton?.LocalClientId}, actionMap={actionMapName}, moveAction={resolvedMoveAction}", this);
                }

                if (lockCursorForOwner)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            else
            {
                if (playerInput != null)
                {
                    playerInput.DeactivateInput();
                }

                DisableInput();
            }
        }

        public override void OnNetworkDespawn()
        {
            DisableInput();
            base.OnNetworkDespawn();
        }

        void OnEnable()
        {
            if (!Application.isPlaying || (IsSpawned && IsOwner))
            {
                EnableInput();
            }
        }

        void OnDisable()
        {
            DisableInput();
        }

        void Update()
        {
            if (!HasInputAuthority)
            {
                UpdateAnimator(Vector2.zero, false, IsGrounded());
                return;
            }

            ReadInput();
            MoveCharacter(Time.deltaTime);
            UpdateOwnerCamera(Time.deltaTime);
        }

        public void ResolveReferences()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
            }

            if (cameraFollowTarget == null)
            {
                cameraFollowTarget = transform;
            }
        }

        void EnableInput()
        {
            if (usePlayerInputCopy && playerInput != null)
            {
                if (!string.IsNullOrWhiteSpace(actionMapName))
                {
                    playerInput.defaultActionMap = actionMapName;
                }

                playerInput.ActivateInput();
                if (!string.IsNullOrWhiteSpace(actionMapName))
                {
                    playerInput.SwitchCurrentActionMap(actionMapName);
                }

                if (playerInput.actions != null)
                {
                    inputActions = playerInput.actions;
                }
            }

            ResolveInputActions();
            _moveAction?.Enable();
            _lookAction?.Enable();
            _jumpAction?.Enable();
            _sprintAction?.Enable();
            _inputEnabled = true;
        }

        void DisableInput()
        {
            if (!_inputEnabled) return;
            _moveAction?.Disable();
            _lookAction?.Disable();
            _jumpAction?.Disable();
            _sprintAction?.Disable();
            _inputEnabled = false;
        }

        void ResolveInputActions()
        {
            if (inputActions == null) return;
            InputActionMap map = inputActions.FindActionMap(actionMapName, false);
            _moveAction = map?.FindAction(moveActionName, false);
            _lookAction = map?.FindAction(lookActionName, false);
            _jumpAction = map?.FindAction(jumpActionName, false);
            _sprintAction = map?.FindAction(sprintActionName, false);
        }

        void ReadInput()
        {
            _moveInput = _moveAction != null ? Vector2.ClampMagnitude(_moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;
            _lookInput = _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;

            if (allowKeyboardFallback && _moveInput.sqrMagnitude < 0.0001f)
            {
                Vector2 fallbackMove = Vector2.zero;
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) fallbackMove.y += 1f;
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) fallbackMove.y -= 1f;
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) fallbackMove.x += 1f;
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) fallbackMove.x -= 1f;
                }

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    fallbackMove += gamepad.leftStick.ReadValue();
                }

                _moveInput = Vector2.ClampMagnitude(fallbackMove, 1f);
            }
        }

        void MoveCharacter(float deltaTime)
        {
            if (characterController == null) return;

            bool grounded = IsGrounded();
            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickVelocity;
            }

            if (grounded && _jumpAction != null && _jumpAction.WasPressedThisFrame())
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                if (animator != null)
                {
                    animator.SetTrigger(JumpHash);
                }
            }

            bool sprinting = (_sprintAction != null && _sprintAction.IsPressed()) || (allowKeyboardFallback && Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed));
            float targetSpeed = sprinting ? sprintSpeed : walkSpeed;
            Vector3 planarMove = new Vector3(_moveInput.x, 0f, _moveInput.y);
            planarMove = Vector3.ClampMagnitude(planarMove, 1f);

            if (planarMove.sqrMagnitude > 0.0001f)
            {
                Vector3 cameraForward = CameraPlanarForward();
                Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward).normalized;
                Vector3 worldMove = (cameraForward * planarMove.z + cameraRight * planarMove.x).normalized;
                Quaternion targetRotation = Quaternion.LookRotation(worldMove, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
                planarMove = worldMove;
            }

            _verticalVelocity += gravity * deltaTime;
            Vector3 velocity = planarMove * targetSpeed + Vector3.up * _verticalVelocity;
            characterController.Move(velocity * deltaTime);
            UpdateAnimator(_moveInput, sprinting, characterController.isGrounded);
        }

        bool IsGrounded()
        {
            return characterController != null && characterController.isGrounded;
        }

        Vector3 CameraPlanarForward()
        {
            Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return Vector3.forward;
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        void UpdateOwnerCamera(float deltaTime)
        {
            if (!driveMainCameraForOwner) return;
            _mainCamera ??= Camera.main;
            if (_mainCamera == null || cameraFollowTarget == null) return;

            _yaw += _lookInput.x * lookYawSensitivity;
            Quaternion cameraYaw = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 targetPosition = cameraFollowTarget.position + cameraYaw * cameraOffset;
            float t = 1f - Mathf.Exp(-cameraFollowSharpness * deltaTime);
            _mainCamera.transform.position = Vector3.Lerp(_mainCamera.transform.position, targetPosition, t);
            _mainCamera.transform.LookAt(cameraFollowTarget.position + Vector3.up * 1.25f);
        }

        void UpdateAnimator(Vector2 move, bool sprinting, bool grounded)
        {
            if (animator == null) return;
            float speed01 = Mathf.Clamp01(move.magnitude) * (sprinting ? 1f : 0.6f);
            animator.SetFloat(MoveXHash, move.x);
            animator.SetFloat(MoveYHash, move.y);
            animator.SetFloat(SpeedHash, speed01);
            animator.SetBool(GroundedHash, grounded);
            animator.SetBool(SprintingHash, sprinting);
        }
    }
}
