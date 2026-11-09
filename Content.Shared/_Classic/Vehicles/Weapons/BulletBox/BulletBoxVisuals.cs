using Content.Shared._Classic.Vehicles;
using Robust.Shared.Serialization;

namespace Content.Shared._Classic.Vehicles;

[Serializable, NetSerializable]
public enum BulletBoxLayers
{
    Fill,
}

[Serializable, NetSerializable]
public enum BulletBoxLayers2
{
    Fill,
}

[Serializable, NetSerializable]
public enum BulletBoxVisuals
{
    Empty = 0,
    Low,
    Medium,
    High,
    Full,
}

