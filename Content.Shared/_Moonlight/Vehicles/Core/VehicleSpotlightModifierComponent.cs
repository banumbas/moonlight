using Content.Shared._Moonlight.Vehicles;
namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent]
public sealed partial class VehicleSpotlightModifierComponent : Component
{
    [DataField]
    public float RadiusMultiplier = 1f;

    [DataField]
    public float RadiusAdd = 0f;

    [DataField]
    public float EnergyMultiplier = 1f;

    [DataField]
    public float EnergyAdd = 0f;

    [DataField]
    public float SoftnessMultiplier = 1f;

    [DataField]
    public float SoftnessAdd = 0f;
}
