/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using System.Numerics;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Classic.ZLevels.Roof;

/// <summary>
/// Systems that automatically covers tiles with roofs (or removes roofs)
/// if there is a tile on one of the levels above in the ZLevels Map or Grid network.
/// </summary>
public abstract partial class ClassicSharedZLevelsRoofSystem : EntitySystem
{
    [Dependency] protected ClassicSharedZLevelsSystem ZLevel = null!;
    [Dependency] protected SharedRoofSystem Roof = null!;
    [Dependency] protected SharedMapSystem Map = null!;
    [Dependency] protected ITileDefinitionManager TilDefMan = null!;

    [Dependency] protected EntityQuery<MapGridComponent> GridQuery = default!;
    [Dependency] protected EntityQuery<RoofComponent> RoofQuery = default!;
    [Dependency] protected EntityQuery<ClassicZMapComponent> ZMapQuery = default!;
    [Dependency] protected EntityQuery<TransformComponent> XformQuery = default!;

    private readonly HashSet<Vector2i> _changedWorldTiles = new();
    private readonly HashSet<Vector2i> _coveredWorldTiles = new();
    private readonly HashSet<Vector2i> _opaqueOnLevel = new();
    private readonly List<(Vector2i Index, bool Value)> _roofBatch = new();

    /// <summary>
    /// Roof state is server-authoritative and networked. Keeping tile propagation off the client
    /// avoids repeating procedural chunk work when replicated tile batches arrive.
    /// </summary>
    protected void InitializeTilePropagation()
    {
        SubscribeLocalEvent<ClassicZLevelRoofComponent, TileChangedEvent>(OnTileChanged);
    }

    private void OnTileChanged(Entity<ClassicZLevelRoofComponent> ent, ref TileChangedEvent args)
    {
        if (!GridQuery.TryComp(ent, out var currentMapGrid))
            return;

        if (args.Changes.Length == 0)
            return;

        EntityUid? mapUid = null;
        if (ZMapQuery.HasComp(ent))
            mapUid = ent.Owner;
        else if (XformQuery.TryGetComponent(ent, out var xform))
            mapUid = xform.MapUid;

        if (mapUid is { } map && ZMapQuery.HasComp(map))
            OnMapTileChanged(map, ent.Owner, currentMapGrid, args);
        else if (RoofQuery.TryComp(ent, out var currentRoof))
            OnGridTileChanged(ent, currentMapGrid, currentRoof, args);
    }

    /// <summary>
    /// Planetary map path: propagate roof state down through the z-map network.
    /// </summary>
    private void OnMapTileChanged(
        EntityUid mapUid,
        EntityUid currentGridUid,
        MapGridComponent currentMapGrid,
        TileChangedEvent args)
    {
        if (!ZLevel.TryGetMapNetwork(mapUid, out var network))
            return;

        _changedWorldTiles.Clear();
        foreach (var change in args.Changes)
            _changedWorldTiles.Add(ZLevel.GridTileToWorldTile(currentGridUid, currentMapGrid, change.GridIndices));

        _coveredWorldTiles.Clear();

        // Re-evaluate the affected columns from the actual top level downward. This both propagates
        // removals and initializes a newly streamed lower tile from already-existing upper terrain.
        var maps = network.Comp.SortedZLevels;
        for (var mapIndex = maps.Count - 1; mapIndex >= 0; mapIndex--)
        {
            var levelMapUid = maps[mapIndex];
            if (!TryComp<MapComponent>(levelMapUid, out var mapComponent))
                continue;

            _opaqueOnLevel.Clear();

            foreach (var grid in Map.GetAllGrids(mapComponent.MapId))
            {
                // Only planetary/Z grids explicitly enrolled in roof propagation participate.
                // Docked shuttles and unrelated moving grids must not leave stale roof columns
                // behind when they move away.
                if (!HasComp<ClassicZLevelRoofComponent>(grid.Owner))
                    continue;

                _roofBatch.Clear();
                var roof = EnsureComp<RoofComponent>(grid.Owner);

                foreach (var worldTile in _changedWorldTiles)
                {
                    if (!TryWorldTileToLocalTile(grid.Owner, grid.Comp, worldTile, out var localTile))
                        continue;

                    var hasTile = Map.TryGetTileRef(grid.Owner, grid.Comp, localTile, out var tileRef) &&
                        !tileRef.Tile.IsEmpty;

                    // Clear stale bits for an unloaded/removed tile as part of the same batch.
                    _roofBatch.Add((localTile, hasTile && _coveredWorldTiles.Contains(worldTile)));

                    if (!hasTile)
                        continue;

                    var tileDef = (ContentTileDefinition) TilDefMan[tileRef.Tile.TypeId];
                    if (!tileDef.Transparent)
                        _opaqueOnLevel.Add(worldTile);
                }

                Roof.SetRoofs((grid.Owner, grid.Comp, roof), _roofBatch);
            }

            _coveredWorldTiles.UnionWith(_opaqueOnLevel);
        }
    }

    private void OnGridTileChanged(
        EntityUid gridUid,
        MapGridComponent currentMapGrid,
        RoofComponent currentRoof,
        TileChangedEvent args)
    {
        if (!ZLevel.TryGetGridNetwork(gridUid, out var network))
            return;

        if (ZLevel.TryGetGridZDepth(gridUid) is not { } ownDepth)
            return;

        // Topmost first, so `covered` below only ever re-latches to true by walking downward into
        // a level with its own opaque tile — never by "seeing" a level further above.
        var below = network.Comp.Grids
            .Select(g => (Grid: g, Depth: ZLevel.TryGetGridZDepth(g)))
            .Where(x => x.Depth is { } d && d < ownDepth)
            .OrderByDescending(x => x.Depth!.Value)
            .ToList();

        if (below.Count == 0)
            return;

        foreach (var change in args.Changes)
        {
            var worldTile = ZLevel.GridTileToWorldTile(gridUid, currentMapGrid, change.GridIndices);
            var tileDef = (ContentTileDefinition)TilDefMan[change.NewTile.TypeId];
            var roovedAbove = Roof.IsRooved((gridUid, currentMapGrid, currentRoof), change.GridIndices);
            var covered = roovedAbove || !tileDef.Transparent;

            foreach (var (otherGrid, _) in below)
            {
                if (!GridQuery.TryComp(otherGrid, out var otherMapGrid))
                    continue;

                if (!TryWorldTileToLocalTile(otherGrid, otherMapGrid, worldTile, out var localTile))
                    continue;

                if (!Map.TryGetTileRef(otherGrid, otherMapGrid, localTile, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue; // nothing here on this level — neither marked nor able to re-shield below

                var otherRoof = EnsureComp<RoofComponent>(otherGrid);
                Roof.SetRoof((otherGrid, otherMapGrid, otherRoof), localTile, covered);

                var otherTileDef = (ContentTileDefinition)TilDefMan[tileRef.Tile.TypeId];
                if (!otherTileDef.Transparent)
                    covered = true; // this level's own solid tile re-shields everything further down
            }
        }
    }

    /// <summary>
    /// Resolves a world tile (shared X,Y convention across Z-level maps — see
    /// <see cref="ClassicSharedZLevelsSystem"/>'s <c>TryMove</c>) into <paramref name="grid"/>'s own local
    /// tile index. Z-levels are separate MapIds, so this deliberately tags the shared (X,Y) with
    /// <paramref name="gridUid"/>'s own MapId rather than the caller's, exploiting that convention.
    /// Non-throwing: <paramref name="gridUid"/> could have been deleted between being read out of the
    /// z-grid network and this call (e.g. a ZCollapse that ate the whole grid).
    /// </summary>
    private bool TryWorldTileToLocalTile(EntityUid gridUid, MapGridComponent grid, Vector2i worldTile, out Vector2i localTile)
    {
        localTile = default;
        if (!XformQuery.TryGetComponent(gridUid, out var xform))
            return false;

        var worldPos = new Vector2(worldTile.X + 0.5f, worldTile.Y + 0.5f);
        localTile = Map.TileIndicesFor(gridUid, grid, new MapCoordinates(worldPos, xform.MapID));
        return true;
    }
}
