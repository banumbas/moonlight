using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Operator;
}

[RegisterComponent]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunControllerComponent : Component
{
    [DataField]
    public string GunSlotId = "port-gun";
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunOperatorComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Gun;

    [DataField, AutoNetworkedField]
    public EntityUid? Vehicle;

    [DataField, AutoNetworkedField]
    public EntityUid? Controller;
}

[RegisterComponent, NetworkedComponent]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunSeatComponent : Component
{
}
