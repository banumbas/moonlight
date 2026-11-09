using Content.Shared._Classic.Vehicles;
using System;
using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Classic.Vehicles.Supply;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class VehicleSupplyEntry
{
    [DataField]
    public string? Name;

    [DataField(required: true)]
    public EntProtoId Vehicle;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class VehicleSupplyConsoleComponent : Component
{
    [DataField(required: true)]
    public List<VehicleSupplyEntry> Vehicles = new();

    [DataField]
    public float LiftSearchRange = 20f;

    [NonSerialized]
    public EntityUid? Lift;

    [DataField]
    public string SelectedVehicle = string.Empty;

    [DataField]
    public int SelectedVehicleCopyIndex;

    [AutoNetworkedField]
    public VehicleSupplyUiState Ui = new(null, false, null, null, 0, null, new List<VehicleSupplyEntryState>());
}
