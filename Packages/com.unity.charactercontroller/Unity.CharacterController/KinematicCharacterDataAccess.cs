using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;

namespace Unity.CharacterController
{
    /// <summary>
    /// A struct representing component data access for core character controller logic
    /// </summary>
    public struct KinematicCharacterDataAccess
    {
        /// <summary>
        /// The entity of the character
        /// </summary>
        public Entity CharacterEntity;
        /// <summary>
        /// The local transform component of the character entity
        /// </summary>
        public RefRW<LocalTransform> LocalTransform;
        /// <summary>
        /// The <see cref="KinematicCharacterProperties"/> component of the character entity
        /// </summary>
        public RefRW<KinematicCharacterProperties> CharacterProperties;
        /// <summary>
        /// The <see cref="KinematicCharacterBody"/> component of the character entity
        /// </summary>
        public RefRW<KinematicCharacterBody> CharacterBody;
        /// <summary>
        /// The <see cref="PhysicsCollider"/> component of the character entity
        /// </summary>
        public RefRW<PhysicsCollider> PhysicsCollider;
        /// <summary>
        /// The <see cref="KinematicCharacterHit"/> dynamic buffer of the character entity
        /// </summary>
        public DynamicBuffer<KinematicCharacterHit> CharacterHitsBuffer;
        /// <summary>
        /// The <see cref="StatefulKinematicCharacterHit"/> dynamic buffer of the character entity
        /// </summary>
        public DynamicBuffer<StatefulKinematicCharacterHit> StatefulHitsBuffer;
        /// <summary>
        /// The <see cref="KinematicCharacterDeferredImpulse"/> dynamic buffer of the character entity
        /// </summary>
        public DynamicBuffer<KinematicCharacterDeferredImpulse> DeferredImpulsesBuffer;
        /// <summary>
        /// The <see cref="KinematicVelocityProjectionHit"/> dynamic buffer of the character entity
        /// </summary>
        public DynamicBuffer<KinematicVelocityProjectionHit> VelocityProjectionHits;

        /// <summary>
        /// Constructs a new KinematicCharacterDataAccess with all required fields
        /// </summary>
        /// <param name="characterEntity"> The character entity </param>
        /// <param name="localTransform"> The character local transform </param>
        /// <param name="characterProperties"> The character properties </param>
        /// <param name="characterBody"> The character body </param>
        /// <param name="physicsCollider"> The character physics collider </param>
        /// <param name="characterHitsBuffer"> The character hits buffer </param>
        /// <param name="statefulHitsBuffer"> The character stateful hits buffer </param>
        /// <param name="deferredImpulsesBuffer"> The character deferred impulses buffer </param>
        /// <param name="velocityProjectionHitsBuffer"> The velocity projection hits buffer </param>
        public KinematicCharacterDataAccess(
            Entity characterEntity,
            RefRW<LocalTransform> localTransform,
            RefRW<KinematicCharacterProperties> characterProperties,
            RefRW<KinematicCharacterBody> characterBody,
            RefRW<PhysicsCollider> physicsCollider,
            DynamicBuffer<KinematicCharacterHit> characterHitsBuffer,
            DynamicBuffer<StatefulKinematicCharacterHit> statefulHitsBuffer,
            DynamicBuffer<KinematicCharacterDeferredImpulse> deferredImpulsesBuffer,
            DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHitsBuffer)
        {
            CharacterEntity = characterEntity;
            LocalTransform = localTransform;
            CharacterProperties = characterProperties;
            CharacterBody = characterBody;
            PhysicsCollider = physicsCollider;
            CharacterHitsBuffer = characterHitsBuffer;
            StatefulHitsBuffer = statefulHitsBuffer;
            DeferredImpulsesBuffer = deferredImpulsesBuffer;
            VelocityProjectionHits = velocityProjectionHitsBuffer;
        }
    }
}
