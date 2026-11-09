// Namespace does not match folder structure
#pragma warning disable IDE0130
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared.Burial.Components;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Content.Shared.Tools;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Tools.Systems;

public abstract partial class SharedToolSystem
{
    [Dependency] private readonly TagSystem _tag = default!;

    [ValidatePrototypeId<ToolQualityPrototype>]
    private static readonly ProtoId<ToolQualityPrototype> DiggingQuality = "Digging";

    [ValidatePrototypeId<TagPrototype>]
    private static readonly ProtoId<TagPrototype> PickaxeTag = "Pickaxe";

    private const int LowestDiggableZLevel = -2;

    /// <summary>
    /// Checks whether the given tool can dig the specified tile.
    /// Pickaxes can only dig stone/basalt/rock floor, not grass or soft soil.
    /// Shovels can only dig grass, dirt, sand, and soil, not solid rock or stone floor.
    /// Also enforces bedrock Z-level limits so open space cannot be excavated on the lowest level.
    /// </summary>
    private bool CanDigClassicTile(
        TileRef tileRef,
        PrototypeFlags<ToolQualityPrototype> toolQualities,
        EntityUid? toolUid = null,
        EntityUid? user = null)
    {
        if (!toolQualities.Contains(DiggingQuality))
            return true;

        var tileDef = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];

        if (toolUid is { } tool)
        {
            var isPickaxe = _tag.HasTag(tool, PickaxeTag);
            var isShovel = HasComp<ShovelComponent>(tool);

            if (isPickaxe && !isShovel && tileDef.DigType == TileDigType.Soft)
            {
                if (user != null)
                    _popup.PopupClient(Loc.GetString("classic-digging-pickaxe-cannot-dig-grass"), user.Value, user.Value);
                return false;
            }

            if (!isPickaxe && tileDef.DigType == TileDigType.Stone)
            {
                if (user != null)
                    _popup.PopupClient(Loc.GetString("classic-digging-cannot-dig-rock"), user.Value, user.Value);
                return false;
            }
        }

        if (tileDef.NaturalTerrain && !string.IsNullOrWhiteSpace(tileDef.BaseTurf))
        {
            var baseTurf = (ContentTileDefinition) _tileDefManager[tileDef.BaseTurf];
            if (baseTurf.TileId == 0 || baseTurf.MapAtmosphere)
            {
                var mapUid = Transform(tileRef.GridUid).MapUid;
                if (mapUid != null &&
                    TryComp<ClassicZMapComponent>(mapUid.Value, out var zMap) &&
                    zMap.Depth <= LowestDiggableZLevel)
                {
                    if (user != null)
                        _popup.PopupClient(Loc.GetString("classic-digging-bedrock-cannot-dig"), user.Value, user.Value);
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Raised on the server when a tile is dug down to empty space (open space).
/// Handled by ClassicDiggingSystem to clear rock on the Z-level below.
/// </summary>
public readonly record struct ClassicDiggingTileDeconstructedEvent(EntityUid GridUid, Vector2i GridIndices);
