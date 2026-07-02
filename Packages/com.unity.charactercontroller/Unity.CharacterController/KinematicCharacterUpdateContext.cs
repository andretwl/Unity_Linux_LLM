using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;
using Unity.Physics;

namespace Unity.CharacterController
{
    /// <summary>
    /// The context struct holding global data that needs to be accessed during the character update.
    /// </summary>
    public struct KinematicCharacterUpdateContext
    {
        /// <summary>
        /// Global time data
        /// </summary>
        public TimeData Time;

        /// <summary>
        /// The physics world this character is part of
        /// </summary>
        [ReadOnly]
        public PhysicsWorld PhysicsWorld;

        /// <summary>
        /// Lookup for the StoredKinematicCharacterData component
        /// </summary>
        [ReadOnly]
        public ComponentLookup<StoredKinematicCharacterData> StoredCharacterBodyPropertiesLookup;
        /// <summary>
        /// Lookup for the TrackedTransform component
        /// </summary>
        [ReadOnly]
        public ComponentLookup<TrackedTransform> TrackedTransformLookup;

        /// <summary>
        /// Temporary raycast hits list
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeList<RaycastHit> TmpRaycastHits;
        /// <summary>
        /// Temporary collider cast hits list
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeList<ColliderCastHit> TmpColliderCastHits;
        /// <summary>
        /// Temporary distance hits list
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeList<DistanceHit> TmpDistanceHits;
        /// <summary>
        /// Temporary rigidbody indexes list used for keeping track of unique rigidbodies collided with
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeList<int> TmpRigidbodyIndexesProcessed;

        /// <summary>
        /// Provides an opportunity to get and store global data at the moment of a system's creation
        /// </summary>
        /// <param name="state"> The state of the system calling this method </param>
        public void OnSystemCreate(ref SystemState state)
        {
            StoredCharacterBodyPropertiesLookup = state.GetComponentLookup<StoredKinematicCharacterData>(true);
            TrackedTransformLookup = state.GetComponentLookup<TrackedTransform>(true);
        }

        /// <summary>
        /// Provides an opportunity to update stored data during a system's update
        /// </summary>
        /// <param name="state"> The state of the system calling this method </param>
        /// <param name="time"> The time data passed on by the system calling this method </param>
        /// <param name="physicsWorldSingleton"> The physics world singleton passed on by the system calling this method </param>
        public void OnSystemUpdate(ref SystemState state, TimeData time, PhysicsWorldSingleton physicsWorldSingleton)
        {
            Time = time;
            PhysicsWorld = physicsWorldSingleton.PhysicsWorld;

            StoredCharacterBodyPropertiesLookup.Update(ref state);
            TrackedTransformLookup.Update(ref state);

            TmpRaycastHits = default;
            TmpColliderCastHits = default;
            TmpDistanceHits = default;
            TmpRigidbodyIndexesProcessed = default;
        }

        /// <summary>
        /// Ensures that the temporary collections held in this struct are created. This should normally be called within a job, before the character update
        /// </summary>
        public void EnsureCreationOfTmpCollections()
        {
            if (!TmpRaycastHits.IsCreated)
            {
                TmpRaycastHits = new NativeList<RaycastHit>(24, Allocator.Temp);
            }

            if (!TmpColliderCastHits.IsCreated)
            {
                TmpColliderCastHits = new NativeList<ColliderCastHit>(24, Allocator.Temp);
            }

            if (!TmpDistanceHits.IsCreated)
            {
                TmpDistanceHits = new NativeList<DistanceHit>(24, Allocator.Temp);
            }

            if (!TmpRigidbodyIndexesProcessed.IsCreated)
            {
                TmpRigidbodyIndexesProcessed = new NativeList<int>(24, Allocator.Temp);
            }
        }
    }
}
