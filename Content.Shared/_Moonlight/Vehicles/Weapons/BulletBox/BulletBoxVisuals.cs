using Content.Shared._Moonlight.Vehicles;
using Robust.Shared.Serialization;

namespace Content.Shared._Moonlight.Vehicles;

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

