using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Moonlight.Salvage.Fulton;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Moonlight.Salvage.Fulton;

public sealed partial class MoonlightCargoFultonSystem : SharedMoonlightCargoFultonSystem
{
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly TimeSpan SaleDelay = TimeSpan.FromSeconds(0.8);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoonlightFultonSoldComponent, ComponentStartup>(OnFultonedStartup);
        SubscribeLocalEvent<MoonlightFultonSoldComponent, ComponentShutdown>(OnFultonedShutdown);
    }

    private void OnFultonedStartup(Entity<MoonlightFultonSoldComponent> ent, ref ComponentStartup args)
    {
        if (Exists(ent.Comp.Effect))
            return;

        ent.Comp.Effect = Spawn(EffectProto, new EntityCoordinates(ent.Owner, EffectOffset));
        Dirty(ent);
    }

    private void OnFultonedShutdown(Entity<MoonlightFultonSoldComponent> ent, ref ComponentShutdown args)
    {
        Del(ent.Comp.Effect);
        ent.Comp.Effect = EntityUid.Invalid;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MoonlightFultonSoldComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.SaleTime is { } saleTime)
            {
                if (_timing.CurTime >= saleTime)
                    FinishSale(uid, component);

                continue;
            }

            if (component.NextFulton <= _timing.CurTime)
                LaunchSale(uid, component);
        }
    }

    protected override bool CanCompleteCargoFulton(
        EntityUid fultonUid,
        EntityUid targetUid,
        EntityUid userUid,
        MoonlightCargoFultonComponent component)
    {
        if (_cargo.MoonlightCanSellEntity(targetUid) && GetSaleStation(targetUid) != null)
            return true;

        _popup.PopupEntity(Loc.GetString("fulton-invalid"), targetUid, userUid);
        return false;
    }

    protected override void OnCargoFultonApplied(
        EntityUid fultonUid,
        EntityUid targetUid,
        EntityUid userUid,
        MoonlightCargoFultonComponent fulton,
        MoonlightFultonSoldComponent fultoned)
    {
        fultoned.SaleStation = GetSaleStation(targetUid);
    }

    private void LaunchSale(EntityUid uid, MoonlightFultonSoldComponent component)
    {
        var station = component.SaleStation;
        if (station == null || Deleted(station.Value))
            station = GetSaleStation(uid);

        if (!_cargo.MoonlightCanSellEntity(uid) ||
            !CanCargoFulton(uid) ||
            station is not { } saleStation)
        {
            CancelSale(uid);
            return;
        }

        component.SaleStation = saleStation;
        component.OriginalCoordinates = _transform.GetMoverCoordinates(uid);
        component.SaleTime = _timing.CurTime + SaleDelay;
        Dirty(uid, component);

        PlayFultonAnimation(uid, component, component.OriginalCoordinates.Value);
        _transform.DetachEntity(uid);
    }

    private EntityUid? GetSaleStation(EntityUid target)
    {
        if (TryFindActiveSensorTower(target, out var towerUid))
            return _station.GetOwningStation(towerUid) ?? _station.GetOwningStation(target);

        return _station.GetOwningStation(target);
    }

    private void FinishSale(EntityUid uid, MoonlightFultonSoldComponent component)
    {
        if (component.SaleStation is { } station && _cargo.MoonlightTrySellEntity(uid, station, out _))
            return;

        if (component.OriginalCoordinates is { } coordinates && coordinates.IsValid(EntityManager))
            _transform.SetCoordinates(uid, coordinates);

        CancelSale(uid);
    }

    private void CancelSale(EntityUid uid)
    {
        RemCompDeferred<MoonlightFultonSoldComponent>(uid);
    }

    private void PlayFultonAnimation(EntityUid uid, MoonlightFultonSoldComponent component, EntityCoordinates oldCoords)
    {
        var metadata = MetaData(uid);

        RaiseNetworkEvent(new MoonlightFultonAnimationMessage()
        {
            Entity = GetNetEntity(uid, metadata),
            Coordinates = GetNetCoordinates(oldCoords),
        });

        _audio.PlayPvs(component.Sound, uid);
    }
}
