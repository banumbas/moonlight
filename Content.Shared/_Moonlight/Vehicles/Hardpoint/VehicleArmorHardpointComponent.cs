using Content.Shared._Moonlight.Vehicles;
using System.Collections.Generic;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleArmorHardpointComponent : Component
{
    [DataField]
    public List<ProtoId<DamageModifierSetPrototype>> ModifierSets = new();

    [DataField]
    public float? ExplosionCoefficient;
}
