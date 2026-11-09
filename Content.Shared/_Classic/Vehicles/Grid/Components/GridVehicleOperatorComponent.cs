using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent]
[Access(typeof(ClassicVehicleSystem))]
public sealed partial class GridVehicleOperatorComponent : Component
{
}
