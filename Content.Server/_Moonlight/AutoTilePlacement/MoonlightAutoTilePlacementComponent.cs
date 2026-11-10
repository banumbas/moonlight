/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server._Moonlight.AutoTilePlacement;

/// <summary>
/// Automatically place tile under spawned entity, if this entity was spawned from PlacementManager
/// </summary>
[RegisterComponent]
public sealed partial class MoonlightAutoTilePlacementComponent : Component
{
    [DataField]
    public ProtoId<ContentTileDefinition> Tile = "FloorSteel";
}
