using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using Unity.Burst.Intrinsics;

namespace Unity.CharacterController.RuntimeTests
{
    [UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
    [BurstCompile]
    public partial struct TestCharacterPhysicsUpdateSystem : ISystem
    {
        EntityQuery m_CharacterQuery;
        TestCharacterUpdateContext m_Context;
        KinematicCharacterUpdateContext m_BaseContext;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_CharacterQuery = KinematicCharacterUtilities.GetBaseCharacterQueryBuilder()
                .WithAll<
                    TestCharacterComponent,
                    TestCharacterControl>()
                .Build(ref state);

            m_Context = new TestCharacterUpdateContext();
            m_Context.OnSystemCreate(ref state);
            m_BaseContext = new KinematicCharacterUpdateContext();
            m_BaseContext.OnSystemCreate(ref state);

            state.RequireForUpdate(m_CharacterQuery);
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_Context.OnSystemUpdate(ref state);
            m_BaseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());

            TestCharacterPhysicsUpdateJob job = new TestCharacterPhysicsUpdateJob
            {
                Context = m_Context,
                BaseContext = m_BaseContext,
            };
            job.ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(Simulate))]
        public partial struct TestCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            public TestCharacterUpdateContext Context;
            public KinematicCharacterUpdateContext BaseContext;

            public void Execute(
                Entity entity,
                RefRW<LocalTransform> localTransform,
                RefRW<KinematicCharacterProperties> characterProperties,
                RefRW<KinematicCharacterBody> characterBody,
                RefRW<PhysicsCollider> physicsCollider,
                RefRW<TestCharacterComponent> characterComponent,
                RefRW<TestCharacterControl> characterControl,
                DynamicBuffer<KinematicCharacterHit> characterHitsBuffer,
                DynamicBuffer<StatefulKinematicCharacterHit> statefulHitsBuffer,
                DynamicBuffer<KinematicCharacterDeferredImpulse> deferredImpulsesBuffer,
                DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits)
            {
                var characterProcessor = new TestCharacterProcessor
                {
                    CharacterDataAccess = new KinematicCharacterDataAccess(

                        entity,
                        localTransform,
                        characterProperties,
                        characterBody,
                        physicsCollider,
                        characterHitsBuffer,
                        statefulHitsBuffer,
                        deferredImpulsesBuffer,
                        velocityProjectionHits
                    ),
                    CharacterComponent = characterComponent,
                    CharacterControl = characterControl
                };

                characterProcessor.PhysicsUpdate(ref Context, ref BaseContext);
            }

            public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                BaseContext.EnsureCreationOfTmpCollections();
                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    [BurstCompile]
    public partial struct TestCharacterVariableUpdateSystem : ISystem
    {
        private EntityQuery m_CharacterQuery;
        private TestCharacterUpdateContext m_Context;
        private KinematicCharacterUpdateContext m_BaseContext;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            m_CharacterQuery = KinematicCharacterUtilities.GetBaseCharacterQueryBuilder()
                .WithAll<
                    TestCharacterComponent,
                    TestCharacterControl>()
                .Build(ref state);

            m_Context = new TestCharacterUpdateContext();
            m_Context.OnSystemCreate(ref state);
            m_BaseContext = new KinematicCharacterUpdateContext();
            m_BaseContext.OnSystemCreate(ref state);

            state.RequireForUpdate(m_CharacterQuery);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_Context.OnSystemUpdate(ref state);
            m_BaseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());

            TestCharacterVariableUpdateJob job = new TestCharacterVariableUpdateJob
            {
                Context = m_Context,
                BaseContext = m_BaseContext,
            };
            job.ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(Simulate))]
        public partial struct TestCharacterVariableUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
        {
            public TestCharacterUpdateContext Context;
            public KinematicCharacterUpdateContext BaseContext;

            public void Execute(
                Entity entity,
                RefRW<LocalTransform> localTransform,
                RefRW<KinematicCharacterProperties> characterProperties,
                RefRW<KinematicCharacterBody> characterBody,
                RefRW<PhysicsCollider> physicsCollider,
                RefRW<TestCharacterComponent> characterComponent,
                RefRW<TestCharacterControl> characterControl,
                DynamicBuffer<KinematicCharacterHit> characterHitsBuffer,
                DynamicBuffer<StatefulKinematicCharacterHit> statefulHitsBuffer,
                DynamicBuffer<KinematicCharacterDeferredImpulse> deferredImpulsesBuffer,
                DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits)
            {
                var characterProcessor = new TestCharacterProcessor
                {
                    CharacterDataAccess = new KinematicCharacterDataAccess(

                        entity,
                        localTransform,
                        characterProperties,
                        characterBody,
                        physicsCollider,
                        characterHitsBuffer,
                        statefulHitsBuffer,
                        deferredImpulsesBuffer,
                        velocityProjectionHits
                    ),
                    CharacterComponent = characterComponent,
                    CharacterControl = characterControl
                };

                characterProcessor.VariableUpdate(ref Context, ref BaseContext);
            }

            public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                BaseContext.EnsureCreationOfTmpCollections();
                return true;
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
            }
        }
    }
}
