using Content.Shared._Classic.Weapons.Ranged.Components;
using Content.Shared._Classic.Weapons.Ranged.Events;
using Content.Shared.Power.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Classic.Weapons.Ranged.Systems;

/// <summary>
/// Shows self-recharging battery weapons as discharged while empty or waiting for their firing-rate delay.
/// </summary>
public sealed partial class BatteryWeaponDischargeVisualsSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BatteryWeaponDischargeVisualsComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<BatteryWeaponDischargeVisualsComponent, BatteryWeaponUpdateVisualsEvent>(OnBatteryVisualsUpdate);
        SubscribeLocalEvent<BatteryWeaponDischargeVisualsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ActiveGunDischargeVisualsComponent, AfterAutoHandleStateEvent>(OnActiveVisualsAfterAutoHandleState);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveGunDischargeVisualsComponent, BatteryWeaponDischargeVisualsComponent>();
        while (query.MoveNext(out var uid, out var active, out var discharge))
        {
            UpdateActiveState((uid, discharge), active);
        }
    }

    private void OnGunShot(Entity<BatteryWeaponDischargeVisualsComponent> ent, ref GunShotEvent args)
    {
        UpdateVisualState(ent);
    }

    private void OnMapInit(Entity<BatteryWeaponDischargeVisualsComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
            return;

        UpdateVisualState(ent, ammo);
    }

    private void OnActiveVisualsAfterAutoHandleState(Entity<ActiveGunDischargeVisualsComponent> ent,
        ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<BatteryWeaponDischargeVisualsComponent>(ent, out var discharge) ||
            !TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
        {
            return;
        }

        ApplyDischargeState((ent.Owner, discharge), IsDischarged(ent, ammo.Shots));
    }

    private void OnBatteryVisualsUpdate(Entity<BatteryWeaponDischargeVisualsComponent> ent,
        ref BatteryWeaponUpdateVisualsEvent args)
    {
        UpdateVisualState(ent, shots: args.Shots);
        args.Handled = true;
    }

    private void UpdateVisualState(Entity<BatteryWeaponDischargeVisualsComponent> ent,
        BatteryAmmoProviderComponent? ammo = null,
        int? shots = null)
    {
        if (ammo == null && !TryComp<BatteryAmmoProviderComponent>(ent, out ammo))
            return;

        var currentShots = shots ?? ammo.Shots;
        UpdateActiveStateMarker(ent, currentShots);
        ApplyDischargeState(ent, IsDischarged(ent, currentShots));
    }

    private void UpdateActiveState(Entity<BatteryWeaponDischargeVisualsComponent> ent,
        ActiveGunDischargeVisualsComponent active)
    {
        var rechargeFinished = active.RechargeEnd is { } rechargeEnd && _timing.CurTime >= rechargeEnd;
        if (rechargeFinished && _net.IsServer && ent.Comp.SoundReload != null)
            _audio.PlayPvs(ent.Comp.SoundReload, ent);

        if (!TryComp<BatteryAmmoProviderComponent>(ent, out var ammo))
        {
            RemComp<ActiveGunDischargeVisualsComponent>(ent);
            return;
        }

        UpdateActiveStateMarker(ent, ammo.Shots, active);
        ApplyDischargeState(ent, IsDischarged(ent, ammo.Shots));

        // Keep tracking an empty weapon even after the self-recharge pause ends. The first restored shot
        // is delivered by BatteryWeaponUpdateVisualsEvent and is then able to turn every view green.
        if (active.RechargeEnd == null && active.FireDelayEnd == null && ammo.Shots != 0)
            RemComp<ActiveGunDischargeVisualsComponent>(ent);
    }

    private void UpdateActiveStateMarker(Entity<BatteryWeaponDischargeVisualsComponent> ent,
        int shots,
        ActiveGunDischargeVisualsComponent? active = null)
    {
        TimeSpan? rechargeEnd = TryGetRechargeEnd(ent, out var recharge) ? recharge : null;
        TimeSpan? fireDelayEnd = TryGetFireDelayEnd(ent, out var fireDelay) ? fireDelay : null;

        if (active == null)
        {
            if (!IsDischarged(ent, shots) && rechargeEnd == null && fireDelayEnd == null)
                return;

            // The server will replicate the marker. Avoid creating a second networked marker while a
            // client is applying that authoritative state.
            if (_timing.ApplyingState)
                return;

            active = EnsureComp<ActiveGunDischargeVisualsComponent>(ent);
        }

        if (active.RechargeEnd == rechargeEnd && active.FireDelayEnd == fireDelayEnd)
            return;

        active.RechargeEnd = rechargeEnd;
        active.FireDelayEnd = fireDelayEnd;
        Dirty(ent, active);
    }

    private bool TryGetRechargeEnd(EntityUid uid, out TimeSpan rechargeEnd)
    {
        rechargeEnd = default;

        if (!TryComp<BatterySelfRechargerComponent>(uid, out var recharger) ||
            recharger.AutoRechargePauseTime <= TimeSpan.Zero ||
            recharger.NextAutoRecharge is not { } nextRecharge ||
            nextRecharge <= _timing.CurTime)
        {
            return false;
        }

        rechargeEnd = nextRecharge;
        return true;
    }

    private bool TryGetFireDelayEnd(EntityUid uid, out TimeSpan fireDelayEnd)
    {
        fireDelayEnd = default;

        if (!_gunQuery.TryComp(uid, out var gun) || gun.NextFire <= _timing.CurTime)
            return false;

        fireDelayEnd = gun.NextFire;
        return true;
    }

    private bool IsDischarged(EntityUid uid, int shots)
    {
        return shots == 0 ||
               _gunQuery.TryComp(uid, out var gun) && gun.NextFire > _timing.CurTime;
    }

    private void ApplyDischargeState(Entity<BatteryWeaponDischargeVisualsComponent> ent,
        bool discharged)
    {
        if (TryComp<AppearanceComponent>(ent, out var appearance))
        {
            // MagazineVisuals with two states treats every partial value as state 0: it only emits
            // state 1 when AmmoCount equals AmmoMax. This component deliberately represents whether
            // the weapon can fire, rather than its numeric charge, so provide the visualizer 0 or 1.
            var visualShots = discharged ? 0 : 1;
            _appearance.SetData(ent, AmmoVisuals.HasAmmo, !discharged, appearance);
            _appearance.SetData(ent, AmmoVisuals.AmmoCount, visualShots, appearance);
            _appearance.SetData(ent, AmmoVisuals.AmmoMax, 1, appearance);
        }

        var stateEv = new BatteryWeaponDischargeStateChangedEvent(discharged);
        RaiseLocalEvent(ent, ref stateEv);
    }
}
