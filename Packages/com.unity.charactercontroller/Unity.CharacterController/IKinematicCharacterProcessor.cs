using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Unity.CharacterController
{
    /// <summary>
    /// Interface implemented by structs meant to be passed as parameter to various character update steps in order to customize internal character update logic.
    /// </summary>
    /// <typeparam name="C"> The type of the character "context" struct created by the user </typeparam>
    public interface IKinematicCharacterProcessor<C> where C : unmanaged
    {
        /// <summary>
        /// Requests that the grounding up direction should be updated.
        /// </summary>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        void UpdateGroundingUp(
            ref C context,
            ref KinematicCharacterUpdateContext baseContext);

        /// <summary>
        /// Determines if a hit can be collided with or not.
        /// </summary>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hit"> The evaluated hit </param>
        /// <returns> Return true if the hit can be collided with, return false if not. </returns>
        bool CanCollideWithHit(
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            in BasicHit hit);

        /// <summary>
        /// Determines if the character can be grounded the hit or not.
        /// </summary>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hit"> The evaluated hit </param>
        /// <param name="groundingEvaluationType"> An identifier meant to indicate what type of grounding evaluation is being done at the moment of calling this. </param>
        /// <returns> Returns true if the character is grounded on the specified hit </returns>
        bool IsGroundedOnHit(
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            in BasicHit hit,
            int groundingEvaluationType);

        /// <summary>
        /// Determines what happens when the character detects a hit during its movement phase.
        /// </summary>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hit"> The evaluated hit </param>
        /// <param name="remainingMovementDirection"> The direction of the movement vector that remains to be processed </param>
        /// <param name="remainingMovementLength"> The magnitude of the movement vector that remains to be processed </param>
        /// <param name="originalVelocityDirection"> The original direction of the movement vector before any movement projection happened </param>
        /// <param name="hitDistance"> The distance of the detected hit </param>
        void OnMovementHit(
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterHit hit,
            ref float3 remainingMovementDirection,
            ref float remainingMovementLength,
            float3 originalVelocityDirection,
            float hitDistance);

        /// <summary>
        /// Requests that the character velocity be projected on the hits detected so far in the character update.
        /// </summary>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="velocity"> The character velocity that needs to be projected </param>
        /// <param name="characterIsGrounded"> Whether the character is grounded or not </param>
        /// <param name="characterGroundHit"> The current effective ground hit of the character </param>
        /// <param name="velocityProjectionHits"> The hits that have been detected so far during the character update </param>
        /// <param name="originalVelocityDirection"> The original velocity direction of the character at the beginning of the character update, before any projection has happened </param>
        void ProjectVelocityOnHits(
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref float3 velocity,
            ref bool characterIsGrounded,
            ref BasicHit characterGroundHit,
            in DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits,
            float3 originalVelocityDirection);

        /// <summary>
        /// Provides an opportunity to modify the physics masses used to solve impulses between characters and detected hit bodies.
        /// </summary>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterMass"> The mass of the character </param>
        /// <param name="otherMass"> The mass of the other body that we've detected a hit with </param>
        /// <param name="hit"> The evaluated hit with the dynamic body </param>
        void OverrideDynamicHitMasses(
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref PhysicsMass characterMass,
            ref PhysicsMass otherMass,
            BasicHit hit);
    }
}
