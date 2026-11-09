using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent]
public sealed partial class HardpointSlotTypeComponent : Component
{
    [DataField]
    public float RepairRate = 0.05f;
}
