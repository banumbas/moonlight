using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles.Viewport;


[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleViewportComponent : Component
{
    /// <summary>View position corresponding to this window, in exterior vehicle-local metres.</summary>
    [DataField]
    public Vector2 ExteriorOffset;
}


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleViewportUserComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? PreviousTarget;

    [AutoNetworkedField]
    public EntityUid? Source;

    [AutoNetworkedField]
    public EntityUid? PeekTarget;

    /// <summary>Selected window position, kept available while the cabin is outside the client's PVS.</summary>
    [AutoNetworkedField]
    public Vector2 ExteriorOffset;
}
