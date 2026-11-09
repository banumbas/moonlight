/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;

namespace Content.Server._Classic.ZLevels.Core;

//WARNING: This file is vibecoded. It WORKS, but i dunno how that works - and we need investigate that and rewrite to more propriate code human style.

/// <summary>
/// Universal z-grid network recalculator driven by <see cref="ClassicZGridConnectorComponent"/>.
/// Sets a dirty flag on any topology event and runs a single recalculation pass per dirty cycle.
/// Network membership is always fully derived from the set of active connector entities.
/// </summary>
public sealed partial class ClassicZGridConnectorSystem : EntitySystem
{
    [Dependency] private ClassicZLevelsSystem _zLevels = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    [Dependency] private EntityQuery<ClassicZGridComponent> _zgridQuery = default!;
    [Dependency] private EntityQuery<ClassicZGridNetworkComponent> _zgridNetworkQuery = default!;
    [Dependency] private EntityQuery<ClassicZMapComponent> _zMapQuery = default!;

    private bool _dirty;

    /// <summary>
    /// Number of completed topology passes. Exposed for diagnostics and performance regression tests.
    /// </summary>
    [ViewVariables]
    public ulong RecalculationCount { get; private set; }

    // Reusable scratch buffers — recalc runs at most once per tick on a single thread,
    // so we clear and reuse rather than allocating fresh collections each pass.
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _adj = new();
    private readonly List<HashSet<EntityUid>> _components = new();
    private readonly Stack<HashSet<EntityUid>> _setPool = new();
    private readonly HashSet<EntityUid> _visited = new();
    private readonly Queue<EntityUid> _bfsQueue = new();
    private readonly Dictionary<EntityUid, EntityUid> _gridToTargetNet = new();
    private readonly HashSet<EntityUid> _claimedNets = new();
    private readonly List<EntityUid> _removeBuffer = new();
    private readonly HashSet<Vector2i> _occupancyChanges = new();

    private HashSet<EntityUid> RentSet()
    {
        return _setPool.Count > 0 ? _setPool.Pop() : new HashSet<EntityUid>();
    }

    private void ReturnSet(HashSet<EntityUid> set)
    {
        set.Clear();
        _setPool.Push(set);
    }

    /// <summary>Schedules a network recalculation on the next tick.</summary>
    public void MarkDirty()
    {
        _dirty = true;
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClassicZGridConnectorComponent, MapInitEvent>(OnConnectorMapInit);
        SubscribeLocalEvent<ClassicZGridConnectorComponent, AnchorStateChangedEvent>(OnConnectorAnchorChanged);
        SubscribeLocalEvent<ClassicZGridConnectorComponent, EntityTerminatingEvent>(OnConnectorTerminating);

        SubscribeLocalEvent<ClassicZGridComponent, EntityTerminatingEvent>(OnGridTerminating);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);

        SubscribeLocalEvent<ClassicZGridNetworkComponent, ComponentShutdown>(OnGridNetworkShutdown);
    }

    private void OnConnectorMapInit(Entity<ClassicZGridConnectorComponent> ent, ref MapInitEvent args)
    {
        _dirty = true;
    }

    private void OnConnectorAnchorChanged(Entity<ClassicZGridConnectorComponent> ent, ref AnchorStateChangedEvent args)
    {
        _dirty = true;
    }

    private void OnConnectorTerminating(Entity<ClassicZGridConnectorComponent> ent, ref EntityTerminatingEvent args)
    {
        _dirty = true;
    }

    private void OnGridTerminating(Entity<ClassicZGridComponent> ent, ref EntityTerminatingEvent args)
    {
        _dirty = true;
    }

    private void OnTileChanged(ref TileChangedEvent ev)
    {
        // A connector only cares whether there is a tile on the level above it. Tile material,
        // sprite or variant changes cannot alter grid topology.
        if (_dirty || ev.Changes.Length == 0)
            return;

        _occupancyChanges.Clear();
        foreach (var change in ev.Changes)
        {
            if (change.EmptyChanged)
                _occupancyChanges.Add(change.GridIndices);
        }

        if (_occupancyChanges.Count == 0)
            return;

        var changedGrid = ev.Entity;
        var changedGridXform = Transform(changedGrid.Owner);
        if (changedGridXform.MapUid is not { } changedMapUid)
            return;

        // Tile indices are local to each grid. Project every connector's current world position
        // into the changed grid so this remains correct for translated and 90-degree-rotated grids.
        // This scan is intentionally done here instead of maintaining a stale spatial cache: connector
        // grids can move, and connector counts are tiny compared with procedural tile batch sizes.
        var query = EntityQueryEnumerator<ClassicZGridConnectorComponent, TransformComponent>();
        while (query.MoveNext(out var connectorUid, out _, out var connectorXform))
        {
            if (!connectorXform.Anchored ||
                connectorXform.GridUid is not { } lowerGridUid ||
                connectorXform.MapUid is not { } lowerMapUid ||
                connectorXform.ParentUid == lowerMapUid ||
                lowerGridUid == changedGrid.Owner ||
                !_zMapQuery.TryComp(lowerMapUid, out var lowerMap) ||
                !_zLevels.TryMapUp((lowerMapUid, lowerMap), out var aboveMap) ||
                aboveMap.Owner != changedMapUid)
            {
                continue;
            }

            var connectorWorldPos = _transform.GetWorldPosition(connectorUid);
            var connectorTile = _mapSystem.TileIndicesFor(
                changedGrid.Owner,
                changedGrid.Comp,
                new MapCoordinates(connectorWorldPos, changedGridXform.MapID));

            if (_occupancyChanges.Contains(connectorTile))
            {
                _dirty = true;
                return;
            }
        }
    }

    private void OnGridSplit(ref GridSplitEvent ev)
    {
        _dirty = true;
    }

    private void OnGridNetworkShutdown(Entity<ClassicZGridNetworkComponent> ent, ref ComponentShutdown args)
    {
        foreach (var grid in ent.Comp.Grids.ToList())
        {
            if (!_zgridQuery.TryComp(grid, out var gc) || gc.Network != ent.Owner)
                continue;
            _zLevels.TryRemoveGridFromNetwork(grid);
        }
    }

    public override void Update(float frameTime)
    {
        if (!_dirty)
            return;

        _dirty = false;
        RecalculateGridNetworks();
    }

    /// <summary>
    /// Reconciles live network membership with the desired connected components by applying the
    /// minimal delta: survivors keep their pose, only joined grids fire <see cref="ClassicGridAddedIntoZNetworkEvent"/>
    /// and only departed grids fire <see cref="ClassicGridRemovedFromZNetworkEvent"/>. Networks are reused (never
    /// torn down and rebuilt) so unrelated members are never re-snapped.
    /// </summary>
    private void RecalculateGridNetworks()
    {
        RecalculationCount++;
        ComputeDesiredComponents();

        // Assign each desired component a target network, reusing the existing network with the
        // largest overlap (so its members stay put). Each existing network is claimed at most once.
        _claimedNets.Clear();
        _gridToTargetNet.Clear();

        foreach (var component in _components)
        {
            var target = PickSurvivorNetwork(component);
            if (!target.IsValid())
                target = _zLevels.CreateGridNetwork().Owner;

            _claimedNets.Add(target);
            foreach (var grid in component)
            {
                _gridToTargetNet[grid] = target;
            }
        }

        // Removals first: any grid whose live network is not its target leaves (raises Unlinked).
        // This frees grids before they are re-added to a different network during a merge/split.
        _removeBuffer.Clear();
        var nq = EntityQueryEnumerator<ClassicZGridNetworkComponent>();
        while (nq.MoveNext(out var netUid, out var nc))
        {
            foreach (var grid in nc.Grids)
            {
                if (!_gridToTargetNet.TryGetValue(grid, out var target) || target != netUid)
                    _removeBuffer.Add(grid);
            }
        }

        foreach (var grid in _removeBuffer)
        {
            _zLevels.TryRemoveGridFromNetwork(grid);
        }

        // Additions: place each grid into its target network if it isn't there already.
        foreach (var (grid, target) in _gridToTargetNet)
        {
            if (!_zgridNetworkQuery.TryComp(target, out var nc))
                continue;
            if (!nc.Grids.Contains(grid))
                _zLevels.TryAddGridToNetwork((target, nc), grid);
        }

        // Reclaim component sets for the next pass.
        foreach (var component in _components)
        {
            ReturnSet(component);
        }

        _components.Clear();
    }

    /// <summary>Existing network sharing the most grids with <paramref name="component"/>, or Invalid.</summary>
    private EntityUid PickSurvivorNetwork(HashSet<EntityUid> component)
    {
        var best = EntityUid.Invalid;
        var bestOverlap = 0;

        var nq = EntityQueryEnumerator<ClassicZGridNetworkComponent>();
        while (nq.MoveNext(out var netUid, out var nc))
        {
            if (_claimedNets.Contains(netUid))
                continue;

            var overlap = 0;
            foreach (var grid in nc.Grids)
            {
                if (component.Contains(grid))
                    overlap++;
            }

            if (overlap <= bestOverlap)
                continue;
            bestOverlap = overlap;
            best = netUid;
        }

        return best;
    }

    private void ComputeDesiredComponents()
    {
        _adj.Clear();

        var query = EntityQueryEnumerator<ClassicZGridConnectorComponent, TransformComponent>();
        while (query.MoveNext(out var connectorUid, out _, out var xform))
        {
            if (!xform.Anchored || xform.GridUid == null || xform.MapUid == null)
                continue;

            if (xform.ParentUid == xform.MapUid)
                continue; //We do not support connecting grid to planet maps right now

            var lowerGridUid = xform.GridUid.Value;
            if (!_zMapQuery.TryComp(xform.MapUid.Value, out var zMap))
                continue;
            if (!_zLevels.TryMapUp((xform.MapUid.Value, zMap), out var aboveMap))
                continue;

            var worldPos = _transform.GetWorldPosition(connectorUid);
            if (!_mapSystem.TryFindGridAt(aboveMap.Owner, worldPos, out var upperGridUid, out var upperGrid))
                continue;
            if (upperGridUid == lowerGridUid)
                continue;

            // TryFindGridAt matches by AABB — verify the tile at this position actually exists
            if (!_mapSystem.TryGetTileRef(upperGridUid, upperGrid, worldPos, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            if (!_adj.TryGetValue(lowerGridUid, out var lowerNeighbors))
                _adj[lowerGridUid] = lowerNeighbors = RentSet();

            if (!_adj.TryGetValue(upperGridUid, out var upperNeighbors))
                _adj[upperGridUid] = upperNeighbors = RentSet();

            lowerNeighbors.Add(upperGridUid);
            upperNeighbors.Add(lowerGridUid);
        }

        // BFS connected components.
        _visited.Clear();

        foreach (var start in _adj.Keys)
        {
            if (!_visited.Add(start))
                continue;

            var comp = RentSet();
            _bfsQueue.Clear();
            _bfsQueue.Enqueue(start);

            while (_bfsQueue.Count > 0)
            {
                var node = _bfsQueue.Dequeue();
                comp.Add(node);
                if (!_adj.TryGetValue(node, out var neighbors))
                    continue;

                foreach (var neighbor in neighbors)
                {
                    if (_visited.Add(neighbor))
                        _bfsQueue.Enqueue(neighbor);
                }
            }

            _components.Add(comp);
        }

        // Return adjacency neighbor sets to the pool (component sets are released after reconciliation).
        foreach (var neighbors in _adj.Values)
        {
            ReturnSet(neighbors);
        }

        _adj.Clear();
    }
}
