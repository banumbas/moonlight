using Content.Shared._Moonlight.Vehicles;
namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent]
public sealed partial class HardpointVisualComponent : Component
{
    [DataField(required: true)]
    public string VehicleState = string.Empty;

    [DataField]
    public string DamagedVehicleState = string.Empty;
}
