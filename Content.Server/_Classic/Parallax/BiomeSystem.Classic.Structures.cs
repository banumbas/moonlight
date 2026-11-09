// Namespace does not match folder structure
#pragma warning disable IDE0130
using Content.Server._Classic.Station;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

public sealed partial class BiomeSystem
{
    /// <summary>
    /// Checks a structure's footprint without changing terrain. Appends pristine procedural entities
    /// that placement may remove; edited terrain and entities are preserved by rejecting the site.
    /// </summary>
    public bool CanReserveStructureTiles(
        Entity<BiomeComponent, MapGridComponent> terrain,
        HashSet<Vector2i> tiles,
        HashSet<EntityUid> removableEntities)
    {
        var biome = terrain.Comp1;
        var grid = terrain.Comp2;
        var depth = ClassicBiomeDepth(terrain);
        _classicStreamingQuery.TryComp(terrain, out var streaming);

        foreach (var tile in tiles)
        {
            var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
            if (biome.ModifiedTiles.TryGetValue(chunk, out var modified) && modified.Contains(tile))
                return false;

            if (_mapSystem.TryGetTileRef(terrain, grid, tile, out var existing) && !existing.Tile.IsEmpty &&
                (!TryGetTile(tile, biome.Layers, biome.Seed, (terrain, grid), out var expected) ||
                 existing.Tile != expected.Value))
            {
                return false;
            }

            ClassicBiomeMarkerCellState? marker = null;
            if (streaming != null && streaming.MarkerCells.TryGetValue(chunk, out var markers))
                markers.TryGetValue(tile, out marker);
            if (marker is { Modified: true })
                return false;

            var trackedCount = 0;
            if (biome.LoadedEntities.TryGetValue(chunk, out var loaded))
            {
                foreach (var (entity, cell) in loaded)
                {
                    if (cell != tile)
                        continue;

                    if (marker != null && !marker.Entities.Contains(entity) ||
                        Deleted(entity) || _classicAlwaysUnloadQuery.HasComponent(entity) ||
                        !_xformQuery.TryGetComponent(entity, out var xform) ||
                        xform.ParentUid != terrain.Owner ||
                        (marker == null || marker.Root != entity || marker.BaselineComponents == null) &&
                        _mapSystem.LocalToTile(terrain, grid, xform.Coordinates) != tile ||
                        ClassicHasPersistentBiomeDependent(terrain, grid, tile, entity) ||
                        !(marker == null
                            ? ClassicCanUnloadTrackedBiomeEntity(biome, terrain, grid, tile, entity, depth)
                            : ClassicCanUnloadMarkerEntity(marker, terrain, entity, depth)))
                    {
                        return false;
                    }

                    removableEntities.Add(entity);
                    trackedCount++;
                }
            }

            if (marker != null && marker.Entities.Count != trackedCount)
                return false;

            var anchored = _mapSystem.GetAnchoredEntities(terrain, grid, tile);
            while (anchored.MoveNext(out var entity))
            {
                if (!removableEntities.Contains(entity.Value))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Commits a previously checked footprint. Same-level structures replace native tiles; structures
    /// above the terrain retain permanent ground. Reserved cells cannot regenerate flora or floors.
    /// </summary>
    public void ReserveStructureTiles(
        Entity<BiomeComponent, MapGridComponent> terrain,
        HashSet<Vector2i> tiles,
        bool clearTiles,
        HashSet<EntityUid> removableEntities)
    {
        var biome = terrain.Comp1;
        var grid = terrain.Comp2;
        var changes = new List<(Vector2i, Tile)>();
        var entityRemovals = new List<EntityUid>();
        var decalRemovals = new List<uint>();
        _classicStreamingQuery.TryComp(terrain, out var streaming);

        foreach (var tile in tiles)
        {
            var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
            biome.ModifiedTiles.GetOrNew(chunk).Add(tile);

            if (biome.LoadedEntities.TryGetValue(chunk, out var loaded))
            {
                entityRemovals.Clear();
                foreach (var (entity, cell) in loaded)
                {
                    if (cell == tile && removableEntities.Contains(entity))
                        entityRemovals.Add(entity);
                }

                foreach (var entity in entityRemovals)
                {
                    loaded.Remove(entity);
                    if (!Deleted(entity))
                        Del(entity);
                }

                if (loaded.Count == 0)
                    biome.LoadedEntities.Remove(chunk);
            }

            if (biome.LoadedDecals.TryGetValue(chunk, out var decals))
            {
                decalRemovals.Clear();
                foreach (var (decal, cell) in decals)
                {
                    if (cell == tile)
                        decalRemovals.Add(decal);
                }

                foreach (var decal in decalRemovals)
                {
                    decals.Remove(decal);
                    _decals.RemoveDecal(terrain, decal);
                }

                if (decals.Count == 0)
                    biome.LoadedDecals.Remove(chunk);
            }

            if (streaming != null && streaming.MarkerCells.TryGetValue(chunk, out var markers))
            {
                markers.Remove(tile);
                if (markers.Count == 0)
                    streaming.MarkerCells.Remove(chunk);
            }

            if (clearTiles)
                changes.Add((tile, Tile.Empty));
            else if (TryGetBiomeTile(tile, biome.Layers, biome.Seed, (terrain, grid), out var ground))
                changes.Add((tile, ground.Value));
        }

        _mapSystem.SetTiles(terrain, grid, changes);
    }
}
