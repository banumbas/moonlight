using Content.Shared._Moonlight.Vehicles;
using Robust.Shared.GameStates;

namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent, NetworkedComponent]
[Access(typeof(MoonlightVehicleSystem))]
public sealed partial class GridVehicleOperatorComponent : Component
{
}
