// Namespace does not match folder structure
#pragma warning disable IDE0130
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.Parallax.Biomes;

/// <summary>
/// Classic-specific biome extensions for conditional tile layers and optional distance masks.
/// </summary>
public abstract partial class SharedBiomeSystem
{
    /// <summary>
    /// Gets the underlying biome tile, including Classic conditional layers,
    /// while ignoring any existing tile that may be present on the destination grid.
    /// </summary>
    public bool TryGetTile(
        Vector2i indices,
        List<IBiomeLayer> layers,
        int seed,
        Entity<MapGridComponent>? grid,
        [NotNullWhen(true)] out Tile? tile)
    {
        return TryGetTileClassicInternal(indices,
            layers,
            seed,
            grid,
            layers.Count,
            out tile);
    }

    private bool TryGetBiomeTileClassicInternal(
        Vector2i indices,
        List<IBiomeLayer> layers,
        int seed,
        Entity<MapGridComponent>? grid,
        int layerCount,
        [NotNullWhen(true)] out Tile? tile)
    {
        if (grid is { } gridEnt &&
            _map.TryGetTileRef(gridEnt, gridEnt.Comp, indices, out var tileRef) &&
            !tileRef.Tile.IsEmpty)
        {
            tile = tileRef.Tile;
            return true;
        }

        return TryGetTileClassicInternal(indices, layers, seed, grid, layerCount, out tile);
    }

    private bool TryGetTileClassicInternal(
        Vector2i indices,
        List<IBiomeLayer> layers,
        int seed,
        Entity<MapGridComponent>? grid,
        int layerCount,
        [NotNullWhen(true)] out Tile? tile)
    {
        for (var i = layerCount - 1; i >= 0; i--)
        {
            var layer = layers[i];
            var noise = GetNoise(layer.Noise, seed);
            var value = noise.GetNoise(indices.X, indices.Y);
            value = layer.Invert ? -value : value;

            if (value < layer.Threshold)
                continue;

            if (layer is BiomeMetaLayer meta)
            {
                var metaLayers = ProtoManager.Index<BiomeTemplatePrototype>(meta.Template).Layers;
                if (TryGetBiomeTileClassicInternal(indices,
                        metaLayers,
                        seed,
                        grid,
                        metaLayers.Count,
                        out tile))
                {
                    return true;
                }

                continue;
            }

            if (layer is not BiomeTileLayer tileLayer)
                continue;

            var threshold = GetClassicThreshold(tileLayer, indices);
            if (threshold == null)
                continue;

            if (!IsClassicTileAllowed(indices, layers, i, seed, grid, tileLayer))
                continue;

            if (TryCreateClassicTile(indices,
                    noise,
                    threshold.Value,
                    ProtoManager.Index(tileLayer.Tile),
                    tileLayer.Variants,
                    value,
                    out tile))
            {
                return true;
            }
        }

        tile = null;
        return false;
    }

    private bool TryCreateClassicTile(
        Vector2i indices,
        Robust.Shared.Noise.FastNoiseLite noise,
        float threshold,
        Content.Shared.Maps.ContentTileDefinition tileDef,
        List<byte>? variants,
        float found,
        [NotNullWhen(true)] out Tile? tile)
    {
        if (found < threshold)
        {
            tile = null;
            return false;
        }

        byte variant = 0;
        var variantCount = variants?.Count ?? tileDef.Variants;
        if (variantCount > 1)
        {
            var variantValue = (noise.GetNoise(indices.X * 8, indices.Y * 8, variantCount) + 1f) * 100;
            variant = _tile.PickVariant(tileDef, (int) variantValue);
        }

        tile = new Tile(tileDef.TileId, variant);
        return true;
    }

    private static float? GetClassicThreshold(BiomeTileLayer layer, Vector2i sampleIndices)
    {
        var threshold = layer.Threshold;
        if (layer.MinDistance <= 0f)
            return threshold;

        var fadeStart = layer.MinDistance;
        var fadeEnd = MathF.Max(0f, fadeStart - 100f);
        var distanceSquared = (float) sampleIndices.X * sampleIndices.X +
            (float) sampleIndices.Y * sampleIndices.Y;

        if (distanceSquared < fadeEnd * fadeEnd)
            return null;

        if (distanceSquared < fadeStart * fadeStart)
        {
            // Sqrt is only needed inside the narrow fade band. Almost every streamed cave tile is
            // outside it, so the common generation path stays multiplication-only.
            var distance = MathF.Sqrt(distanceSquared);
            // Make caves disappear gradually toward the colony instead of at a hard circle.
            var progress = 1f - ((distance - fadeEnd) / (fadeStart - fadeEnd));
            threshold += progress * 2.5f;
        }

        return threshold;
    }

    private bool IsClassicTileAllowed(
        Vector2i indices,
        List<IBiomeLayer> layers,
        int layerIndex,
        int seed,
        Entity<MapGridComponent>? grid,
        BiomeTileLayer layer)
    {
        if (layer.AllowedTiles is not { Count: > 0 })
            return true;

        if (!TryGetBiomeTileClassicInternal(indices,
                layers,
                seed,
                grid,
                layerIndex,
                out var underlyingTile))
            return false;

        var tileId = TileDefManager[underlyingTile.Value.TypeId].ID;
        foreach (var allowedTile in layer.AllowedTiles)
        {
            if (allowedTile.Id == tileId)
                return true;
        }

        return false;
    }
}
