using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleFlamerTankSlotsComponent : Component
{
    [DataField, AutoNetworkedField]
    public int MaxTanks = 1;

    [DataField]
    public EntProtoId? StartingItem;
}
