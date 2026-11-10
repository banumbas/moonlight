using Content.Shared._Moonlight.Vehicles;
using Robust.Shared.GameStates;

namespace Content.Shared._Moonlight.Vehicles;

/// <summary>
/// Tracking component for handling the operator of a given <see cref="MoonlightVehicleComponent"/>
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(MoonlightVehicleBaseSystem))]
public sealed partial class MoonlightVehicleOperatorComponent : Component
{
    /// <summary>
    /// The vehicle we are currently operating.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Vehicle;
}
