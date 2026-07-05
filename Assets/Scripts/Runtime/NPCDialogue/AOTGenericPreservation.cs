using Unity.Entities;
using UnityEngine.Scripting;

namespace NPCSystem
{
    [Preserve]
    [DisableAutoCreation]
    public partial class AOTGenericPreservation : SystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            // This method is never called at runtime, but its presence forces the IL2CPP AOT compiler
            // to generate the specialized, non-shared C++ code paths for these generic types,
            // bypassing the buggy generic-sharing (Il2CppFullySharedGenericStruct) memory violations under WebGL.
            // By placing calls to SystemAPI directly inside OnUpdate() of a system class, we satisfy the
            // custom Entities compiler analyzer (EA0004), which restricts SystemAPI calls to system lifecycle methods.

            // 1. BeginInitializationEntityCommandBufferSystem.Singleton
            if (SystemAPI.HasSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>())
            {
                var rw = SystemAPI.GetSingletonRW<BeginInitializationEntityCommandBufferSystem.Singleton>();
                var ro = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            }

            // 2. EndInitializationEntityCommandBufferSystem.Singleton
            if (SystemAPI.HasSingleton<EndInitializationEntityCommandBufferSystem.Singleton>())
            {
                var rw = SystemAPI.GetSingletonRW<EndInitializationEntityCommandBufferSystem.Singleton>();
                var ro = SystemAPI.GetSingleton<EndInitializationEntityCommandBufferSystem.Singleton>();
            }

            // 3. BeginSimulationEntityCommandBufferSystem.Singleton
            if (SystemAPI.HasSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>())
            {
                var rw = SystemAPI.GetSingletonRW<BeginSimulationEntityCommandBufferSystem.Singleton>();
                var ro = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            }

            // 4. EndSimulationEntityCommandBufferSystem.Singleton
            if (SystemAPI.HasSingleton<EndSimulationEntityCommandBufferSystem.Singleton>())
            {
                var rw = SystemAPI.GetSingletonRW<EndSimulationEntityCommandBufferSystem.Singleton>();
                var ro = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            }

            // 5. BeginPresentationEntityCommandBufferSystem.Singleton
            if (SystemAPI.HasSingleton<BeginPresentationEntityCommandBufferSystem.Singleton>())
            {
                var rw = SystemAPI.GetSingletonRW<BeginPresentationEntityCommandBufferSystem.Singleton>();
                var ro = SystemAPI.GetSingleton<BeginPresentationEntityCommandBufferSystem.Singleton>();
            }

            // 6. BeginVariableRateSimulationEntityCommandBufferSystem.Singleton
            if (SystemAPI.HasSingleton<BeginVariableRateSimulationEntityCommandBufferSystem.Singleton>())
            {
                var rw = SystemAPI.GetSingletonRW<BeginVariableRateSimulationEntityCommandBufferSystem.Singleton>();
                var ro = SystemAPI.GetSingleton<BeginVariableRateSimulationEntityCommandBufferSystem.Singleton>();
            }
        }

        [Preserve]
        public static void Reference()
        {
            // Dummy method to establish an unbroken static reference path from active runtime code (like NPCDialogueBootstrapper)
            // to this system, preventing IL2CPP from optimizing away or stripping the system and its virtual OnUpdate method.
        }
    }
}
