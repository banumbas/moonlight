using System.Linq;
using System.Numerics;
using Content.Server._Moonlight.GameTicking.Rules.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Moonlight.GameTicking.Rules;

public sealed partial class MoonlightMergeMapRuleSystem
{
    private bool TryFindPlacement(
        MoonlightMergeMapRuleComponent rule,
        Box2 stationBounds,
        List<LoadedLevel> levels,
        Entity<MapComponent>? ground,
        out Vector2 offset,
        out Dictionary<EntityUid, TerrainReservation> reservations)
    {
        offset = default;
        reservations = new();
        if (!float.IsFinite(rule.MinDistance) || !float.IsFinite(rule.MaxDistance) ||
            !float.IsFinite(rule.PlacementPadding) || rule.MinDistance < 0 || rule.MaxDistance < rule.MinDistance ||
            rule.PlacementPadding < 0 || rule.PlacementAttempts <= 0)
        {
            return false;
        }

        // Anchor the translation to the base grid rather than the union's centre. Roof grids may
        // have fractional offsets, which must survive placement without shifting base floors off tiles.
        var origin = _transform.GetWorldPosition(levels[0].Grids[0]);
        var structureRadius = 0f;
        foreach (var level in levels)
        {
            var bounds = GetLevelBounds(level);
            var extent = Vector2.Max(Vector2.Abs(bounds.BottomLeft - origin), Vector2.Abs(bounds.TopRight - origin));
            structureRadius = MathF.Max(structureRadius, extent.Length());
        }

        var distance = stationBounds.Size.Length() / 2f + structureRadius + rule.PlacementPadding;
        var attempts = rule.RandomPlacement ? rule.PlacementAttempts : 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (rule.RandomPlacement)
            {
                var position = stationBounds.Center + _random.NextVector2(distance + rule.MinDistance, distance + rule.MaxDistance);
                offset = new Vector2(MathF.Round(position.X), MathF.Round(position.Y)) - origin;
            }

            reservations = BuildTerrainReservations(levels, ground, offset);
            var canReserve = true;
            foreach (var reservation in reservations.Values)
            {
                var footprint = new HashSet<Vector2i>(reservation.ClearTiles);
                footprint.UnionWith(reservation.FillTiles);
                if (!_biome.CanReserveStructureTiles(reservation.Terrain, footprint, reservation.RemovableEntities))
                {
                    canReserve = false;
                    break;
                }
            }

            if (canReserve && IsPlacementFree(levels, offset, rule.PlacementPadding, reservations))
                return true;
        }

        return false;
    }

    private Dictionary<EntityUid, TerrainReservation> BuildTerrainReservations(
        List<LoadedLevel> levels,
        Entity<MapComponent>? ground,
        Vector2 offset)
    {
        var reservations = new Dictionary<EntityUid, TerrainReservation>();
        foreach (var level in levels)
        {
            if (!TryGetTerrain(level.Target, out var terrain))
                continue;

            var reservation = new TerrainReservation(terrain);
            reservations.Add(terrain, reservation);
            AddFootprint(level.Grids, terrain, offset, reservation.ClearTiles);
        }

        if (ground is { } groundMap && TryGetTerrain(groundMap, out var groundTerrain))
        {
            if (!reservations.TryGetValue(groundTerrain, out var reservation))
            {
                reservation = new TerrainReservation(groundTerrain);
                reservations.Add(groundTerrain, reservation);
            }

            foreach (var level in levels)
                AddFootprint(level.Grids, groundTerrain, offset, reservation.FillTiles);
            reservation.FillTiles.ExceptWith(reservation.ClearTiles);
        }

        return reservations;
    }

    private void AddFootprint(
        List<Entity<MapGridComponent>> sourceGrids,
        Entity<BiomeComponent, MapGridComponent> terrain,
        Vector2 offset,
        HashSet<Vector2i> footprint)
    {
        var terrainInverse = _transform.GetInvWorldMatrix(terrain);
        foreach (var source in sourceGrids)
        {
            var sourceMatrix = _transform.GetWorldMatrix(source);
            var tileSize = source.Comp.TileSize;
            var halfSize = new Vector2(tileSize / 2f);
            var relativeRotation = _transform.GetWorldRotation(source) - _transform.GetWorldRotation(terrain);
            var axis = relativeRotation.RotateVec(Vector2.UnitX);
            var terrainHalfSize = terrain.Comp2.TileSize / 2f;
            var projectedHalfSize = tileSize / 2f + terrainHalfSize * (MathF.Abs(axis.X) + MathF.Abs(axis.Y));
            foreach (var tile in _map.GetAllTiles(source, source.Comp))
            {
                var min = (Vector2) tile.GridIndices * tileSize;
                var centre = Vector2.Transform(Vector2.Transform(min + halfSize, sourceMatrix) + offset, terrainInverse);
                var area = new Box2Rotated(new Box2(centre - halfSize, centre + halfSize), relativeRotation, centre).CalcBoundingBox();
                foreach (var target in _map.GetLocalTilesIntersecting(terrain, terrain.Comp2, area, false))
                {
                    // The broad box tests terrain axes; these two source axes complete the SAT test.
                    // This avoids reserving neighbouring cells outside a rotated saved floor tile.
                    var targetCentre = (Vector2) target.GridIndices * terrain.Comp2.TileSize + new Vector2(terrainHalfSize);
                    var delta = (-relativeRotation).RotateVec(targetCentre - centre);
                    if (MathF.Abs(delta.X) < projectedHalfSize && MathF.Abs(delta.Y) < projectedHalfSize)
                        footprint.Add(target.GridIndices);
                }
            }
        }
    }

    private bool IsPlacementFree(
        List<LoadedLevel> levels,
        Vector2 offset,
        float padding,
        Dictionary<EntityUid, TerrainReservation> reservations)
    {
        var entities = new HashSet<EntityUid>();
        foreach (var level in levels)
        {
            var bounds = GetLevelBounds(level).Translated(offset);
            foreach (var existing in _map.GetAllGrids(level.Target.Comp.MapId))
            {
                if (HasComp<BiomeComponent>(existing))
                    continue;

                var existingBounds = _transform.GetWorldMatrix(existing).TransformBox(existing.Comp.LocalAABB);
                if (bounds.Enlarged(padding).Intersects(existingBounds))
                    return false;
            }

            entities.Clear();
            _lookup.GetEntitiesIntersecting(level.Target.Comp.MapId, bounds, entities, LookupFlags.Uncontained);
            foreach (var entity in entities)
            {
                if (HasComp<MapGridComponent>(entity) || HasComp<MapComponent>(entity))
                    continue;

                var xform = Transform(entity);
                if (reservations.TryGetValue(xform.ParentUid, out var reservation))
                {
                    if (reservation.RemovableEntities.Contains(entity))
                        continue;

                    // Untouched natural objects in holes between the prefab's tiles remain in place.
                    // Other objects, including loose items and player construction, block placement.
                    var tile = _map.LocalToTile(reservation.Terrain, reservation.Terrain.Comp2, xform.Coordinates);
                    if (!reservation.ClearTiles.Contains(tile) && !reservation.FillTiles.Contains(tile))
                    {
                        var biome = reservation.Terrain.Comp1;
                        if (biome.LoadedEntities.Values.Any(loaded => loaded.ContainsKey(entity)))
                            continue;
                    }
                }

                return false;
            }
        }
        return true;
    }

    private bool TryGetTerrain(Entity<MapComponent> map, out Entity<BiomeComponent, MapGridComponent> terrain)
    {
        foreach (var grid in _map.GetAllGrids(map.Comp.MapId))
        {
            if (!TryComp<BiomeComponent>(grid, out var biome))
                continue;
            terrain = (grid, biome, grid.Comp);
            return true;
        }
        terrain = default;
        return false;
    }

    private Box2 GetLevelBounds(LoadedLevel level)
    {
        Box2? bounds = null;
        foreach (var grid in level.Grids)
        {
            var world = _transform.GetWorldMatrix(grid).TransformBox(grid.Comp.LocalAABB);
            bounds = bounds?.Union(world) ?? world;
        }
        return bounds!.Value;
    }

    private sealed class TerrainReservation(Entity<BiomeComponent, MapGridComponent> terrain)
    {
        public readonly Entity<BiomeComponent, MapGridComponent> Terrain = terrain;
        public readonly HashSet<Vector2i> ClearTiles = new();
        public readonly HashSet<Vector2i> FillTiles = new();
        public readonly HashSet<EntityUid> RemovableEntities = new();
    }
}
