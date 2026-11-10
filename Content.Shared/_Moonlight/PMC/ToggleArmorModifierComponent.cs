using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;

namespace Content.Shared._Moonlight.PMC;

[RegisterComponent, NetworkedComponent]
public sealed partial class ToggleArmorModifierComponent : Component
{
    [DataField(required: true)]
    public DamageModifierSet ActiveDamageModifiers = new();

    [DataField]
    public DamageModifierSet? BaseDamageModifiers = null;
}
