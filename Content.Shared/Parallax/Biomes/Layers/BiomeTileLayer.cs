using Content.Shared.Maps;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Parallax.Biomes.Layers;

[Serializable, NetSerializable]
public sealed partial class BiomeTileLayer : IBiomeLayer
{
    [DataField] public FastNoiseLite Noise { get; private set; } = new(0);

    /// <inheritdoc/>
    [DataField]
    public float Threshold { get; private set; } = 0.5f;

    /// <inheritdoc/>
    [DataField] public bool Invert { get; private set; } = false;

    /// <summary>
    /// Which tile variants to use for this layer. Uses all of the tile's variants if none specified
    /// </summary>
    [DataField]
    public List<byte>? Variants = null;

    /// <summary>
    /// Which tiles this layer is allowed to override. If specified, it will evaluate lower layers to ensure they yield one of these tiles before spawning.
    /// </summary>
    [DataField("allowedTiles")]
    public List<ProtoId<ContentTileDefinition>>? AllowedTiles;

    // Moonlight-Start
    /// <summary>
    /// If greater than 0, this tile layer will not spawn within this distance from the grid origin (0,0).
    /// </summary>
    [DataField]
    public float MinDistance = 0f;
    // Moonlight-End

    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> Tile = string.Empty;
}
