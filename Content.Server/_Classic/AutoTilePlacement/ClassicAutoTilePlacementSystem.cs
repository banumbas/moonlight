/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Classic.AutoTilePlacement;

public sealed partial class ClassicAutoTilePlacementSystem : EntitySystem
{
    [Dependency] private readonly TileSystem _tile = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<ClassicAutoTilePlacementComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<ClassicAutoTilePlacementComponent> ent, ref ComponentInit args)
    {
        var xform = _xformQuery.GetComponent(ent);
        var coord = xform.Coordinates;
        var gridUid = xform.GridUid;
        var proto = MetaData(ent).EntityPrototype;

        if (gridUid is null || proto is null)
            return;

        if (!_gridQuery.TryComp(gridUid.Value, out var grid))
            return;

        var tileRef = _map.GetTileRef(gridUid.Value, grid, coord);

        if (!tileRef.Tile.IsEmpty)
            return;

        // Resolve the tile definition and replace the empty tile with the configured one.
        if (!_tileDef.TryGetDefinition(ent.Comp.Tile, out var def))
            return;

        _tile.ReplaceTile(tileRef, (ContentTileDefinition) def);

        // Recreate this entity
        Spawn(proto.ID, xform.Coordinates);
        QueueDel(ent);
    }
}
