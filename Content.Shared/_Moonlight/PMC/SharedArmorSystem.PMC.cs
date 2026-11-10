using Content.Shared.Damage;
using Robust.Shared.GameObjects;

namespace Content.Shared.Armor;

public abstract partial class SharedArmorSystem
{
    public void SetModifiers(EntityUid uid, DamageModifierSet modifiers, ArmorComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.Modifiers = modifiers;
        Dirty(uid, component);
    }
}
