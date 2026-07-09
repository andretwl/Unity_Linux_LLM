using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NPCSystem.Tests
{
    public class NPCNetworkPlayerPrefabTests
    {
        const string PlayerPrefabPath = "Assets/Prefabs/Networking/NPCPlayerAvatar.prefab";
        const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [Test]
        public void PlayerPrefabHasRequiredComponents()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCOwnerNetworkTransform>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCPlayerNetworkAvatar>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCPlayerInventory>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCNetworkItemInteractor>(), Is.Not.Null);
#if !UNITY_SERVER
            Assert.That(prefab.GetComponent<PlayerInput>(), Is.Not.Null);
#endif
        }

        [Test]
        public void PlayerPrefabHasModularControllerComponents()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCMultiplayerInputActions>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCCharacterMotor>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCCharacterAnimatorBridge>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCThirdPersonCameraController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCPlayerCharacterController>(), Is.Not.Null);
        }

        [Test]
        public void PlayerPrefabHasAnimatorWithController()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Animator animator = prefab.GetComponent<Animator>();

            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null,
                "Animator must have a controller assigned to drive NetworkAnimator parameters");
        }

#if !UNITY_SERVER
        [Test]
        public void PlayerPrefabIsOwnerAuthoritativeAndUsesPlayerInputActions()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var controller = prefab.GetComponent<NPCPlayerCharacterController>();
            var playerInput = prefab.GetComponent<PlayerInput>();

            Assert.That(controller, Is.Not.Null);
            Assert.That(playerInput, Is.Not.Null);
            Assert.That(playerInput.actions, Is.SameAs(AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath)));
            Assert.That(playerInput.defaultActionMap, Is.EqualTo("Player"));
            var inputActionsField = typeof(NPCPlayerCharacterController).GetField(
                "_lockCursorForOwner",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            );
            Assert.That(inputActionsField, Is.Not.Null);
            Assert.That((bool)inputActionsField.GetValue(controller), Is.True);
        }

        [Test]
        public void NewInputHandlerReferencesSameInputActions()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var newHandler = prefab.GetComponent<NPCMultiplayerInputActions>();
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            Assert.That(newHandler, Is.Not.Null);
            Assert.That(newHandler.InputActions, Is.SameAs(inputActions));
        }
#endif

        [Test]
        public void PlayerInputActionsExposeMovementJumpLookAndSprint()
        {
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            InputActionMap player = actions.FindActionMap("Player", true);

            Assert.That(player.FindAction("Move", true).expectedControlType, Is.EqualTo("Vector2"));
            Assert.That(player.FindAction("Look", true).expectedControlType, Is.EqualTo("Vector2"));
            Assert.That(player.FindAction("Jump", true).type, Is.EqualTo(InputActionType.Button));
            Assert.That(player.FindAction("Sprint", true).type, Is.EqualTo(InputActionType.Button));
            Assert.That(player.FindAction("Interact", true).type, Is.EqualTo(InputActionType.Button));
            Assert.That(player.FindAction("Previous", true).type, Is.EqualTo(InputActionType.Button));
            Assert.That(player.FindAction("Next", true).type, Is.EqualTo(InputActionType.Button));
        }

        [Test]
        public void PlayerControllerExposesSetUIActiveMethod()
        {
            var method = typeof(NPCPlayerCharacterController).GetMethod(
                "SetUIActive",
                new[] { typeof(bool) }
            );

            Assert.That(method, Is.Not.Null,
                "NPCPlayerCharacterController must expose SetUIActive(bool) for dialogue UI input-mode switching");
        }

        [Test]
        public void PlayerControllerHasLocalInstanceProperty()
        {
            var prop = typeof(NPCPlayerCharacterController).GetProperty(
                "LocalInstance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            );

            Assert.That(prop, Is.Not.Null,
                "NPCPlayerCharacterController must expose static LocalInstance property for dialogue UI lookup");
        }
    }
}
