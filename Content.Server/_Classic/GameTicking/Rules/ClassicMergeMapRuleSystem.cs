using System.Linq;
using Content.Server._Classic.GameTicking.Rules.Components;
using Content.Server._Classic.Station;
using Content.Server._Classic.ZLevels.Core;
using Content.Server._Classic.ZLevels.Core.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Parallax;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Mapping.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Classic.GameTicking.Rules;

/// <summary>Places complete map structures into an existing station Z-network.</summary>
public sealed partial class ClassicMergeMapRuleSystem : EntitySystem
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ClassicZLevelsSystem _zLevels = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private BiomeSystem _biome = default!;

    private readonly Dictionary<EntityUid, EntityUid?> _pendingRules = new();
    private readonly Dictionary<MergedMapKey, LoadedMapData> _loadedMaps = new();
    private readonly Dictionary<EntityUid, Box2> _stationBounds = new();
    private EntityUid? _addingStationNetwork;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClassicMergeMapRuleComponent, GameRuleAddedEvent>(OnRuleAdded);
        SubscribeLocalEvent<ClassicMergeMapRuleComponent, GameRuleEndedEvent>(OnRuleEnded);
        SubscribeLocalEvent<ClassicZLevelMapNetworkReadyEvent>(OnMapNetworkReady);
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit,
            after: [typeof(ClassicStationBiomeSystem), typeof(ClassicZLevelsSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRuleAdded(Entity<ClassicMergeMapRuleComponent> ent, ref GameRuleAddedEvent args)
    {
        _pendingRules[ent] = _addingStationNetwork;
        TryMergeRuleMap(ent, _addingStationNetwork);
    }

    private void OnRuleEnded(Entity<ClassicMergeMapRuleComponent> ent, ref GameRuleEndedEvent args)
    {
        _pendingRules.Remove(ent);
    }

    private void OnMapNetworkReady(ClassicZLevelMapNetworkReadyEvent args)
    {
        RetryPendingRules(args.Network);
    }

    private void OnStationPostInit(ref StationPostInitEvent args)
    {
        var ent = args.Station;
        if (!TryComp<ClassicStationZLevelsComponent>(ent, out var zLevels) ||
            zLevels.ZNetworkEntity is not { } network || _stationBounds.ContainsKey(ent) ||
            _station.GetLargestGrid(ent.Owner) is not { } mainGrid || !TryComp<MapGridComponent>(mainGrid, out var grid))
        {
            return;
        }

        // Capture the authored station bounds before biome streaming expands its terrain grid.
        _stationBounds.Add(ent, _transform.GetWorldMatrix(mainGrid).TransformBox(grid.LocalAABB));
        RetryPendingRules(network);

        if (!TryComp<ClassicStationMapRulesComponent>(ent, out var rules))
            return;

        var previousNetwork = _addingStationNetwork;
        _addingStationNetwork = network;
        try
        {
            foreach (var rule in rules.Rules)
                _ticker.AddGameRule(rule);
        }
        finally
        {
            _addingStationNetwork = previousNetwork;
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _pendingRules.Clear();
        _loadedMaps.Clear();
        _stationBounds.Clear();
    }

    private void RetryPendingRules(EntityUid network)
    {
        foreach (var (uid, boundNetwork) in _pendingRules.ToArray())
        {
            if (!TryComp<ClassicMergeMapRuleComponent>(uid, out var rule))
            {
                _pendingRules.Remove(uid);
                continue;
            }

            if (boundNetwork == null || boundNetwork == network)
                TryMergeRuleMap((uid, rule), network);
        }
    }

    private bool TryMergeRuleMap(Entity<ClassicMergeMapRuleComponent> rule, EntityUid? requiredNetwork)
    {
        if (!TryFindTargetStation(requiredNetwork, out var station, out var network))
            return false;

        var key = new MergedMapKey(network, rule.Comp.MapPath, rule.Comp.ZMap, rule.Comp.TargetDepth);
        if (_loadedMaps.TryGetValue(key, out var existing))
        {
            NotifyRule(rule, existing);
            return true;
        }

        if (!TryGetSourceMaps(rule.Comp, out var paths))
            return FailRule(rule, "Specify exactly one valid mapPath or nonempty zMap bundle.");

        var levels = new List<LoadedLevel>();
        var movedRoots = new List<EntityUid>();
        var committed = false;
        try
        {
            // Validate every destination before deserializing any source map.
            for (var i = 0; i < paths.Count; i++)
            {
                if (!_zLevels.TryGetMapAtDepth(network.AsNullable(), rule.Comp.TargetDepth + i, out var target) ||
                    !TryComp<MapComponent>(target, out var targetMap))
                {
                    return FailRule(rule, $"The station has no Z-depth {rule.Comp.TargetDepth + i}.");
                }
                levels.Add(new LoadedLevel(paths[i], (target, targetMap)));
            }

            Entity<MapComponent>? ground = null;
            if (rule.Comp.BiomeGroundDepth is { } groundDepth)
            {
                if (!_zLevels.TryGetMapAtDepth(network.AsNullable(), groundDepth, out var groundUid) ||
                    !TryComp<MapComponent>(groundUid, out var groundMap) ||
                    !TryGetTerrain((groundUid, groundMap), out _))
                {
                    return FailRule(rule, $"No planetary terrain exists at Z-depth {groundDepth}.");
                }
                ground = (groundUid, groundMap);
            }

            foreach (var level in levels)
            {
                // Default loading leaves the staging map uninitialized and paused. MapInit only runs at the final pose.
                if (!_mapLoader.TryLoadMap(level.Path, out var source, out var grids))
                    return FailRule(rule, $"Could not load {level.Path}.");

                level.Source = source;
                level.Grids.AddRange(grids);
                if (grids.Count == 0 || HasComp<MapGridComponent>(source))
                    return FailRule(rule, $"{level.Path} must contain grids parented to a separate map.");
            }

            if (!TryFindPlacement(rule.Comp, _stationBounds[station], levels, ground, out var offset, out var reservations))
                return FailRule(rule, "No free position was found for the complete structure.");

            // All loading and placement checks succeeded; terrain and structures are now committed together.
            foreach (var reservation in reservations.Values)
            {
                _biome.ReserveStructureTiles(reservation.Terrain, reservation.FillTiles, false, reservation.RemovableEntities);
                _biome.ReserveStructureTiles(reservation.Terrain, reservation.ClearTiles, true, reservation.RemovableEntities);
            }

            var allGrids = new List<EntityUid>();
            foreach (var level in levels)
            {
                var roots = new List<EntityUid>();
                var children = Transform(level.Source!.Value).ChildEnumerator;
                while (children.MoveNext(out var child))
                    roots.Add(child);

                foreach (var child in roots)
                {
                    var xform = Transform(child);
                    _transform.SetCoordinates(child, xform, new EntityCoordinates(level.Target, xform.LocalPosition + offset));
                    movedRoots.Add(child);
                }

                foreach (var grid in level.Grids)
                    allGrids.Add(grid);
            }

            InitializeMovedEntities(movedRoots);
            var loaded = new LoadedMapData(levels[0].Target.Comp.MapId, allGrids);
            _loadedMaps.Add(key, loaded);
            committed = true;
            NotifyRule(rule, loaded);
            return true;
        }
        finally
        {
            if (!committed)
            {
                foreach (var root in movedRoots)
                {
                    if (Exists(root))
                        Del(root);
                }
            }

            foreach (var level in levels)
            {
                if (level.Source is { } source && Exists(source))
                    Del(source);
            }
        }
    }

    private bool TryGetSourceMaps(ClassicMergeMapRuleComponent rule, out IReadOnlyList<ResPath> paths)
    {
        paths = [];
        if (rule.MapPath.HasValue == rule.ZMap.HasValue)
            return false;
        if (rule.MapPath is { } path)
        {
            paths = [path];
            return true;
        }
        if (!_prototype.TryIndex(rule.ZMap, out var bundle) || bundle.Maps.Count == 0)
            return false;
        paths = bundle.Maps;
        return true;
    }

    private bool TryFindTargetStation(
        EntityUid? requiredNetwork,
        out EntityUid stationUid,
        out Entity<ClassicZMapNetworkComponent> network)
    {
        stationUid = default;
        network = default;
        var stations = EntityQueryEnumerator<ClassicStationZLevelsComponent>();
        while (stations.MoveNext(out var uid, out var station))
        {
            if (!_stationBounds.ContainsKey(uid) || station.ZNetworkEntity is not { } networkUid ||
                requiredNetwork is { } required && networkUid != required ||
                !TryComp<ClassicZMapNetworkComponent>(networkUid, out var candidate))
            {
                continue;
            }

            stationUid = uid;
            network = (networkUid, candidate);
            return true;
        }
        return false;
    }

    private void InitializeMovedEntities(List<EntityUid> entities)
    {
        // The loader's merge path uses the same parent-before-child initialization. Running it
        // here keeps MapInit handlers on their destination map and Z-network.
        for (var i = 0; i < entities.Count; i++)
        {
            var uid = entities[i];
            if (!TryComp(uid, out TransformComponent? xform) ||
                !TryComp(uid, out MetaDataComponent? metadata) || metadata.EntityLifeStage != EntityLifeStage.Initialized ||
                !_map.IsInitialized(xform.MapID))
            {
                continue;
            }

            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var child))
                entities.Add(child);
            EntityManager.RunMapInit(uid, metadata);
        }
    }

    private bool FailRule(EntityUid rule, string reason)
    {
        Log.Error($"Could not place {ToPrettyString(rule)}: {reason}");
        _pendingRules.Remove(rule);
        _ticker.EndGameRule(rule);
        return false;
    }

    private void NotifyRule(EntityUid rule, LoadedMapData loaded)
    {
        _pendingRules.Remove(rule);
        var ev = new RuleLoadedGridsEvent(loaded.MapId, loaded.Grids);
        RaiseLocalEvent(rule, ref ev);
    }

    private readonly record struct MergedMapKey(
        EntityUid Network,
        ResPath? MapPath,
        ProtoId<ClassicZLevelMapPrototype>? ZMap,
        int Depth);

    private sealed record LoadedMapData(MapId MapId, IReadOnlyList<EntityUid> Grids);

    private sealed class LoadedLevel(ResPath path, Entity<MapComponent> target)
    {
        public readonly ResPath Path = path;
        public readonly Entity<MapComponent> Target = target;
        public readonly List<Entity<MapGridComponent>> Grids = new();
        public Entity<MapComponent>? Source;
    }
}
