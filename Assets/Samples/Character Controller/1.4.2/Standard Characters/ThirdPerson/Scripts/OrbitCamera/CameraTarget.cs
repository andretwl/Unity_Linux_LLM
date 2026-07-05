#pragma warning disable UAC1001
using System;
using Unity.Entities;

[Serializable]
public struct CameraTarget : IComponentData
{
    public Entity TargetEntity;
}
