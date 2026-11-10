using Content.Shared._Moonlight.Vehicles;
using System;
using Robust.Shared.Serialization;

namespace Content.Shared._Moonlight.Vehicles;

[Serializable, NetSerializable]
public enum VehicleFrameDamageVisuals : byte
{
    IntegrityFraction,
}

public static class VehicleFrameDamageLayers
{
    public const string DamagedFrame = "damaged_frame";
}
