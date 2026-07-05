using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class MainCameraSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (MainGameObjectCamera.Instance != null)
        {
            foreach (var targetLocalToWorld in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<MainEntityCamera>())
            {
                MainGameObjectCamera.Instance.transform.SetPositionAndRotation(targetLocalToWorld.ValueRO.Position, targetLocalToWorld.ValueRO.Rotation);
            }
        }
    }
}
