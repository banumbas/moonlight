using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared._Classic.PMC;

public sealed partial class ToggleArmorModifierSystem : EntitySystem
{
    [Dependency] private readonly SharedArmorSystem _armor = default!;

    private EntityQuery<ArmorComponent> _armorQuery;

    public override void Initialize()
    {
        base.Initialize();

        _armorQuery = GetEntityQuery<ArmorComponent>();

        SubscribeLocalEvent<ToggleArmorModifierComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ToggleArmorModifierComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnMapInit(Entity<ToggleArmorModifierComponent> ent, ref MapInitEvent args)
    {
        if (_armorQuery.TryComp(ent, out var armor))
        {
            ent.Comp.BaseDamageModifiers = armor.Modifiers;
        }
    }

    private void OnToggled(Entity<ToggleArmorModifierComponent> ent, ref ItemToggledEvent args)
    {
        if (_armorQuery.TryComp(ent, out var armor))
        {
            if (args.Activated)
            {
                if (ent.Comp.BaseDamageModifiers == null)
                    ent.Comp.BaseDamageModifiers = armor.Modifiers;
                _armor.SetModifiers(ent.Owner, ent.Comp.ActiveDamageModifiers, armor);
            }
            else
            {
                if (ent.Comp.BaseDamageModifiers != null)
                {
                    _armor.SetModifiers(ent.Owner, ent.Comp.BaseDamageModifiers, armor);
                }
            }
        }
    }
}
