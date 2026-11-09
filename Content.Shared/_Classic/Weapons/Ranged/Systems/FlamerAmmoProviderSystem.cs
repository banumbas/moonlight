using System.Diagnostics.CodeAnalysis;
using Content.Shared._Classic.Projectiles;
using Content.Shared._Classic.Weapons.Ranged.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Classic.Weapons.Ranged.Systems;

/// <summary>
/// Handles flamer fuel consumption, feeding from internal cartridge or backpack tank,
/// firing flame stream projectiles, and updating weapon loaded visuals.
/// </summary>
public sealed class FlamerAmmoProviderSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlamerAmmoProviderComponent, MapInitEvent>(OnAmmoProviderMapInit);
        SubscribeLocalEvent<FlamerAmmoProviderComponent, TakeAmmoEvent>(OnTakeAmmo);
        SubscribeLocalEvent<FlamerAmmoProviderComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<FlamerAmmoProviderComponent, GetAmmoCountEvent>(OnGetAmmoCount);
        SubscribeLocalEvent<FlamerAmmoProviderComponent, EntInsertedIntoContainerMessage>(OnContainerInserted);
        SubscribeLocalEvent<FlamerAmmoProviderComponent, EntRemovedFromContainerMessage>(OnContainerRemoved);
    }

    private void OnAmmoProviderMapInit(Entity<FlamerAmmoProviderComponent> ent, ref MapInitEvent args)
    {
        UpdateGunAppearance(ent, ent.Comp);
    }

    private void OnContainerInserted(Entity<FlamerAmmoProviderComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.SlotId)
            UpdateGunAppearance(ent, ent.Comp);
    }

    private void OnContainerRemoved(Entity<FlamerAmmoProviderComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.SlotId)
            UpdateGunAppearance(ent, ent.Comp);
    }

    private void OnAttemptShoot(Entity<FlamerAmmoProviderComponent> ent, ref AttemptShootEvent args)
    {
        if (TryGetActiveFuelTank(ent, ent.Comp, args.User, out _, out var solutionEnt) &&
            solutionEnt.Value.Comp.Solution.Volume >= ent.Comp.FuelCostPerShot)
        {
            var primaryReagent = GetPrimaryReagent(solutionEnt.Value.Comp.Solution);
            if (primaryReagent != null && _proto.HasIndex<ReagentFlameEffectPrototype>(primaryReagent))
                return;
        }

        args.Cancelled = true;
        args.ResetCooldown = true;

        var curTime = _timing.CurTime;
        if (curTime < ent.Comp.LastEmptyPopup + ent.Comp.EmptyPopupCooldown)
            return;

        ent.Comp.LastEmptyPopup = curTime;
        Dirty(ent);

        if (args.User != null)
            _popup.PopupClient(Loc.GetString("flamer-empty"), ent, args.User);
    }

    private void OnTakeAmmo(Entity<FlamerAmmoProviderComponent> ent, ref TakeAmmoEvent args)
    {
        if (!TryGetActiveFuelTank(ent, ent.Comp, args.User, out _, out var solutionEnt))
            return;

        var available = solutionEnt.Value.Comp.Solution.Volume;
        if (available < ent.Comp.FuelCostPerShot)
            return;

        // Extract fuel for this shot
        var splitSolution = _solutions.SplitSolution(solutionEnt.Value, ent.Comp.FuelCostPerShot);
        if (splitSolution.Volume <= FixedPoint2.Zero)
            return;

        var primaryReagent = GetPrimaryReagent(splitSolution);
        if (primaryReagent == null || !_proto.TryIndex<ReagentFlameEffectPrototype>(primaryReagent, out var effect))
            return;

        var shot = Spawn(ent.Comp.ProjectilePrototype, args.Coordinates);

        if (TryComp<FlameProjectileComponent>(shot, out var flameComp))
        {
            flameComp.Effect = effect.ID;
            flameComp.Lifetime = effect.ProjectileLifetime;
            flameComp.Reagent = primaryReagent;
            flameComp.ReagentAmount = splitSolution.Volume;
            flameComp.SpawnTime = _timing.CurTime;
            Dirty(shot, flameComp);
        }

        if (TryComp<ProjectileComponent>(shot, out var proj))
        {
            proj.Damage = effect.Damage;
        }

        if (TryComp<PhysicsComponent>(shot, out var physics))
        {
            _physics.SetLinearDamping(shot, physics, effect.LinearDamping);
        }

        IShootable shootable;
        if (TryComp<CartridgeAmmoComponent>(shot, out var cart))
            shootable = cart;
        else if (TryComp<HitscanAmmoComponent>(shot, out var hitscan))
            shootable = hitscan;
        else
            shootable = EnsureComp<AmmoComponent>(shot);

        args.Ammo.Add((shot, shootable));

        UpdateGunAppearance(ent, ent.Comp);
    }

    private void OnGetAmmoCount(Entity<FlamerAmmoProviderComponent> ent, ref GetAmmoCountEvent args)
    {
        EntityUid? user = null;
        if (_containers.TryGetContainingContainer(ent.Owner, out var container))
        {
            user = container.Owner;
            if (!HasComp<InventoryComponent>(user) &&
                _containers.TryGetContainingContainer(user.Value, out var parentContainer) &&
                HasComp<InventoryComponent>(parentContainer.Owner))
            {
                user = parentContainer.Owner;
            }
        }

        var hasInternal = TryGetInternalTank(ent.Owner, ent.Comp, out _, out var internalSol);

        // If internal tank exists and has fuel, show internal tank count
        if (hasInternal && internalSol!.Value.Comp.Solution.Volume > FixedPoint2.Zero)
        {
            var sol = internalSol.Value.Comp.Solution;
            args.Count = sol.Volume.Int();
            args.Capacity = sol.MaxVolume.Int();
            return;
        }

        // If user is wearing backpack with tank/slots
        if (user != null && TryComp<InventoryComponent>(user.Value, out var inv) &&
            _inventory.TryGetSlotEntity(user.Value, "back", out var backEnt, inv) && backEnt != null)
        {
            FixedPoint2 totalVolume = FixedPoint2.Zero;
            FixedPoint2 totalCapacity = FixedPoint2.Zero;
            bool foundAnyTank = false;

            if (TryComp<ItemSlotsComponent>(backEnt.Value, out var itemSlots))
            {
                foreach (var slot in itemSlots.Slots.Values)
                {
                    if (slot.Item != null &&
                        _solutions.TryGetSolution(slot.Item.Value, ent.Comp.SolutionId, out _, out var sol))
                    {
                        totalVolume += sol.Volume;
                        totalCapacity += sol.MaxVolume;
                        foundAnyTank = true;
                    }
                }
            }

            if (_solutions.TryGetSolution(backEnt.Value, ent.Comp.SolutionId, out _, out var directSol))
            {
                totalVolume += directSol.Volume;
                totalCapacity += directSol.MaxVolume;
                foundAnyTank = true;
            }

            if (foundAnyTank)
            {
                args.Count = totalVolume.Int();
                args.Capacity = totalCapacity.Int();
                return;
            }
        }

        // If internal tank is present (even if empty)
        if (hasInternal)
        {
            var sol = internalSol!.Value.Comp.Solution;
            args.Count = sol.Volume.Int();
            args.Capacity = sol.MaxVolume.Int();
            return;
        }

        args.Count = 0;
        args.Capacity = 0;
    }

    public bool TryGetInternalTank(
        EntityUid gun,
        FlamerAmmoProviderComponent comp,
        [NotNullWhen(true)] out EntityUid? tankUid,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solutionEnt)
    {
        tankUid = null;
        solutionEnt = null;

        if (!_containers.TryGetContainer(gun, comp.SlotId, out var container) ||
            container.ContainedEntities.Count == 0)
        {
            return false;
        }

        var candidate = container.ContainedEntities[0];
        if (!_solutions.TryGetSolution(candidate, comp.SolutionId, out var sol, out _))
            return false;

        tankUid = candidate;
        solutionEnt = sol;
        return true;
    }

    public bool TryGetBackpackTank(
        EntityUid? user,
        FlamerAmmoProviderComponent comp,
        [NotNullWhen(true)] out EntityUid? tankUid,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solutionEnt)
    {
        tankUid = null;
        solutionEnt = null;

        if (user == null || !TryComp<InventoryComponent>(user.Value, out var inv))
            return false;

        if (!_inventory.TryGetSlotEntity(user.Value, "back", out var backEnt, inv) || backEnt == null)
            return false;

        EntityUid? fallbackTank = null;
        Entity<SolutionComponent>? fallbackSol = null;

        // 1. Direct solution on backpack (if the backpack entity itself has a fluid tank solution)
        if (_solutions.TryGetSolution(backEnt.Value, comp.SolutionId, out var directSol, out var directSolution))
        {
            if (directSolution.Volume >= comp.FuelCostPerShot)
            {
                tankUid = backEnt.Value;
                solutionEnt = directSol;
                return true;
            }
            fallbackTank ??= backEnt.Value;
            fallbackSol ??= directSol;
        }

        // 2. ItemSlots on backpack (in case cartridges were slotted into backpack slots)
        if (TryComp<ItemSlotsComponent>(backEnt.Value, out var itemSlots))
        {
            foreach (var slot in itemSlots.Slots.Values)
            {
                if (slot.Item != null &&
                    _solutions.TryGetSolution(slot.Item.Value, comp.SolutionId, out var slotSol, out var slotSolution))
                {
                    if (slotSolution.Volume >= comp.FuelCostPerShot)
                    {
                        tankUid = slot.Item.Value;
                        solutionEnt = slotSol;
                        return true;
                    }
                    fallbackTank ??= slot.Item.Value;
                    fallbackSol ??= slotSol;
                }
            }
        }


        if (fallbackTank != null && fallbackSol != null)
        {
            tankUid = fallbackTank.Value;
            solutionEnt = fallbackSol.Value;
            return true;
        }

        return false;
    }

    public bool TryGetActiveFuelTank(
        EntityUid gun,
        FlamerAmmoProviderComponent comp,
        EntityUid? user,
        [NotNullWhen(true)] out EntityUid? tankUid,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solutionEnt)
    {
        var hasInternal = TryGetInternalTank(gun, comp, out var internalUid, out var internalSol);
        var hasBackpack = TryGetBackpackTank(user, comp, out var backpackUid, out var backpackSol);

        (bool Has, EntityUid? Uid, Entity<SolutionComponent>? Sol) first = comp.PreferInternalTank
            ? (hasInternal, internalUid, internalSol)
            : (hasBackpack, backpackUid, backpackSol);

        (bool Has, EntityUid? Uid, Entity<SolutionComponent>? Sol) second = comp.PreferInternalTank
            ? (hasBackpack, backpackUid, backpackSol)
            : (hasInternal, internalUid, internalSol);

        if (first.Has && first.Sol!.Value.Comp.Solution.Volume >= comp.FuelCostPerShot)
        {
            tankUid = first.Uid!.Value;
            solutionEnt = first.Sol!.Value;
            return true;
        }

        if (second.Has && second.Sol!.Value.Comp.Solution.Volume >= comp.FuelCostPerShot)
        {
            tankUid = second.Uid!.Value;
            solutionEnt = second.Sol!.Value;
            return true;
        }

        // Return whichever tank exists for empty error handling
        if (first.Has)
        {
            tankUid = first.Uid!.Value;
            solutionEnt = first.Sol!.Value;
            return true;
        }

        if (second.Has)
        {
            tankUid = second.Uid!.Value;
            solutionEnt = second.Sol!.Value;
            return true;
        }

        tankUid = null;
        solutionEnt = null;
        return false;
    }

    public void UpdateGunAppearance(EntityUid gun, FlamerAmmoProviderComponent? comp = null)
    {
        if (!Resolve(gun, ref comp, false))
            return;

        var hasTank = _containers.TryGetContainer(gun, comp.SlotId, out var container) &&
                      container.ContainedEntities.Count > 0;

        _appearance.SetData(gun, AmmoVisuals.MagLoaded, hasTank);

        if (TryComp<ItemComponent>(gun, out var item))
            _item.SetHeldPrefix(gun, hasTank ? "loaded" : null, component: item);
    }

    public static string? GetPrimaryReagent(Solution solution)
    {
        if (solution.Volume == FixedPoint2.Zero || solution.Contents.Count == 0)
            return null;

        var max = FixedPoint2.Zero;
        string? top = null;

        foreach (var reagent in solution.Contents)
        {
            if (reagent.Quantity > max)
            {
                max = reagent.Quantity;
                top = reagent.Reagent.Prototype;
            }
        }

        return top;
    }
}
