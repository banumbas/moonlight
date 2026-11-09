using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared._Classic.Weapons.Ranged.Components;
using Content.Shared._Classic.Weapons.Ranged.Events;
using Content.Shared.Item;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Classic.Weapons.Ranged.Systems;

/// <summary>
/// Applies empty held and equipped prefixes while a self-recharging battery weapon is empty or fire-rate limited.
/// </summary>
public sealed partial class BatteryWeaponDischargePrefixSystem : EntitySystem
{
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BatteryWeaponDischargePrefixComponent, BatteryWeaponDischargeStateChangedEvent>(OnDischargeStateChanged);
        SubscribeLocalEvent<BatteryWeaponDischargePrefixComponent, GunShotEvent>(OnGunShotFallback);
        SubscribeLocalEvent<BatteryWeaponDischargePrefixComponent, BatteryWeaponUpdateVisualsEvent>(OnBatteryVisualsUpdateFallback);
        SubscribeLocalEvent<BatteryWeaponDischargePrefixComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BatteryWeaponDischargePrefixComponent, ItemWieldedEvent>(OnItemWielded,
            after: [typeof(SharedWieldableSystem)]);
        SubscribeLocalEvent<BatteryWeaponDischargePrefixComponent, ItemUnwieldedEvent>(OnItemUnwielded,
            after: [typeof(SharedWieldableSystem)]);
        SubscribeLocalEvent<ActiveGunDischargePrefixComponent, AfterAutoHandleStateEvent>(OnActivePrefixAfterAutoHandleState);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveGunDischargePrefixComponent, BatteryWeaponDischargePrefixComponent>();
        while (query.MoveNext(out var uid, out var active, out var discharge))
        {
            UpdateActiveState((uid, discharge), active);
        }
    }

    private void OnDischargeStateChanged(Entity<BatteryWeaponDischargePrefixComponent> ent,
        ref BatteryWeaponDischargeStateChangedEvent args)
    {
        // The authoritative prefix marker is replicated with the visual marker. Do not create another
        // networked component while applying that state; its AfterAutoHandleState callback will apply it.
        if (args.Discharged && _timing.ApplyingState && !HasComp<ActiveGunDischargePrefixComponent>(ent))
            return;

        UpdatePrefixes(ent, args.Discharged);
    }

    private void OnMapInit(Entity<BatteryWeaponDischargePrefixComponent> ent, ref MapInitEvent args)
    {
        if (HasComp<BatteryWeaponDischargeVisualsComponent>(ent) ||
            !TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
            return;

        UpdatePrefixes(ent, IsDischarged(ent, ammo.Shots));
    }

    private void OnGunShotFallback(Entity<BatteryWeaponDischargePrefixComponent> ent, ref GunShotEvent args)
    {
        if (HasComp<BatteryWeaponDischargeVisualsComponent>(ent) ||
            !TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
        {
            return;
        }

        UpdatePrefixes(ent, IsDischarged(ent, ammo.Shots));
    }

    private void OnBatteryVisualsUpdateFallback(Entity<BatteryWeaponDischargePrefixComponent> ent,
        ref BatteryWeaponUpdateVisualsEvent args)
    {
        if (HasComp<BatteryWeaponDischargeVisualsComponent>(ent))
            return;

        UpdatePrefixes(ent, IsDischarged(ent, args.Shots));
    }

    private void OnActivePrefixAfterAutoHandleState(Entity<ActiveGunDischargePrefixComponent> ent,
        ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<BatteryWeaponDischargePrefixComponent>(ent, out var discharge) ||
            !TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
        {
            return;
        }

        UpdatePrefixes((ent.Owner, discharge), IsDischarged(ent, ammo.Shots));
    }

    private void OnItemWielded(Entity<BatteryWeaponDischargePrefixComponent> ent, ref ItemWieldedEvent args)
    {
        if (TryComp<ActiveGunDischargePrefixComponent>(ent, out _) &&
            TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
        {
            UpdatePrefixes(ent, IsDischarged(ent, ammo.Shots));
        }
    }

    private void OnItemUnwielded(Entity<BatteryWeaponDischargePrefixComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (TryComp<ActiveGunDischargePrefixComponent>(ent, out _) &&
            TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
        {
            UpdatePrefixes(ent, IsDischarged(ent, ammo.Shots));
        }
    }

    private void StartDischarge(Entity<BatteryWeaponDischargePrefixComponent> ent,
        out ActiveGunDischargePrefixComponent active)
    {
        if (TryComp<ActiveGunDischargePrefixComponent>(ent, out var existing))
        {
            active = existing;
            return;
        }

        active = EnsureComp<ActiveGunDischargePrefixComponent>(ent);
        active.ReadyHeldPrefix = GetReadyHeldPrefix(ent);
        active.ReadyEquippedPrefix = GetReadyEquippedPrefix(ent);
        Dirty(ent, active);
    }

    private void UpdateActiveState(Entity<BatteryWeaponDischargePrefixComponent> ent,
        ActiveGunDischargePrefixComponent active)
    {
        if (!TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
            return;

        UpdatePrefixes(ent, IsDischarged(ent, ammo.Shots));
    }

    private void EndDischarge(Entity<BatteryWeaponDischargePrefixComponent> ent,
        ActiveGunDischargePrefixComponent active)
    {
        ApplyReadyPrefix(ent, active.ReadyHeldPrefix, active.ReadyEquippedPrefix);

        if (!_timing.ApplyingState)
            RemComp<ActiveGunDischargePrefixComponent>(ent);
    }

    private void UpdatePrefixes(Entity<BatteryWeaponDischargePrefixComponent> ent,
        bool discharged)
    {
        if (discharged)
        {
            StartDischarge(ent, out _);
            ApplyDischargedPrefix(ent);
            return;
        }

        if (!TryComp<ActiveGunDischargePrefixComponent>(ent, out var active))
            return;

        EndDischarge(ent, active);
    }

    private bool IsDischarged(EntityUid uid, int shots)
    {
        return shots == 0 ||
               _gunQuery.TryComp(uid, out var gun) && gun.NextFire > _timing.CurTime;
    }

    private void ApplyDischargedPrefix(Entity<BatteryWeaponDischargePrefixComponent> ent)
    {
        var prefix = ent.Comp.EmptyHeldPrefix;
        if (TryComp<WieldableComponent>(ent, out var wieldable) &&
            wieldable.Wielded &&
            wieldable.WieldedInhandPrefix is { } wieldedPrefix)
        {
            prefix = $"{prefix}-{wieldedPrefix}";
        }

        _item.SetHeldPrefix(ent, prefix);

        if (ent.Comp.EmptyEquippedPrefix != null &&
            TryComp<ClothingComponent>(ent, out var clothing))
        {
            _clothing.SetEquippedPrefix(ent, ent.Comp.EmptyEquippedPrefix, clothing);
        }
    }

    private string? GetReadyHeldPrefix(Entity<BatteryWeaponDischargePrefixComponent> ent)
    {
        if (ent.Comp.ReadyHeldPrefix != null)
            return ent.Comp.ReadyHeldPrefix;

        if (TryComp<WieldableComponent>(ent, out var wieldable) && wieldable.Wielded)
            return wieldable.OldInhandPrefix;

        return TryComp<ItemComponent>(ent, out var item) ? item.HeldPrefix : null;
    }

    private string? GetReadyEquippedPrefix(Entity<BatteryWeaponDischargePrefixComponent> ent)
    {
        if (ent.Comp.ReadyEquippedPrefix != null)
            return ent.Comp.ReadyEquippedPrefix;

        return TryComp<ClothingComponent>(ent, out var clothing) ? clothing.EquippedPrefix : null;
    }

    private void ApplyReadyPrefix(Entity<BatteryWeaponDischargePrefixComponent> ent,
        string? readyHeldPrefix,
        string? readyEquippedPrefix)
    {
        var prefix = readyHeldPrefix;
        if (TryComp<WieldableComponent>(ent, out var wieldable) && wieldable.Wielded)
            prefix = wieldable.WieldedInhandPrefix;

        _item.SetHeldPrefix(ent, prefix);

        if (ent.Comp.EmptyEquippedPrefix != null &&
            TryComp<ClothingComponent>(ent, out var clothing))
        {
            _clothing.SetEquippedPrefix(ent, readyEquippedPrefix, clothing);
        }
    }
}
