using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameObjects;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent]
public sealed partial class VehicleDamageMultiplierComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}
