using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NPCSystem
{
    /// <summary>
    /// Modern multiplayer character controller orchestrator.
    /// Replaces the monolithic NPCNetworkPlayerController with modular subsystems:
    /// - NPCMultiplayerInputActions: clean InputSystem wrapper with cached actions
    /// - NPCCharacterMotor: physics-based movement (FixedUpdate)
    /// - NPCCharacterAnimatorBridge: animator driving
    /// - NPCThirdPersonCameraController: orbit camera (separate component)
    /// - NPCNetworkItemInteractor: interaction routing (existing, preserved)
    ///
    /// Network-aware: only the owning client reads input and drives camera.
    /// Non-owners receive transform replication via NPCOwnerNetworkTransform.
    /// </summary>
    [DefaultExecutionOrder(-350)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NPCOwnerNetworkTransform))]
    [RequireComponent(typeof(NPCMultiplayerInputActions))]
    [RequireComponent(typeof(NPCCharacterMotor))]
    [RequireComponent(typeof(NPCCharacterAnimatorBridge))]
    public sealed class NPCPlayerCharacterController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField]
        NPCMultiplayerInputActions inputHandler;

        [SerializeField]
        NPCCharacterMotor motor;

        [SerializeField]
        NPCCharacterAnimatorBridge animBridge;

        [SerializeField]
        NPCThirdPersonCameraController cameraController;

        [SerializeField]
        NPCNetworkItemInteractor interactor;

        [Header("Input Asset")]
        public InputActionAsset inputActions;
        public string actionMapName = "Player";

        [Header("Owner Settings")]
        public bool lockCursorForOwner = true;
        public bool logSpawnDiagnostics = true;

        // ─── Singleton-style access for the owning client ───
        public static NPCPlayerCharacterController LocalInstance { get; private set; }

        public static bool PendingUIModeRequest { get; private set; }

        [Header("Runtime Status")]
        public bool IsInUIMode { get; private set; }

        public static void RequestUIActive()
        {
            PendingUIModeRequest = true;
            LocalInstance?.SetUIActive(true);
        }

        public static void ClearUIActiveRequest()
        {
            PendingUIModeRequest = false;
        }

        bool _eventsSubscribed;

        // \u2500\u2500\u2500 Lifecycle \u2500\u2500\u2500

        void Reset() => ResolveReferences();

        void Awake()
        {
            ResolveReferences();
            SubscribeInputEvents();
        }

        public override void OnDestroy()
        {
            UnsubscribeInputEvents();
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
            {
                LocalInstance = this;
                if (PendingUIModeRequest)
                {
                    SetUIActive(true);
                }
                else if (IsInUIMode)
                {
                    SetUIActive(true);
                }
                else
                {
                    EnableOwnerInput();
                }

                if (cameraController != null)
                    cameraController.StartFollowing();
            }
            else
            {
                DisableAllInput();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && LocalInstance == this)
                LocalInstance = null;
            DisableAllInput();
            if (IsOwner && cameraController != null)
                cameraController.StopFollowing();
            base.OnNetworkDespawn();
        }

        void OnEnable()
        {
            if (!Application.isPlaying || (IsSpawned && IsOwner))
            {
                if (IsInUIMode)
                {
                    SetUIActive(true);
                }
                else
                {
                    EnableOwnerInput();
                }
            }
        }

        void OnDisable()
        {
            DisableAllInput();
        }

        void Update()
        {
            if (!IsOwner || motor == null)
                return;

            // Tab toggles between UI mode (cursor free, movement locked) and gameplay
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                SetUIActive(!IsInUIMode);
                return;
            }

            motor.MoveInput = inputHandler != null ? inputHandler.MoveInput : Vector2.zero;
            motor.SprintInput = inputHandler != null && inputHandler.SprintHeld;
        }

        // \u2500\u2500\u2500 Input Event Wiring \u2500\u2500\u2500

        void SubscribeInputEvents()
        {
            if (_eventsSubscribed || inputHandler == null)
                return;

            inputHandler.OnJump += HandleJump;
            inputHandler.OnInteract += HandleInteract;
            inputHandler.OnPrevious += HandleGiveToPlayer;
            inputHandler.OnNext += HandleGiveToNpc;
            inputHandler.OnCrouch += HandleCrouch;

            _eventsSubscribed = true;
        }

        void UnsubscribeInputEvents()
        {
            if (!_eventsSubscribed || inputHandler == null)
                return;

            inputHandler.OnJump -= HandleJump;
            inputHandler.OnInteract -= HandleInteract;
            inputHandler.OnPrevious -= HandleGiveToPlayer;
            inputHandler.OnNext -= HandleGiveToNpc;
            inputHandler.OnCrouch -= HandleCrouch;

            _eventsSubscribed = false;
        }

        void HandleJump()
        {
            if (!IsOwner || motor == null)
                return;
            motor.RequestJump();
            animBridge?.TriggerJump();
        }

        void HandleInteract()
        {
            if (!IsOwner || interactor == null)
                return;
            interactor.RequestPickupNearestItemServerRpc();
        }

        void HandleGiveToPlayer()
        {
            if (!IsOwner || interactor == null)
                return;
            interactor.RequestGiveHeldItemToNearestPlayerServerRpc();
        }

        void HandleGiveToNpc()
        {
            if (!IsOwner || interactor == null)
                return;
            interactor.RequestGiveHeldItemToNearestNpcServerRpc();
        }

        void HandleCrouch()
        {
            // Reserved for crouch toggle \u2014 extend when crouch mechanic is added
        }

        // \u2500\u2500\u2500 Input Management \u2500\u2500\u2500

        void EnableOwnerInput()
        {
            if (inputHandler != null)
            {
                if (inputHandler.inputActions == null && inputActions != null)
                    inputHandler.inputActions = inputActions;

                inputHandler.EnableActions();
            }

            if (lockCursorForOwner)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void DisableAllInput()
        {
            if (inputHandler != null)
                inputHandler.DisableAll();

            if (lockCursorForOwner && IsOwner)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void SetUIActive(bool active)
        {
            if (inputHandler == null)
                return;

            if (active)
            {
                inputHandler.DisableActions();
                inputHandler.EnableUIActions();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                ClearUIActiveRequest();
            }
            else
            {
                inputHandler.DisableUIActions();
                inputHandler.EnableActions();
                if (lockCursorForOwner)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            IsInUIMode = active;
        }

        void ResolveReferences()
        {
            if (inputHandler == null)
                inputHandler = GetComponent<NPCMultiplayerInputActions>();
            if (motor == null)
                motor = GetComponent<NPCCharacterMotor>();
            if (animBridge == null)
                animBridge = GetComponent<NPCCharacterAnimatorBridge>();
            if (cameraController == null)
                cameraController = GetComponent<NPCThirdPersonCameraController>();
            if (interactor == null)
                interactor = GetComponent<NPCNetworkItemInteractor>();
        }

        // \u2500\u2500\u2500 Public API \u2500\u2500\u2500

        public NPCCharacterMotor Motor => motor;
        public NPCMultiplayerInputActions InputHandler => inputHandler;
        public NPCThirdPersonCameraController CameraController => cameraController;
    }
}
