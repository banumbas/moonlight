using Content.Shared._Classic.Vehicles;
using System;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleRunoverComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Vehicle;

    public TimeSpan Duration;
    public TimeSpan ExpiresAt;
}
