using Unity.Entities;
using UnityEngine;

namespace Unity.CharacterController.RuntimeTests
{
    [DisallowMultipleComponent]
    public class TestCharacterAuthoring : MonoBehaviour
    {
        public AuthoringKinematicCharacterProperties CharacterProperties = AuthoringKinematicCharacterProperties.GetDefault();
        public TestCharacterComponent Character = TestCharacterComponent.GetDefault();

        public class Baker : Baker<TestCharacterAuthoring>
        {
            public override void Bake(TestCharacterAuthoring authoring)
            {
                KinematicCharacterUtilities.BakeCharacter(this, authoring, authoring.CharacterProperties);

                Entity selfEntity = GetEntity(TransformUsageFlags.None);

                AddComponent(selfEntity, authoring.Character);
                AddComponent(selfEntity, new TestCharacterControl());
            }
        }
    }
}
