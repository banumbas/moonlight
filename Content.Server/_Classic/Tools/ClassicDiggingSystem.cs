using Content.Server._Classic.ZLevels.Core;
using Content.Server.Destructible;
using Content.Server.Gatherable.Components;
using Content.Shared.Tag;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Classic.Tools;

public sealed partial class ClassicDiggingSystem : EntitySystem
{
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private ClassicZLevelsSystem _zLevels = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClassicDiggingTileDeconstructedEvent>(OnDiggingTileDeconstructed);
    }

    private void OnDiggingTileDeconstructed(ClassicDiggingTileDeconstructedEvent args)
    {
        if (!TryComp<MapGridComponent>(args.GridUid, out var sourceGrid) ||
            Transform(args.GridUid).MapUid is not { } sourceMap ||
            !_zLevels.TryMapDown(sourceMap, out var mapBelow))
        {
            return;
        }

        var worldPosition = _map.GridTileToWorldPos(args.GridUid, sourceGrid, args.GridIndices);
        if (!_map.TryFindGridAt(mapBelow, worldPosition, out var gridUid, out var grid))
            return;

        var gridIndices = _map.WorldToTile(gridUid, grid, worldPosition);
        var anchored = _map.GetAnchoredEntities(gridUid, grid, gridIndices);
        List<EntityUid>? walls = null;
        while (anchored.MoveNext(out var entity))
        {
            if (!HasComp<GatherableComponent>(entity.Value) || !_tag.HasTag(entity.Value, WallTag))
                continue;

            walls ??= new List<EntityUid>();
            walls.Add(entity.Value);
        }

        if (walls == null)
            return;

        foreach (var wall in walls)
        {
            _destructible.DestroyEntity(wall);
        }
    }
}
