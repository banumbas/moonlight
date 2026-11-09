using Content.Shared._Classic.Vehicles;
using System.Collections.Generic;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleArmorHardpointComponent : Component
{
    [DataField]
    public List<ProtoId<DamageModifierSetPrototype>> ModifierSets = new();

    [DataField]
    public float? ExplosionCoefficient;
}
