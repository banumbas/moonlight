// Namespace does not match folder structure
#pragma warning disable IDE0130
namespace Content.Shared.Maps;

public enum TileDigType : byte
{
    None = 0,
    Soft,
    Stone,
    Both,
}

public sealed partial class ContentTileDefinition
{
    /// <summary>
    /// Marks naturally generated terrain. Classic mining and structural systems use this
    /// independently from <see cref="Indestructible"/>, so geology can be excavated without
    /// being treated as ordinary player-built flooring.
    /// </summary>
    [DataField]
    public bool NaturalTerrain;

    /// <summary>
    /// Dig category for tools. Soft earth (grass, dirt, sand, snow) requires a shovel,
    /// Stone (rock, basalt, cobblestone) requires a pickaxe.
    /// </summary>
    [DataField]
    public TileDigType DigType = TileDigType.None;
}
