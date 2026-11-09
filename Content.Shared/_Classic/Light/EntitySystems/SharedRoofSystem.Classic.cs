// Namespace does not match folder structure
#pragma warning disable IDE0130
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.Light.EntitySystems;

public abstract partial class SharedRoofSystem
{
    /// <summary>
    /// Checks a conservative local area once before drawing a batch of tiles. If it contains no
    /// enabled roof entities, GetColor can use only the tile masks throughout that area.
    /// </summary>
    public bool HasRoofEntities(EntityUid gridUid, Box2 localBounds)
    {
        _roofSet.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, localBounds, _roofSet);
        foreach (var entity in _roofSet)
        {
            if (entity.Comp.Enabled)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Applies multiple roof-bit changes while dirtying the networked component at most once.
    /// This is intended for streamed/generated tile batches.
    /// </summary>
    public void SetRoofs(
        Entity<MapGridComponent?, RoofComponent?> grid,
        IReadOnlyList<(Vector2i Index, bool Value)> changes)
    {
        if (changes.Count == 0 || !Resolve(grid, ref grid.Comp1, ref grid.Comp2, false))
            return;

        var roof = grid.Comp2;
        var changed = false;

        foreach (var (index, value) in changes)
        {
            var chunkOrigin = SharedMapSystem.GetChunkIndices(index, RoofComponent.ChunkSize);
            roof.Data.TryGetValue(chunkOrigin, out var chunkData);

            var chunkRelative = SharedMapSystem.GetChunkRelative(index, RoofComponent.ChunkSize);
            var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * RoofComponent.ChunkSize);
            var newChunkData = value ? chunkData | bitFlag : chunkData & ~bitFlag;

            if (newChunkData == chunkData)
                continue;

            if (newChunkData == 0)
                roof.Data.Remove(chunkOrigin);
            else
                roof.Data[chunkOrigin] = newChunkData;

            changed = true;
        }

        if (changed)
            Dirty(grid.Owner, roof);
    }
}
