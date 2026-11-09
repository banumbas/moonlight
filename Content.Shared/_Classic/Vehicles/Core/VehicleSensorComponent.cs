using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleSensorComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 40f;

    [DataField, AutoNetworkedField]
    public bool RequiresDeployed;
}
