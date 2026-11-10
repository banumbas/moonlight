using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._Moonlight.ZLevels.Core;

/// <summary>
/// Reuses a viewport's opening search while its tile-aligned bounds, grids and tiles are unchanged.
/// World bounds are cached instead of pixels so smooth camera movement still updates the crop.
/// </summary>
internal sealed class MoonlightZLevelViewOpeningCache
{
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<GridSnapshot> _snapshots = new();
    private readonly List<Box2> _openings = new();
    private MapId _mapId;
    private Box2 _searchBounds;
    private Box2 _openingBounds;
    private bool _captureBounds;
    private bool _hasValue;
    private bool _found;

    internal int Searches { get; private set; }

    public bool TryFindOpeningBounds(
        MapId mapId,
        Box2 worldBounds,
        bool captureBounds,
        MoonlightZLevelOpeningCache openings,
        SharedMapSystem map,
        SharedTransformSystem transform,
        ITileDefinitionManager tiles,
        out Box2 openingBounds)
    {
        // A conservative envelope changes only at tile boundaries, rather than at render FPS.
        var searchBounds = new Box2(MathF.Floor(worldBounds.Left), MathF.Floor(worldBounds.Bottom),
            MathF.Ceiling(worldBounds.Right), MathF.Ceiling(worldBounds.Top));
        _grids.Clear();
        map.FindGridsIntersecting(mapId, searchBounds, ref _grids, approx: true, includeMap: true);

        var unchanged = _hasValue && _mapId == mapId && _searchBounds == searchBounds &&
                        _captureBounds == captureBounds && _snapshots.Count == _grids.Count;
        for (var i = 0; unchanged && i < _grids.Count; i++)
        {
            var grid = _grids[i];
            unchanged = _snapshots[i] == new GridSnapshot(grid.Owner, openings.GetRevision(grid),
                transform.GetWorldMatrix(grid), grid.Comp.TileSize);
        }

        if (!unchanged)
        {
            Searches++;
            _openings.Clear();
            _found = openings.TryFindOpeningBounds(mapId, searchBounds, captureBounds ? _openings : null,
                out _openingBounds, int.MaxValue, exactOpeningBounds: true, _grids, map, transform, tiles);
            if (_grids.Count == 0)
            {
                // A map without terrain is open space, not an opaque floor.
                _found = true;
                _openingBounds = searchBounds;
            }

            _snapshots.Clear();
            foreach (var grid in _grids)
                _snapshots.Add(new GridSnapshot(grid.Owner, openings.GetRevision(grid),
                    transform.GetWorldMatrix(grid), grid.Comp.TileSize));

            _mapId = mapId;
            _searchBounds = searchBounds;
            _captureBounds = captureBounds;
            _hasValue = true;
        }

        openingBounds = _openingBounds;
        return _found;
    }

    private readonly record struct GridSnapshot(EntityUid Uid, ulong Revision, Matrix3x2 Matrix, float TileSize);
}
