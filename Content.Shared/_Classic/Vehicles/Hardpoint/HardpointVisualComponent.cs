using Content.Shared._Classic.Vehicles;
namespace Content.Shared._Classic.Vehicles;

[RegisterComponent]
public sealed partial class HardpointVisualComponent : Component
{
    [DataField(required: true)]
    public string VehicleState = string.Empty;

    [DataField]
    public string DamagedVehicleState = string.Empty;
}
