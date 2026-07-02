#pragma warning disable CS0618
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Physics;
using Unity.Transforms;

namespace Unity.CharacterController
{
    /// <summary>
    /// A struct used to store the entire state of the core character components
    /// </summary>
    [Serializable]
    public unsafe struct KinematicCharacterStateSave : IDisposable
    {
        /// <summary>
        /// The local transform of the character
        /// </summary>
        public LocalTransform SavedTransform;
        /// <summary>
        /// The character properties component
        /// </summary>
        public KinematicCharacterProperties SavedCharacterProperties;
        /// <summary>
        /// The character body component
        /// </summary>
        public KinematicCharacterBody SavedCharacterBody;

        /// <summary>
        /// Size of the saved physics collider, in bytes
        /// </summary>
        public int SavedPhysicsColliderMemorySize;
        /// <summary>
        /// Saved physics collider data
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> SavedPhysicsColliderMemory;

        /// <summary>
        /// Count for the saved character hits buffer
        /// </summary>
        public int SavedCharacterHitsBufferCount;
        /// <summary>
        /// The character hits buffer
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<KinematicCharacterHit> SavedCharacterHitsBuffer;
        /// <summary>
        /// Count for the saved stateful character hits buffer
        /// </summary>
        public int SavedStatefulHitsBufferCount;
        /// <summary>
        /// The stateful character hits buffer
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<StatefulKinematicCharacterHit> SavedStatefulHitsBuffer;
        /// <summary>
        /// Count for the saved deferred impulses buffer
        /// </summary>
        public int SavedDeferredImpulsesBufferCount;
        /// <summary>
        /// The deferred impulses buffer
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<KinematicCharacterDeferredImpulse> SavedDeferredImpulsesBuffer;
        /// <summary>
        /// Count for the saved velocity projection hits buffer
        /// </summary>
        public int SavedVelocityProjectionHitsCount;
        /// <summary>
        /// The velocity projection hits buffer
        /// </summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<KinematicVelocityProjectionHit> SavedVelocityProjectionHits;

        #if !UNITY_6000_5_OR_NEWER
        /// <summary>
        /// Saves the character state. Only reallocates data arrays if the current arrays are not allocated or don't have the required capacity
        /// </summary>
        /// <param name="characterAspect"> The character aspect that provides access to the components to save </param>
        /// <param name="allocator"> The type of allocation that will be used to store arrays of data </param>
        public void Save(KinematicCharacterAspect characterAspect, Allocator allocator = Allocator.Temp)
        {
            SavedTransform = characterAspect.LocalTransform.ValueRO;
            SavedCharacterProperties = characterAspect.CharacterProperties.ValueRO;
            SavedCharacterBody = characterAspect.CharacterBody.ValueRO;

            PhysicsCollider characterAspectPhysicsCollider = characterAspect.PhysicsCollider.ValueRO;
            SavedPhysicsColliderMemorySize = characterAspectPhysicsCollider.ColliderPtr->MemorySize;
            CheckReallocateArray(ref SavedPhysicsColliderMemory, SavedPhysicsColliderMemorySize, allocator);
            UnsafeUtility.MemCpy(SavedPhysicsColliderMemory.GetUnsafePtr(), characterAspectPhysicsCollider.ColliderPtr, SavedPhysicsColliderMemorySize);

            SavedCharacterHitsBufferCount = characterAspect.CharacterHitsBuffer.Length;
            CheckReallocateArray(ref SavedCharacterHitsBuffer, SavedCharacterHitsBufferCount, allocator);
            UnsafeUtility.MemCpy(SavedCharacterHitsBuffer.GetUnsafePtr(), characterAspect.CharacterHitsBuffer.GetUnsafePtr(), SavedCharacterHitsBufferCount);

            SavedStatefulHitsBufferCount = characterAspect.StatefulHitsBuffer.Length;
            CheckReallocateArray(ref SavedStatefulHitsBuffer, SavedStatefulHitsBufferCount, allocator);
            UnsafeUtility.MemCpy(SavedStatefulHitsBuffer.GetUnsafePtr(), characterAspect.StatefulHitsBuffer.GetUnsafePtr(), SavedStatefulHitsBufferCount);

            SavedDeferredImpulsesBufferCount = characterAspect.DeferredImpulsesBuffer.Length;
            CheckReallocateArray(ref SavedDeferredImpulsesBuffer, SavedDeferredImpulsesBufferCount, allocator);
            UnsafeUtility.MemCpy(SavedDeferredImpulsesBuffer.GetUnsafePtr(), characterAspect.DeferredImpulsesBuffer.GetUnsafePtr(), SavedDeferredImpulsesBufferCount);

            SavedVelocityProjectionHitsCount = characterAspect.VelocityProjectionHits.Length;
            CheckReallocateArray(ref SavedVelocityProjectionHits, SavedVelocityProjectionHitsCount, allocator);
            UnsafeUtility.MemCpy(SavedVelocityProjectionHits.GetUnsafePtr(), characterAspect.VelocityProjectionHits.GetUnsafePtr(), SavedVelocityProjectionHitsCount);
        }
        #endif

        /// <summary>
        /// Saves the character state. Only reallocates data arrays if the current arrays are not allocated or don't have the required capacity
        /// </summary>
        /// <param name="characterDataAccess"> The character data access </param>
        /// <param name="allocator"> The type of allocation that will be used to store arrays of data </param>
        public void Save(KinematicCharacterDataAccess characterDataAccess, Allocator allocator = Allocator.Temp)
        {
            SavedTransform = characterDataAccess.LocalTransform.ValueRO;
            SavedCharacterProperties = characterDataAccess.CharacterProperties.ValueRO;
            SavedCharacterBody = characterDataAccess.CharacterBody.ValueRO;

            PhysicsCollider characterAspectPhysicsCollider = characterDataAccess.PhysicsCollider.ValueRO;
            SavedPhysicsColliderMemorySize = characterAspectPhysicsCollider.ColliderPtr->MemorySize;
            CheckReallocateArray(ref SavedPhysicsColliderMemory, SavedPhysicsColliderMemorySize, allocator);
            UnsafeUtility.MemCpy(SavedPhysicsColliderMemory.GetUnsafePtr(), characterAspectPhysicsCollider.ColliderPtr, SavedPhysicsColliderMemorySize);

            SavedCharacterHitsBufferCount = characterDataAccess.CharacterHitsBuffer.Length;
            CheckReallocateArray(ref SavedCharacterHitsBuffer, SavedCharacterHitsBufferCount, allocator);
            UnsafeUtility.MemCpy(SavedCharacterHitsBuffer.GetUnsafePtr(), characterDataAccess.CharacterHitsBuffer.GetUnsafePtr(), SavedCharacterHitsBufferCount);

            SavedStatefulHitsBufferCount = characterDataAccess.StatefulHitsBuffer.Length;
            CheckReallocateArray(ref SavedStatefulHitsBuffer, SavedStatefulHitsBufferCount, allocator);
            UnsafeUtility.MemCpy(SavedStatefulHitsBuffer.GetUnsafePtr(), characterDataAccess.StatefulHitsBuffer.GetUnsafePtr(), SavedStatefulHitsBufferCount);

            SavedDeferredImpulsesBufferCount = characterDataAccess.DeferredImpulsesBuffer.Length;
            CheckReallocateArray(ref SavedDeferredImpulsesBuffer, SavedDeferredImpulsesBufferCount, allocator);
            UnsafeUtility.MemCpy(SavedDeferredImpulsesBuffer.GetUnsafePtr(), characterDataAccess.DeferredImpulsesBuffer.GetUnsafePtr(), SavedDeferredImpulsesBufferCount);

            SavedVelocityProjectionHitsCount = characterDataAccess.VelocityProjectionHits.Length;
            CheckReallocateArray(ref SavedVelocityProjectionHits, SavedVelocityProjectionHitsCount, allocator);
            UnsafeUtility.MemCpy(SavedVelocityProjectionHits.GetUnsafePtr(), characterDataAccess.VelocityProjectionHits.GetUnsafePtr(), SavedVelocityProjectionHitsCount);
        }

        #if !UNITY_6000_5_OR_NEWER
        /// <summary>
        /// Restores the character state.
        /// </summary>
        /// <param name="characterAspect"> The character aspect that provides access to the components to restore the state to </param>
        public void Restore(KinematicCharacterAspect characterAspect)
        {
            characterAspect.LocalTransform.ValueRW = SavedTransform;
            characterAspect.CharacterProperties.ValueRW = SavedCharacterProperties;
            characterAspect.CharacterBody.ValueRW = SavedCharacterBody;

            PhysicsCollider characterAspectPhysicsCollider = characterAspect.PhysicsCollider.ValueRW;
            if (characterAspectPhysicsCollider.ColliderPtr->MemorySize == SavedPhysicsColliderMemorySize)
            {
                UnsafeUtility.MemCpy(characterAspectPhysicsCollider.ColliderPtr, SavedPhysicsColliderMemory.GetUnsafePtr(), SavedPhysicsColliderMemorySize);
            }
            else
            {
                UnityEngine.Debug.LogError("Error: trying to restore collider state, but memory size of the PhysicsCollider component data on the character entity is different from the saved state. This may have happened because the collider type has been changed since saving the state. In this case, you have the responsibility of manually restoring the original collider type/MemorySize before you restore state.");
            }

            characterAspect.CharacterHitsBuffer.ResizeUninitialized(SavedCharacterHitsBufferCount);
            UnsafeUtility.MemCpy(characterAspect.CharacterHitsBuffer.GetUnsafePtr(), SavedCharacterHitsBuffer.GetUnsafePtr(), SavedCharacterHitsBufferCount);

            characterAspect.StatefulHitsBuffer.ResizeUninitialized(SavedStatefulHitsBufferCount);
            UnsafeUtility.MemCpy(characterAspect.StatefulHitsBuffer.GetUnsafePtr(), SavedStatefulHitsBuffer.GetUnsafePtr(), SavedStatefulHitsBufferCount);

            characterAspect.DeferredImpulsesBuffer.ResizeUninitialized(SavedDeferredImpulsesBufferCount);
            UnsafeUtility.MemCpy(characterAspect.DeferredImpulsesBuffer.GetUnsafePtr(), SavedDeferredImpulsesBuffer.GetUnsafePtr(), SavedDeferredImpulsesBufferCount);

            characterAspect.VelocityProjectionHits.ResizeUninitialized(SavedVelocityProjectionHitsCount);
            UnsafeUtility.MemCpy(characterAspect.VelocityProjectionHits.GetUnsafePtr(), SavedVelocityProjectionHits.GetUnsafePtr(), SavedVelocityProjectionHitsCount);
        }
        #endif

        /// <summary>
        /// Restores the character state.
        /// </summary>
        /// <param name="characterDataAccess"> The character processor that provides access to the components to restore the state to </param>
        public void Restore(KinematicCharacterDataAccess characterDataAccess)
        {
            characterDataAccess.LocalTransform.ValueRW = SavedTransform;
            characterDataAccess.CharacterProperties.ValueRW = SavedCharacterProperties;
            characterDataAccess.CharacterBody.ValueRW = SavedCharacterBody;

            PhysicsCollider characterAspectPhysicsCollider = characterDataAccess.PhysicsCollider.ValueRW;
            if (characterAspectPhysicsCollider.ColliderPtr->MemorySize == SavedPhysicsColliderMemorySize)
            {
                UnsafeUtility.MemCpy(characterAspectPhysicsCollider.ColliderPtr, SavedPhysicsColliderMemory.GetUnsafePtr(), SavedPhysicsColliderMemorySize);
            }
            else
            {
                UnityEngine.Debug.LogError("Error: trying to restore collider state, but memory size of the PhysicsCollider component data on the character entity is different from the saved state. This may have happened because the collider type has been changed since saving the state. In this case, you have the responsibility of manually restoring the original collider type/MemorySize before you restore state.");
            }

            characterDataAccess.CharacterHitsBuffer.ResizeUninitialized(SavedCharacterHitsBufferCount);
            UnsafeUtility.MemCpy(characterDataAccess.CharacterHitsBuffer.GetUnsafePtr(), SavedCharacterHitsBuffer.GetUnsafePtr(), SavedCharacterHitsBufferCount);

            characterDataAccess.StatefulHitsBuffer.ResizeUninitialized(SavedStatefulHitsBufferCount);
            UnsafeUtility.MemCpy(characterDataAccess.StatefulHitsBuffer.GetUnsafePtr(), SavedStatefulHitsBuffer.GetUnsafePtr(), SavedStatefulHitsBufferCount);

            characterDataAccess.DeferredImpulsesBuffer.ResizeUninitialized(SavedDeferredImpulsesBufferCount);
            UnsafeUtility.MemCpy(characterDataAccess.DeferredImpulsesBuffer.GetUnsafePtr(), SavedDeferredImpulsesBuffer.GetUnsafePtr(), SavedDeferredImpulsesBufferCount);

            characterDataAccess.VelocityProjectionHits.ResizeUninitialized(SavedVelocityProjectionHitsCount);
            UnsafeUtility.MemCpy(characterDataAccess.VelocityProjectionHits.GetUnsafePtr(), SavedVelocityProjectionHits.GetUnsafePtr(), SavedVelocityProjectionHitsCount);
        }

        /// <summary>
        /// Disposes all data arrays stored in the character state save
        /// </summary>
        public void Dispose()
        {
            if (SavedPhysicsColliderMemory.IsCreated)
            {
                SavedPhysicsColliderMemory.Dispose();
            }

            if (SavedCharacterHitsBuffer.IsCreated)
            {
                SavedCharacterHitsBuffer.Dispose();
            }

            if (SavedStatefulHitsBuffer.IsCreated)
            {
                SavedStatefulHitsBuffer.Dispose();
            }

            if (SavedDeferredImpulsesBuffer.IsCreated)
            {
                SavedDeferredImpulsesBuffer.Dispose();
            }

            if (SavedVelocityProjectionHits.IsCreated)
            {
                SavedVelocityProjectionHits.Dispose();
            }
        }

        /// <summary>
        /// Reallocates a native array only if it is not created or if it does not have the required specified capacity
        /// </summary>
        /// <param name="arr"> The array to reallocate </param>
        /// <param name="requiredCapacity"> The minimum required capacity that the array should have </param>
        /// <param name="allocator"> The type of allocator to use </param>
        /// <typeparam name="T"> The type of elements stored in the array </typeparam>
        public static void CheckReallocateArray<T>(ref NativeArray<T> arr, int requiredCapacity, Allocator allocator) where T : unmanaged
        {
            if (!arr.IsCreated || arr.Length < requiredCapacity)
            {
                if (arr.IsCreated)
                {
                    arr.Dispose();
                }

                arr = new NativeArray<T>(requiredCapacity, allocator);
            }
        }
    }
}
