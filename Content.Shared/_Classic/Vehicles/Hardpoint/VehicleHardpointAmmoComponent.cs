using Content.Shared._Classic.Vehicles;
using System.Collections.Generic;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleHardpointAmmoComponent : Component
{
    [DataField, AutoNetworkedField]
    public int MagazineSize = 1;

    [DataField, AutoNetworkedField]
    public int MaxStoredMagazines = 0;

    [DataField, AutoNetworkedField]
    public int StoredMagazines = 0;

    [DataField, AutoNetworkedField]
    public int StoredRounds = 0;

    [DataField, AutoNetworkedField]
    public List<int> StoredRoundSlots = new();
}
