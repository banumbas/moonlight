using Content.Shared._Moonlight.Vehicles;
using Robust.Shared.GameObjects;

namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent]
public sealed partial class VehicleDamageMultiplierComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}
