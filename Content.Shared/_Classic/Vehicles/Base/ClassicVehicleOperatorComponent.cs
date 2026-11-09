using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles;

/// <summary>
/// Tracking component for handling the operator of a given <see cref="ClassicVehicleComponent"/>
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ClassicVehicleBaseSystem))]
public sealed partial class ClassicVehicleOperatorComponent : Component
{
    /// <summary>
    /// The vehicle we are currently operating.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Vehicle;
}
