using Content.Shared._Classic.Vehicles;
using System;
using Robust.Shared.Serialization;

namespace Content.Shared._Classic.Vehicles;

[Serializable, NetSerializable]
public enum VehicleFrameDamageVisuals : byte
{
    IntegrityFraction,
}

public static class VehicleFrameDamageLayers
{
    public const string DamagedFrame = "damaged_frame";
}
