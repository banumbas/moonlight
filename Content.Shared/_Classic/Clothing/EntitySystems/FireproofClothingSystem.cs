using Content.Shared._Classic.Clothing.Components;
using Content.Shared.Armor;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Shared._Classic.Clothing.EntitySystems;

/// <summary>
/// Handles fireproof clothing examine message and extinguishing the wearer on equip.
/// </summary>
public sealed class FireproofClothingSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FireproofClothingComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<FireproofClothingComponent, ArmorExamineEvent>(OnArmorExamine);
    }

    private void OnEquipped(Entity<FireproofClothingComponent> ent, ref GotEquippedEvent args)
    {
        if ((ent.Comp.Slots & args.SlotFlags) == 0)
            return;

        var ev = new ExtinguishEvent { FireStacksAdjustment = -100f };
        RaiseLocalEvent(args.EquipTarget, ref ev);
    }

    private void OnArmorExamine(Entity<FireproofClothingComponent> ent, ref ArmorExamineEvent args)
    {
        args.Msg.PushNewline();
        args.Msg.AddMarkupOrThrow(Loc.GetString(ent.Comp.ExamineMessage));
    }

    /// <summary>
    /// Checks if the entity is fireproof, either directly or via equipped fireproof clothing.
    /// </summary>
    public bool IsFireproof(EntityUid uid)
    {
        if (HasComp<FireproofClothingComponent>(uid))
            return true;

        if (_inventory.TryGetInventoryEntity<FireproofClothingComponent>(uid, out _))
            return true;

        return false;
    }
}
