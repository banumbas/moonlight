using System.Numerics;
using Content.Server.Decals;
using Content.Server.Power.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Temperature.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Classic.Geyser;

public sealed partial class ClassicGeyserSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<EntityUid> _nearby = new();
    private readonly HashSet<Entity<ClassicGeyserGeneratorComponent>> _generators = new();

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<ClassicGeyserGeneratorComponent> _generatorQuery;
    private EntityQuery<PowerSupplierComponent> _supplierQuery;
    private EntityQuery<DamageableComponent> _damageableQuery;
    private EntityQuery<TemperatureComponent> _temperatureQuery;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _generatorQuery = GetEntityQuery<ClassicGeyserGeneratorComponent>();
        _supplierQuery = GetEntityQuery<PowerSupplierComponent>();
        _damageableQuery = GetEntityQuery<DamageableComponent>();
        _temperatureQuery = GetEntityQuery<TemperatureComponent>();

        SubscribeLocalEvent<ClassicGeyserOutletComponent, MapInitEvent>(OnOutletMapInit);
    }

    private void OnOutletMapInit(Entity<ClassicGeyserOutletComponent> ent, ref MapInitEvent args)
    {
        ScheduleNextEruption(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateOutlets();
        UpdateGenerators();
        UpdateMists();
    }

    private void UpdateOutlets()
    {
        var query = EntityQueryEnumerator<ClassicGeyserOutletComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Erupting)
            {
                if (_timing.CurTime >= comp.NextEruptionTime)
                    StartEruption((uid, comp));

                continue;
            }

            if (_timing.CurTime >= comp.NextMistTime)
            {
                SpawnMistBurst((uid, comp));
                EnergizeGenerators((uid, comp));
                comp.NextMistTime = _timing.CurTime + GetMistSpawnInterval(comp);
            }

            if (_timing.CurTime >= comp.EruptionEndTime)
                StopEruption((uid, comp));
        }
    }

    private void UpdateGenerators()
    {
        var query = EntityQueryEnumerator<ClassicGeyserGeneratorComponent, PowerSupplierComponent>();
        while (query.MoveNext(out _, out var geyser, out var supplier))
        {
            if (!geyser.PoweredByGeyser || _timing.CurTime < geyser.PoweredUntil)
                continue;

            var idleSupply = MathF.Max(0f, geyser.IdleSupply);
            supplier.MaxSupply = idleSupply;
            supplier.Enabled = idleSupply > 0f;
            geyser.PoweredByGeyser = false;
        }
    }

    private void UpdateMists()
    {
        var query = EntityQueryEnumerator<ClassicTemperatureMistComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime < comp.NextTick)
                continue;

            var tickInterval = MathF.Max(0.1f, comp.TickInterval);
            comp.NextTick = _timing.CurTime + TimeSpan.FromSeconds(tickInterval);
            DamageEntitiesInMist(uid, comp, tickInterval);
        }
    }

    private void StartEruption(Entity<ClassicGeyserOutletComponent> ent)
    {
        ent.Comp.Erupting = true;
        ent.Comp.EruptionEndTime = _timing.CurTime + GetEruptionDuration(ent.Comp);
        ent.Comp.NextMistTime = _timing.CurTime;

        var coords = _transform.GetMoverCoordinates(ent.Owner);
        Spawn(ent.Comp.FlashPrototype, coords);
        _audio.PlayPvs(ent.Comp.EruptionSound, ent.Owner);

        if (ent.Comp.LeavesSnowScars)
            SpawnSnowScars(ent.Owner, ent.Comp);
    }

    private void StopEruption(Entity<ClassicGeyserOutletComponent> ent)
    {
        ent.Comp.Erupting = false;
        ScheduleNextEruption(ent);
    }

    private void ScheduleNextEruption(Entity<ClassicGeyserOutletComponent> ent)
    {
        var min = MathF.Max(0.1f, ent.Comp.MinDormantTime);
        var max = MathF.Max(min, ent.Comp.MaxDormantTime);
        var delay = min.Equals(max) ? min : _random.NextFloat(min, max);
        ent.Comp.NextEruptionTime = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }

    private void SpawnMistBurst(Entity<ClassicGeyserOutletComponent> ent)
    {
        if (!TryGetTile(ent.Owner, out var gridUid, out _, out var grid, out var centerCoords))
            return;

        var radius = GetMistRadius(ent.Comp);
        var count = Math.Max(1, ent.Comp.MistBurstCount);
        var center = centerCoords.Position;
        TrySpawnMist(ent.Comp.MistPrototype, gridUid, grid, center);

        for (var i = 1; i < count; i++)
        {
            var distance = radius.Equals(0f) ? 0f : _random.NextFloat(0f, radius);
            var offset = _random.NextAngle().ToVec() * distance;
            TrySpawnMist(ent.Comp.MistPrototype, gridUid, grid, center + offset);
        }
    }

    private void TrySpawnMist(EntProtoId prototype, EntityUid gridUid, MapGridComponent grid, Vector2 position)
    {
        if (!_map.TryGetTileRef(gridUid, grid, position, out var tile) || tile.Tile.IsEmpty)
            return;

        Spawn(prototype, new EntityCoordinates(gridUid, position));
    }

    private void EnergizeGenerators(Entity<ClassicGeyserOutletComponent> ent)
    {
        if (!TryGetTile(ent.Owner, out var gridUid, out var outletTile, out _, out _))
            return;

        var coords = _transform.GetMoverCoordinates(ent.Owner);
        _generators.Clear();
        _lookup.GetEntitiesInRange(coords, 0.55f, _generators);

        foreach (var generatorEnt in _generators)
        {
            var generatorUid = generatorEnt.Owner;

            if (!TryGetTile(generatorUid, out var generatorGridUid, out var generatorTile, out _, out _) ||
                generatorGridUid != gridUid ||
                generatorTile != outletTile ||
                !_supplierQuery.TryComp(generatorUid, out var supplier))
            {
                continue;
            }

            var supply = MathF.Max(0f, ent.Comp.GeneratorSupply);
            supplier.MaxSupply = supply;
            supplier.Enabled = supply > 0f;
            generatorEnt.Comp.PoweredByGeyser = true;
            generatorEnt.Comp.PoweredUntil = _timing.CurTime + GetGeneratorOutputDuration(ent.Comp);
        }
    }

    private void DamageEntitiesInMist(EntityUid mistUid, ClassicTemperatureMistComponent mist, float tickInterval)
    {
        var coords = _transform.GetMoverCoordinates(mistUid);
        _nearby.Clear();
        _lookup.GetEntitiesInRange(coords, GetMistRadius(mist), _nearby);

        foreach (var target in _nearby)
        {
            if (target == mistUid ||
                !HasComp<MobStateComponent>(target) ||
                !_damageableQuery.TryComp(target, out var damageable))
            {
                continue;
            }

            _damageable.TryChangeDamage((target, damageable), mist.Damage, mist.IgnoreResistances, false, mistUid);

            if (_temperatureQuery.TryComp(target, out var temperature))
                _temperature.ChangeHeat(target, mist.HeatPerSecond * tickInterval, mist.IgnoreResistances, temperature);
        }
    }

    private void SpawnSnowScars(EntityUid uid, ClassicGeyserOutletComponent comp)
    {
        if (!TryGetTile(uid, out var gridUid, out _, out var grid, out var centerCoords))
            return;

        TryAddSnowScar(comp, gridUid, grid, centerCoords.Position);

        for (var i = 0; i < 5; i++)
        {
            var distance = _random.NextFloat(0.35f, 2.25f);
            var position = centerCoords.Position + _random.NextAngle().ToVec() * distance;
            TryAddSnowScar(comp, gridUid, grid, position);
        }
    }

    private void TryAddSnowScar(ClassicGeyserOutletComponent comp, EntityUid gridUid, MapGridComponent grid, Vector2 position)
    {
        if (!_map.TryGetTileRef(gridUid, grid, position, out var tile) ||
            tile.Tile.IsEmpty ||
            !_tileDef.TryGetDefinition(tile.Tile.TypeId, out var tileDef) ||
            !tileDef.ID.Contains("Snow", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var coords = new EntityCoordinates(gridUid, position);
        _decals.TryAddDecal(comp.ScarDecal, coords, out _, Color.FromHex("#9D3DFFCC"), _random.NextAngle(), 1);
    }

    private bool TryGetTile(
        EntityUid uid,
        out EntityUid gridUid,
        out Vector2i tile,
        out MapGridComponent grid,
        out EntityCoordinates coords)
    {
        if (!_xformQuery.TryComp(uid, out var xform) || xform.GridUid is not { } foundGridUid || !_gridQuery.TryComp(foundGridUid, out var gridComp))
        {
            gridUid = default;
            tile = default;
            grid = default!;
            coords = default;
            return false;
        }

        gridUid = foundGridUid;
        grid = gridComp;
        coords = xform.Coordinates;
        tile = _map.TileIndicesFor(gridUid, grid, coords);
        return true;
    }

    private static TimeSpan GetEruptionDuration(ClassicGeyserOutletComponent component)
    {
        return TimeSpan.FromSeconds(MathF.Max(0.1f, component.EruptionDuration));
    }

    private static TimeSpan GetMistSpawnInterval(ClassicGeyserOutletComponent component)
    {
        return TimeSpan.FromSeconds(MathF.Max(0.1f, component.MistSpawnInterval));
    }

    private static TimeSpan GetGeneratorOutputDuration(ClassicGeyserOutletComponent component)
    {
        return TimeSpan.FromSeconds(MathF.Max(0f, component.GeneratorOutputDuration));
    }

    private static float GetMistRadius(ClassicGeyserOutletComponent component)
    {
        return MathF.Max(0f, component.MistRadius);
    }

    private static float GetMistRadius(ClassicTemperatureMistComponent component)
    {
        return MathF.Max(0f, component.Radius);
    }
}
