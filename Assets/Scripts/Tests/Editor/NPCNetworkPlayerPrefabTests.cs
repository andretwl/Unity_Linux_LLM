using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NPCSystem.Tests
{
    public class NPCNetworkPlayerPrefabTests
    {
        const string PlayerPrefabPath = "Assets/Resources/Networking/NPCPlayerAvatar.prefab";
        const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [Test]
        public void PlayerPrefabHasRequiredNetworkMovementComponents()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCOwnerNetworkTransform>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Animator>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkAnimator>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCPlayerNetworkAvatar>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NPCNetworkPlayerController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PlayerInput>(), Is.Not.Null);
        }

        [Test]
        public void PlayerPrefabIsOwnerAuthoritativeAndUsesPlayerInputActions()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var controller = prefab.GetComponent<NPCNetworkPlayerController>();
            var playerInput = prefab.GetComponent<PlayerInput>();
            var networkAnimator = prefab.GetComponent<NetworkAnimator>();

            Assert.That(controller.inputActions, Is.SameAs(AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath)));
            Assert.That(playerInput.actions, Is.SameAs(controller.inputActions));
            Assert.That(playerInput.defaultActionMap, Is.EqualTo("Player"));
            Assert.That(controller.usePlayerInputCopy, Is.True);
            Assert.That(controller.allowKeyboardFallback, Is.True);
            Assert.That(networkAnimator.AuthorityMode, Is.EqualTo(NetworkAnimator.AuthorityModes.Owner));
        }

        [Test]
        public void PlayerInputActionsExposeMovementJumpLookAndSprint()
        {
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            InputActionMap player = actions.FindActionMap("Player", true);

            Assert.That(player.FindAction("Move", true).expectedControlType, Is.EqualTo("Vector2"));
            Assert.That(player.FindAction("Look", true).expectedControlType, Is.EqualTo("Vector2"));
            Assert.That(player.FindAction("Jump", true).type, Is.EqualTo(InputActionType.Button));
            Assert.That(player.FindAction("Sprint", true).type, Is.EqualTo(InputActionType.Button));
        }
    }
}
