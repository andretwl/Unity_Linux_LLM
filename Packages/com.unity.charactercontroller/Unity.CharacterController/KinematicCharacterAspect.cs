#if !UNITY_6000_5_OR_NEWER
#pragma warning disable CS0618
using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using RaycastHit = Unity.Physics.RaycastHit;

namespace Unity.CharacterController
{
    /// <summary>
    /// Aspect regrouping the core components and logic of a character controller
    /// </summary>
    public readonly partial struct KinematicCharacterAspect : IAspect
    {
        /// <summary>
        /// The entity of the character
        /// </summary>
        public readonly Entity Entity;
        /// <summary>
        /// The local transform component of the character entity
        /// </summary>
        public readonly RefRW<LocalTransform> LocalTransform;
        /// <summary>
        /// The <see cref="KinematicCharacterProperties"/> component of the character entity
        /// </summary>
        public readonly RefRW<KinematicCharacterProperties> CharacterProperties;
        /// <summary>
        /// The <see cref="KinematicCharacterBody"/> component of the character entity
        /// </summary>
        public readonly RefRW<KinematicCharacterBody> CharacterBody;
        /// <summary>
        /// The <see cref="PhysicsCollider"/> component of the character entity
        /// </summary>
        public readonly RefRW<PhysicsCollider> PhysicsCollider;
        /// <summary>
        /// The <see cref="KinematicCharacterHit"/> dynamic buffer of the character entity
        /// </summary>
        public readonly DynamicBuffer<KinematicCharacterHit> CharacterHitsBuffer;
        /// <summary>
        /// The <see cref="StatefulKinematicCharacterHit"/> dynamic buffer of the character entity
        /// </summary>
        public readonly DynamicBuffer<StatefulKinematicCharacterHit> StatefulHitsBuffer;
        /// <summary>
        /// The <see cref="KinematicCharacterDeferredImpulse"/> dynamic buffer of the character entity
        /// </summary>
        public readonly DynamicBuffer<KinematicCharacterDeferredImpulse> DeferredImpulsesBuffer;
        /// <summary>
        /// The <see cref="KinematicVelocityProjectionHit"/> dynamic buffer of the character entity
        /// </summary>
        public readonly DynamicBuffer<KinematicVelocityProjectionHit> VelocityProjectionHits;

        /// <summary>
        /// Returns the forward direction of the character transform
        /// </summary>
        public float3 Forward => math.mul(LocalTransform.ValueRO.Rotation, math.forward());

        /// <summary>
        /// Returns the back direction of the character transform
        /// </summary>
        public float3 Back => math.mul(LocalTransform.ValueRO.Rotation, -math.forward());

        /// <summary>
        /// Returns the up direction of the character transform
        /// </summary>
        public float3 Up => math.mul(LocalTransform.ValueRO.Rotation, math.up());

        /// <summary>
        /// Returns the down direction of the character transform
        /// </summary>
        public float3 Down => math.mul(LocalTransform.ValueRO.Rotation, -math.up());

        /// <summary>
        /// Returns the right direction of the character transform
        /// </summary>
        public float3 Right => math.mul(LocalTransform.ValueRO.Rotation, math.right());

        /// <summary>
        /// Returns the left direction of the character transform
        /// </summary>
        public float3 Left => math.mul(LocalTransform.ValueRO.Rotation, -math.right());

        /// <summary>
        /// The initialization step of the character update (should be called on every character update). This resets key component values and buffers
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="deltaTime"> The time delta of the character update </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void Update_Initialize<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            float deltaTime) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.Update_Initialize(
                in processor,
                ref context,
                ref baseContext,
                ref characterBody,
                CharacterHitsBuffer,
                DeferredImpulsesBuffer,
                VelocityProjectionHits,
                deltaTime);
        }

        /// <summary>
        /// Handles moving the character based on its currently-assigned ParentEntity, if any.
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="constrainRotationToGroundingUp"> Whether or not to limit rotation around the grounding up direction </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void Update_ParentMovement<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition,
            bool constrainRotationToGroundingUp) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.Update_ParentMovement(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref characterBody,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                in LocalTransform.ValueRO,
                ref characterPosition,
                constrainRotationToGroundingUp);
        }

        /// <summary>
        /// Handles detecting character grounding and storing results in <see cref="KinematicCharacterBody"/>
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void Update_Grounding<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.Update_Grounding(
                in processor,
                ref context,
                ref baseContext,
                ref characterBody,
                Entity,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                in LocalTransform.ValueRO,
                VelocityProjectionHits,
                CharacterHitsBuffer,
                ref characterPosition);
        }

        /// <summary>
        /// Handles moving the character and solving collisions, based on character velocity, rotation, character grounding, and various other properties
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void Update_MovementAndDecollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.Update_MovementAndDecollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref characterBody,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                in LocalTransform.ValueRO,
                VelocityProjectionHits,
                CharacterHitsBuffer,
                DeferredImpulsesBuffer,
                ref characterPosition);
        }

        /// <summary>
        /// Handles predicting future slope changes in order to prevent grounding in certain scenarios
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="stepAndSlopeHandling"> Parameters for step and slope handling </param>
        /// <param name="slopeDetectionVerticalOffset"> The vertical distance from ground hit at which slope detection raycasts will start </param>
        /// <param name="slopeDetectionDownDetectionDepth"> The distance of downward slope detection raycasts, added to the initial vertical offset </param>
        /// <param name="slopeDetectionSecondaryNoGroundingCheckDistance"> The forward distance of an extra raycast meant to detect slopes that are slightly further away than where our velocity would bring us over the next update </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void Update_PreventGroundingFromFutureSlopeChange<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            in BasicStepAndSlopeHandlingParameters stepAndSlopeHandling,
            float slopeDetectionVerticalOffset = 0.05f,
            float slopeDetectionDownDetectionDepth = 0.05f,
            float slopeDetectionSecondaryNoGroundingCheckDistance = 0.25f) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.Update_PreventGroundingFromFutureSlopeChange(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref characterBody,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                in stepAndSlopeHandling,
                slopeDetectionVerticalOffset,
                slopeDetectionDownDetectionDepth,
                slopeDetectionSecondaryNoGroundingCheckDistance);
        }

        /// <summary>
        /// Handles applying ground push forces to the currently-detected ground hit, if applicable
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="gravity"> The effective gravity used to create a force to apply to the ground, in combination with the character mass </param>
        /// <param name="forceMultiplier"> An arbitrary multiplier to apply to the calculated force to apply to the ground </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void Update_GroundPushing<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 gravity,
            float forceMultiplier = 1f) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.Update_GroundPushing(
                in processor,
                ref context,
                ref baseContext,
                ref CharacterBody.ValueRW,
                in CharacterProperties.ValueRO,
                in LocalTransform.ValueRO,
                DeferredImpulsesBuffer,
                gravity,
                forceMultiplier);
        }

        /// <summary>
        /// Handles detecting valid moving platforms based on current ground hit, and automatically sets them as the character's parent entity
        /// </summary>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        public void Update_MovingPlatformDetection(ref KinematicCharacterUpdateContext baseContext, ref KinematicCharacterBody characterBody)
        {
            KinematicCharacterUtilities.Update_MovingPlatformDetection(ref baseContext, ref characterBody);
        }

        /// <summary>
        /// Handles preserving velocity momentum when getting unparented from a parent body (such as a moving platform).
        /// </summary>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        public void Update_ParentMomentum(ref KinematicCharacterUpdateContext baseContext, ref KinematicCharacterBody characterBody)
        {
            KinematicCharacterUtilities.Update_ParentMomentum(
                ref baseContext,
                ref characterBody,
                LocalTransform.ValueRO.Position);
        }

        /// <summary>
        /// Handles filling the stateful hits buffer on the character entity, with character hits that have an Enter/Exit/Stay state associated to them
        /// </summary>
        public void Update_ProcessStatefulCharacterHits()
        {
            KinematicCharacterUtilities.Update_ProcessStatefulCharacterHits(CharacterHitsBuffer, StatefulHitsBuffer);
        }

        /// <summary>
        /// Determines if the character movement collision detection would detect non-grounded obstructions with the designated movement vector
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="movement"> The movement vector of the character </param>
        /// <param name="hit"> The hit to decollide from </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not a non-grounded obstruction would be hit with the designated movement </returns>
        public bool MovementWouldHitNonGroundedObstruction<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 movement,
            out ColliderCastHit hit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.MovementWouldHitNonGroundedObstruction(
                in processor,
                ref context,
                ref baseContext,
                in CharacterProperties.ValueRO,
                in LocalTransform.ValueRO,
                Entity,
                in PhysicsCollider.ValueRO,
                movement,
                out hit);
        }

        /// <summary>
        /// Default implementation of the "IsGroundedOnHit" processor callback. Calls default grounding evaluation for a hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hit"> The hit to decollide from </param>
        /// <param name="stepAndSlopeHandling"> Whether or not step-handling is enabled </param>
        /// <param name="groundingEvaluationType"> Identifier for the type of grounding evaluation that's being requested </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not the character is grounded on the hit </returns>
        public bool Default_IsGroundedOnHit<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            in BasicHit hit,
            in BasicStepAndSlopeHandlingParameters stepAndSlopeHandling,
            int groundingEvaluationType) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.Default_IsGroundedOnHit(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                in PhysicsCollider.ValueRO,
                in CharacterBody.ValueRO,
                in CharacterProperties.ValueRO,
                in hit,
                in stepAndSlopeHandling,
                groundingEvaluationType);
        }

        /// <summary>
        /// Default implementation of the "UpdateGroundingUp" processor callback. Sets the character ground up to the character transform's up direction
        /// </summary>
        /// <param name="characterBody"> The character body component </param>
        public void Default_UpdateGroundingUp(ref KinematicCharacterBody characterBody)
        {
            KinematicCharacterUtilities.Default_UpdateGroundingUp(ref characterBody, LocalTransform.ValueRO.Rotation);
        }

        /// <summary>
        /// Default implementation of the "ProjectVelocityOnHits" processor callback. Projects velocity based on grounding considerations
        /// </summary>
        /// <param name="velocity"> Character velocity </param>
        /// <param name="characterIsGrounded"> Whether character is grounded or not </param>
        /// <param name="characterGroundHit"> The ground hit of the character </param>
        /// <param name="velocityProjectionHits"> List of hits that the velocity must be projected on, from oldest to most recent </param>
        /// <param name="originalVelocityDirection"> Original character velocity direction before any projection happened </param>
        /// <param name="constrainToGroundPlane"> Whether or not to constrain </param>
        public void Default_ProjectVelocityOnHits(
            ref float3 velocity,
            ref bool characterIsGrounded,
            ref BasicHit characterGroundHit,
            in DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits,
            float3 originalVelocityDirection,
            bool constrainToGroundPlane)
        {
            KinematicCharacterUtilities.Default_ProjectVelocityOnHits(
                ref velocity,
                ref characterIsGrounded,
                ref characterGroundHit,
                in velocityProjectionHits,
                originalVelocityDirection,
                constrainToGroundPlane,
                in CharacterBody.ValueRO);
        }

        /// <summary>
        /// Default implementation of the "OnMovementHit" processor callback
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="hit"> The hit to decollide from </param>
        /// <param name="remainingMovementDirection"> Direction of the character movement that's left to be processed </param>
        /// <param name="remainingMovementLength"> Magnitude of the character movement that's left to be processed </param>
        /// <param name="originalVelocityDirection"> Original character velocity direction before any projection happened </param>
        /// <param name="movementHitDistance"> Distance of the hit </param>
        /// <param name="stepHandling"> Whether step-handling is enabled or not </param>
        /// <param name="maxStepHeight"> Maximum height of steps that can be stepped on </param>
        /// <param name="characterWidthForStepGroundingCheck"> Character width used to determine grounding for steps. This is for cases where character with a spherical base tries to step onto an angled surface that is near the character's max step height. In thoses cases, the character might be grounded on steps on one frame, but wouldn't be grounded on the next frame as the spherical nature of its shape would push it a bit further up beyond its max step height. </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void Default_OnMovementHit<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition,
            ref KinematicCharacterHit hit,
            ref float3 remainingMovementDirection,
            ref float remainingMovementLength,
            float3 originalVelocityDirection,
            float movementHitDistance,
            bool stepHandling,
            float maxStepHeight,
            float characterWidthForStepGroundingCheck = 0f) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.Default_OnMovementHit(
                in processor,
                ref context,
                ref baseContext,
                ref characterBody,
                Entity,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                in LocalTransform.ValueRO,
                ref characterPosition,
                VelocityProjectionHits,
                ref hit,
                ref remainingMovementDirection,
                ref remainingMovementLength,
                originalVelocityDirection,
                movementHitDistance,
                stepHandling,
                maxStepHeight,
                characterWidthForStepGroundingCheck);
        }

        /// <summary>
        /// Casts the character collider and returns all collideable hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="characterRotation"> The rotation of the character</param>
        /// <param name="characterScale"> The uniform scale of the character</param>
        /// <param name="direction"> The direction of the case </param>
        /// <param name="length"> The length of the cast </param>
        /// <param name="onlyObstructingHits"> Should the cast only detect hits whose normal is opposed to the direction of the cast </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="hits"> All valid detected hits </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit was detected </returns>
        public bool CastColliderAllCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 characterPosition,
            quaternion characterRotation,
            float characterScale,
            float3 direction,
            float length,
            bool onlyObstructingHits,
            bool ignoreDynamicBodies,
            out NativeList<ColliderCastHit> hits) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.CastColliderAllCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                in PhysicsCollider.ValueRO,
                characterPosition,
                characterRotation,
                characterScale,
                direction,
                length,
                onlyObstructingHits,
                ignoreDynamicBodies,
                out hits);
        }

        /// <summary>
        /// Casts a ray and returns all collideable hits
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="startPoint"> The cast start point </param>
        /// <param name="direction"> The direction of the case </param>
        /// <param name="length"> The length of the cast </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="hits"> The detected hits </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit was detected </returns>
        public bool RaycastAllCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 startPoint,
            float3 direction,
            float length,
            bool ignoreDynamicBodies,
            out NativeList<RaycastHit> hits) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.RaycastAllCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                in PhysicsCollider.ValueRO,
                startPoint,
                direction,
                length,
                ignoreDynamicBodies,
                out hits
            );
        }

        /// <summary>
        /// Called on every character physics update in order to set a parent body for the character
        /// </summary>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="parentEntity"> The parent entity of the character </param>
        /// <param name="anchorPointLocalParentSpace"> The contact point between character and parent, in the parent's local space, around which the character will be rotated </param>
        public void SetOrUpdateParentBody(
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            Entity parentEntity,
            float3 anchorPointLocalParentSpace)
        {
            KinematicCharacterUtilities.SetOrUpdateParentBody(
                ref baseContext,
                ref characterBody,
                parentEntity,
                anchorPointLocalParentSpace);
        }

        /// <summary>
        /// Casts a ray and only returns the closest collideable hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="startPoint"> The cast start point </param>
        /// <param name="direction"> The direction of the case </param>
        /// <param name="length"> The length of the cast </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="hit"> The detected hit </param>
        /// <param name="hitDistance"> The distance of the detected hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit was detected </returns>
        public bool RaycastClosestCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 startPoint,
            float3 direction,
            float length,
            bool ignoreDynamicBodies,
            out RaycastHit hit,
            out float hitDistance) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.RaycastClosestCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                startPoint,
                direction,
                length,
                ignoreDynamicBodies,
                in PhysicsCollider.ValueRO,
                out hit,
                out hitDistance);
        }

        /// <summary>
        /// Casts the character collider and only returns the closest collideable hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="characterRotation"> The rotation of the character</param>
        /// <param name="characterScale"> The uniform scale of the character</param>
        /// <param name="direction"> The direction of the case </param>
        /// <param name="length"> The length of the cast </param>
        /// <param name="onlyObstructingHits"> Should the cast only detect hits whose normal is opposed to the direction of the cast </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="hit"> The closest detected hit </param>
        /// <param name="hitDistance"> The distance of the closest detected hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit was detected</returns>
        public bool CastColliderClosestCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 characterPosition,
            quaternion characterRotation,
            float characterScale,
            float3 direction,
            float length,
            bool onlyObstructingHits,
            bool ignoreDynamicBodies,
            out ColliderCastHit hit,
            out float hitDistance) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.CastColliderClosestCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                in PhysicsCollider.ValueRO,
                characterPosition,
                characterRotation,
                characterScale,
                direction,
                length,
                onlyObstructingHits,
                ignoreDynamicBodies,
                out hit,
                out hitDistance);
        }

        /// <summary>
        /// Calculates distance from the character collider and only returns all collideable hits
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="characterRotation"> The rotation of the character</param>
        /// <param name="characterScale"> The uniform scale of the character</param>
        /// <param name="maxDistance"> The direction of the case </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="hits"> The detected hits </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit was detected</returns>
        public bool CalculateDistanceAllCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 characterPosition,
            quaternion characterRotation,
            float characterScale,
            float maxDistance,
            bool ignoreDynamicBodies,
            out NativeList<DistanceHit> hits) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.CalculateDistanceAllCollisions(
                in processor,
                ref context,
                ref baseContext,
                in PhysicsCollider.ValueRO,
                Entity,
                characterPosition,
                characterRotation,
                characterScale,
                maxDistance,
                ignoreDynamicBodies,
                out hits);
        }

        /// <summary>
        /// Calculates distance from the character collider and only returns the closest collideable hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="characterRotation"> The rotation of the character</param>
        /// <param name="characterScale"> The uniform scale of the character</param>
        /// <param name="maxDistance"> The direction of the case </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="hit"> The closest detected hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit was detected</returns>
        public bool CalculateDistanceClosestCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float3 characterPosition,
            quaternion characterRotation,
            float characterScale,
            float maxDistance,
            bool ignoreDynamicBodies,
            out DistanceHit hit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.CalculateDistanceClosestCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                in PhysicsCollider.ValueRO,
                characterPosition,
                characterRotation,
                characterScale,
                maxDistance,
                ignoreDynamicBodies,
                out hit);
        }

        /// <summary>
        /// Detects grounding at the current character pose
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="groundProbingLength"> Ground probing collider cast distance </param>
        /// <param name="isGrounded"> Outputs whether or not valid ground was detected </param>
        /// <param name="groundHit"> Outputs the detected ground hit </param>
        /// <param name="distanceToGround"> Outputs the distance of the detected ground hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public unsafe void GroundDetection<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float groundProbingLength,
            out bool isGrounded,
            out BasicHit groundHit,
            out float distanceToGround) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            KinematicCharacterUtilities.GroundDetection(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                in LocalTransform.ValueRO,
                in CharacterBody.ValueRO,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                groundProbingLength,
                out isGrounded,
                out groundHit,
                out distanceToGround);
        }

        /// <summary>
        /// Handles calculating forces resulting from character hits, and these forces may be applied both to the character or to the hit body.
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void ProcessCharacterHitDynamics<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.ProcessCharacterHitDynamics(
                in processor,
                ref context,
                ref baseContext,
                ref characterBody,
                in LocalTransform.ValueRO,
                in CharacterProperties.ValueRO,
                CharacterHitsBuffer,
                DeferredImpulsesBuffer);
        }

        /// <summary>
        /// Handles casting the character shape in the velocity direction/magnitude order to detect hits, projecting the character velocity on those hits, and moving the character.
        /// The process is repeated until no new hits are detected, or until a certain max amount of iterations is reached.
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="originalVelocityDirection"> Direction of the character velocity before any projection of velocity happened on this update </param>
        /// <param name="confirmedNoOverlapsOnLastMoveIteration"> Whether or not we can confirm that the character wasn't overlapping with any colliders after the last movement iteration. This is used for optimisation purposes as it gives us an opportunity to skip certain physics queries later in the character update </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void MoveWithCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition,
            float3 originalVelocityDirection,
            out bool confirmedNoOverlapsOnLastMoveIteration) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            KinematicCharacterUtilities.MoveWithCollisions(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref characterBody,
                in LocalTransform.ValueRO,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                ref characterPosition,
                originalVelocityDirection,
                CharacterHitsBuffer,
                VelocityProjectionHits,
                out confirmedNoOverlapsOnLastMoveIteration);
        }

        /// <summary>
        /// Handles the special case of projecting character velocity on a grounded hit, where the velocity magnitude is multiplied by a factor of 1 when it is parallel to the ground, and a factor of 0 when it is parallel to the character's "grounding up direction".
        /// </summary>
        /// <param name="velocity"> The velocity to project </param>
        /// <param name="groundNormal"> The detected ground normal </param>
        /// <param name="groundingUp"> The grounding up direction of the character </param>
        public void ProjectVelocityOnGrounding(ref float3 velocity, float3 groundNormal, float3 groundingUp)
        {
            KinematicCharacterUtilities.ProjectVelocityOnGrounding(ref velocity, groundNormal, groundingUp);
        }

        /// <summary>
        /// Handles detecting current overlap hits, and decolliding the character from them
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="originalVelocityDirection"> Direction of the character velocity before any projection of velocity happened on this update </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void SolveOverlaps<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition,
            float3 originalVelocityDirection) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            KinematicCharacterUtilities.SolveOverlaps(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref characterBody,
                in LocalTransform.ValueRO,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                ref characterPosition,
                originalVelocityDirection,
                DeferredImpulsesBuffer,
                VelocityProjectionHits,
                CharacterHitsBuffer);
        }

        /// <summary>
        /// Decollides character from a specific hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="hit"> The hit to decollide from </param>
        /// <param name="decollisionDistance"> The distance of the decollision check, from the character collider's surface </param>
        /// <param name="originalVelocityDirection"> Direction of the character velocity before any projection of velocity happened on this update </param>
        /// <param name="characterSimulateDynamic"> If the character is "simulate dynamic"</param>
        /// <param name="isGroundedOnHit"> If the character is grounded on hit </param>
        /// <param name="hitIsDynamic"> If the hit is dynamic </param>
        /// <param name="addToCharacterHits"> If the decollision hit should be added to character hits </param>
        /// <param name="projectVelocityOnHit"> If the character velocity should be projected on the hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void DecollideFromHit<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition,
            in BasicHit hit,
            float decollisionDistance,
            float3 originalVelocityDirection,
            bool characterSimulateDynamic,
            bool isGroundedOnHit,
            bool hitIsDynamic,
            bool addToCharacterHits,
            bool projectVelocityOnHit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            KinematicCharacterUtilities.DecollideFromHit(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref characterBody,
                in PhysicsCollider.ValueRO,
                in LocalTransform.ValueRO,
                ref characterPosition,
                in hit,
                decollisionDistance,
                originalVelocityDirection,
                DeferredImpulsesBuffer,
                VelocityProjectionHits,
                CharacterHitsBuffer,
                characterSimulateDynamic,
                isGroundedOnHit,
                hitIsDynamic,
                addToCharacterHits,
                projectVelocityOnHit);
        }

        /// <summary>
        /// Determines if grounded status should be prevented, based on the velocity of the character as well as the velocity of the hit body, if any.
        /// </summary>
        /// <param name="physicsWorld"> The physics world in which the hit body exists </param>
        /// <param name="hit"> The hit to evaluate </param>
        /// <param name="wasGroundedBeforeCharacterUpdate"> Whether or not the character was grounded at the start of its update, before ground detection </param>
        /// <param name="relativeVelocity"> The relative velocity of the character</param>
        /// <returns> Whether or not grounding should be set to false </returns>
        public bool ShouldPreventGroundingBasedOnVelocity(
            in PhysicsWorld physicsWorld,
            in BasicHit hit,
            bool wasGroundedBeforeCharacterUpdate,
            float3 relativeVelocity)
        {
            return KinematicCharacterUtilities.ShouldPreventGroundingBasedOnVelocity(
                in physicsWorld,
                in hit,
                wasGroundedBeforeCharacterUpdate,
                relativeVelocity);
        }

        /// <summary>
        /// Determines if step-handling considerations would make a character be grounded on a hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hit"> The hit to decollide from </param>
        /// <param name="maxStepHeight"> The maximum height that the character can step over </param>
        /// <param name="extraStepChecksDistance"> The horizontal distance at which extra downward step-detection raycasts will be made, in order to allow stepping over steps that are slightly angled </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not step-handling would make the character grounded on this hit </returns>
        public bool IsGroundedOnSteps<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            in BasicHit hit,
            float maxStepHeight,
            float extraStepChecksDistance) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            return KinematicCharacterUtilities.IsGroundedOnSteps(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                in PhysicsCollider.ValueRO,
                in CharacterBody.ValueRO,
                in CharacterProperties.ValueRO,
                in hit,
                maxStepHeight,
                extraStepChecksDistance);
        }

        /// <summary>
        /// Handles the stepping-up-a-step logic during character movement iterations
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="characterBody"> The character body component </param>
        /// <param name="characterPosition"> The position of the character </param>
        /// <param name="hit"> The hit to decollide from </param>
        /// <param name="remainingMovementDirection"> The remaining movement direction </param>
        /// <param name="remainingMovementLength"> The remaining movement length </param>
        /// <param name="hitDistance"> The hit distance </param>
        /// <param name="stepHandling"> If step handling is enabled </param>
        /// <param name="maxStepHeight"> The character's max step height </param>
        /// <param name="characterWidthForStepGroundingCheck"> Character width used to determine grounding for steps. This is for cases where character with a spherical base tries to step onto an angled surface that is near the character's max step height. In thoses cases, the character might be grounded on steps on one frame, but wouldn't be grounded on the next frame as the spherical nature of its shape would push it a bit further up beyond its max step height. </param>
        /// <param name="hasSteppedUp"> If the character has stepped up something </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void CheckForSteppingUpHit<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterBody characterBody,
            ref float3 characterPosition,
            ref KinematicCharacterHit hit,
            ref float3 remainingMovementDirection,
            ref float remainingMovementLength,
            float hitDistance,
            bool stepHandling,
            float maxStepHeight,
            float characterWidthForStepGroundingCheck,
            out bool hasSteppedUp) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            KinematicCharacterUtilities.CheckForSteppingUpHit(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                in PhysicsCollider.ValueRO,
                ref characterBody,
                in CharacterProperties.ValueRO,
                in LocalTransform.ValueRO,
                ref characterPosition,
                ref hit,
                ref remainingMovementDirection,
                ref remainingMovementLength,
                hitDistance,
                stepHandling,
                maxStepHeight,
                characterWidthForStepGroundingCheck,
                out hasSteppedUp);
        }

        /// <summary>
        /// Detects how the ground slope will change over the next character update, based on current character velocity
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="verticalOffset"> Vertical upwards distance where detection raycasts will begin </param>
        /// <param name="downDetectionDepth"> Distance of downwards slope detection raycasts </param>
        /// <param name="deltaTimeIntoFuture"> Time delta into future to detect slopes at with the current character velocity </param>
        /// <param name="secondaryNoGroundingCheckDistance"> Extra horizontal raycast distance for a secondary slope detection raycast </param>
        /// <param name="stepHandling"> Whether step-handling is enabled or not </param>
        /// <param name="maxStepHeight"> Maximum height of steps that can be stepped on </param>
        /// <param name="isMovingTowardsNoGrounding"> Whether or not the character is moving towards a place where it wouldn't be grounded </param>
        /// <param name="foundSlopeHit"> Whether or not we found a slope hit in the future </param>
        /// <param name="futureSlopeChangeAnglesRadians"> The detected slope angle change (in radians) in the future </param>
        /// <param name="futureSlopeHit"> The detected slope hit in the future </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void DetectFutureSlopeChange<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            float verticalOffset,
            float downDetectionDepth,
            float deltaTimeIntoFuture,
            float secondaryNoGroundingCheckDistance,
            bool stepHandling,
            float maxStepHeight,
            out bool isMovingTowardsNoGrounding,
            out bool foundSlopeHit,
            out float futureSlopeChangeAnglesRadians,
            out RaycastHit futureSlopeHit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            KinematicCharacterUtilities.DetectFutureSlopeChange(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref CharacterBody.ValueRW,
                in CharacterProperties.ValueRO,
                in PhysicsCollider.ValueRO,
                verticalOffset,
                downDetectionDepth,
                deltaTimeIntoFuture,
                secondaryNoGroundingCheckDistance,
                stepHandling,
                maxStepHeight,
                out isMovingTowardsNoGrounding,
                out foundSlopeHit,
                out futureSlopeChangeAnglesRadians,
                out futureSlopeHit);
        }

        /// <summary>
        /// Determines if the slope angle is within grounded tolerance
        /// </summary>
        /// <param name="maxGroundedSlopeDotProduct"> Dot product between grounding up and maximum slope normal direction </param>
        /// <param name="slopeSurfaceNormal"> Evaluated slope normal </param>
        /// <param name="groundingUp"> Character's grounding up </param>
        /// <returns> Whether or not the character can be grounded on this slope </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsGroundedOnSlopeNormal(
            float maxGroundedSlopeDotProduct,
            float3 slopeSurfaceNormal,
            float3 groundingUp)
        {
            return KinematicCharacterUtilities.IsGroundedOnSlopeNormal(
                maxGroundedSlopeDotProduct,
                slopeSurfaceNormal,
                groundingUp);
        }

        /// <summary>
        /// Determines the effective signed slope angle of a hit based on character movement direction (negative sign means downward)
        /// </summary>
        /// <param name="currentGroundUp"> Current ground hit normal </param>
        /// <param name="hitNormal"> Evaluated hit normal </param>
        /// <param name="velocityDirection"> Direction of the character's velocity </param>
        /// <param name="groundingUp"> Grounding up of the character </param>
        /// <returns> The signed slope angle of the hit in the character's movement direction </returns>
        public float CalculateAngleOfHitWithGroundUp(float3 currentGroundUp, float3 hitNormal, float3 velocityDirection, float3 groundingUp)
        {
            return KinematicCharacterUtilities.CalculateAngleOfHitWithGroundUp(currentGroundUp, hitNormal, velocityDirection, groundingUp);
        }

        /// <summary>
        /// Filters a list of hits for ground probing and returns the closest valid hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="castDirection"> The direction of the ground probing cast </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="closestHit"> The closest detected hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterColliderCastHitsForGroundProbing<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<ColliderCastHit> hits,
            float3 castDirection,
            bool ignoreDynamicBodies,
            out ColliderCastHit closestHit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            return KinematicCharacterUtilities.FilterColliderCastHitsForGroundProbing(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref hits,
                castDirection,
                ignoreDynamicBodies,
                out closestHit);
        }

        /// <summary>
        /// Filters a list of hits for character movement and returns the closest valid hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="characterIsKinematic"> Is the character kinematic (as opposed to simulated dynamic) </param>
        /// <param name="castDirection"> The direction of the ground probing cast </param>
        /// <param name="ignoredEntity"> An optional Entity to force ignore </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="closestHit"> The closest detected hit </param>
        /// <param name="foundAnyOverlaps"> Whether any overlaps were found with other colliders </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterColliderCastHitsForMove<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<ColliderCastHit> hits,
            bool characterIsKinematic,
            float3 castDirection,
            Entity ignoredEntity,
            bool ignoreDynamicBodies,
            out ColliderCastHit closestHit,
            out bool foundAnyOverlaps) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            return KinematicCharacterUtilities.FilterColliderCastHitsForMove(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref hits,
                characterIsKinematic,
                castDirection,
                ignoredEntity,
                ignoreDynamicBodies,
                out closestHit,
                out foundAnyOverlaps);
        }

        /// <summary>
        /// Filters a list of hits and returns the closest valid hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="onlyObstructingHits"> Should the cast only detect hits whose normal is opposed to the direction of the cast </param>
        /// <param name="castDirection"> The direction of the ground probing cast </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="closestHit"> The closest detected hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterColliderCastHitsForClosestCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<ColliderCastHit> hits,
            bool onlyObstructingHits,
            float3 castDirection,
            bool ignoreDynamicBodies,
            out ColliderCastHit closestHit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            return KinematicCharacterUtilities.FilterColliderCastHitsForClosestCollisions(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref hits,
                onlyObstructingHits,
                castDirection,
                ignoreDynamicBodies,
                out closestHit);
        }

        /// <summary>
        /// Filters a list of hits and keeps only valid hits
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="onlyObstructingHits"> Should the cast only detect hits whose normal is opposed to the direction of the cast </param>
        /// <param name="castDirection"> The direction of the ground probing cast </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterColliderCastHitsForAllCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<ColliderCastHit> hits,
            bool onlyObstructingHits,
            float3 castDirection,
            bool ignoreDynamicBodies) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.FilterColliderCastHitsForAllCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref hits,
                onlyObstructingHits,
                castDirection,
                ignoreDynamicBodies);
        }

        /// <summary>
        /// Filters a list of hits for overlap resolution, and keeps only valid hits. Also returns a variety of closest hits
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="closestHit"> The closest valid hit </param>
        /// <param name="closestDynamicHit"> The closest valid dynamic hit </param>
        /// <param name="closestNonDynamicHit"> The closest valid non-dynamic hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        public void FilterDistanceHitsForSolveOverlaps<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<DistanceHit> hits,
            out DistanceHit closestHit,
            out DistanceHit closestDynamicHit,
            out DistanceHit closestNonDynamicHit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            KinematicCharacterUtilities.FilterDistanceHitsForSolveOverlaps(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref hits,
                out closestHit,
                out closestDynamicHit,
                out closestNonDynamicHit);
        }

        /// <summary>
        /// Filters a list of hits and returns the closest valid hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="closestHit"> The closest valid hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterDistanceHitsForClosestCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<DistanceHit> hits,
            bool ignoreDynamicBodies,
            out DistanceHit closestHit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.FilterDistanceHitsForClosestCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref hits,
                ignoreDynamicBodies,
                out closestHit);
        }

        /// <summary>
        /// Filters a list of hits and returns all valid hits
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterDistanceHitsForAllCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<DistanceHit> hits,
            bool ignoreDynamicBodies) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.FilterDistanceHitsForAllCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref hits,
                ignoreDynamicBodies);
        }

        /// <summary>
        /// Filters a list of hits and returns the closest valid hit
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <param name="closestHit"> The closest valid hit </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterRaycastHitsForClosestCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<RaycastHit> hits,
            bool ignoreDynamicBodies,
            out RaycastHit closestHit) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            var characterEntity = Entity;
            return KinematicCharacterUtilities.FilterRaycastHitsForClosestCollisions(
                in processor,
                ref context,
                ref baseContext,
                ref characterEntity,
                ref hits,
                ignoreDynamicBodies,
                out closestHit);
        }

        /// <summary>
        /// Filters a list of hits and returns all valid hits
        /// </summary>
        /// <param name="processor"> The struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </param>
        /// <param name="context"> The user context struct holding global data meant to be accessed during the character update </param>
        /// <param name="baseContext"> The built-in context struct holding global data meant to be accessed during the character update </param>
        /// <param name="hits"> The list of hits to filter </param>
        /// <param name="ignoreDynamicBodies"> Should the cast ignore dynamic bodies </param>
        /// <typeparam name="T"> The type of the struct implementing <see cref="IKinematicCharacterProcessor{C}"/> </typeparam>
        /// <typeparam name="C"> The type of the user-created context struct </typeparam>
        /// <returns> Whether or not any valid hit remains </returns>
        public bool FilterRaycastHitsForAllCollisions<T, C>(
            in T processor,
            ref C context,
            ref KinematicCharacterUpdateContext baseContext,
            ref NativeList<RaycastHit> hits,
            bool ignoreDynamicBodies) where T : unmanaged, IKinematicCharacterProcessor<C> where C : unmanaged
        {
            return KinematicCharacterUtilities.FilterRaycastHitsForAllCollisions(
                in processor,
                ref context,
                ref baseContext,
                Entity,
                ref hits,
                ignoreDynamicBodies);
        }
    }
}
#endif
