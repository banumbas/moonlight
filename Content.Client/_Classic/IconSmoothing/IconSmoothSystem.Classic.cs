#pragma warning disable IDE0130 // Namespace does not match folder structure
using Robust.Client.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Enumerators;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;

namespace Content.Client.IconSmoothing;

public sealed partial class IconSmoothSystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<TransformComponent> _classicXformQuery;
    private EntityQuery<MapGridComponent> _classicGridQuery;
    private EntityQuery<IconSmoothComponent> _classicSmoothQuery;

    private void InitializeClassic()
    {
        _classicXformQuery = GetEntityQuery<TransformComponent>();
        _classicGridQuery = GetEntityQuery<MapGridComponent>();
        _classicSmoothQuery = GetEntityQuery<IconSmoothComponent>();
    }

    internal bool TryGetClassicUpperLayers(
        Entity<SpriteComponent> sprite,
        out int corner1,
        out int corner2)
    {
        var angle = _transform.GetWorldRotation(sprite.Owner) + _eyeManager.CurrentEye.Rotation;
        var rotation = angle.GetCardinalDir();
        CornerLayers c1, c2;

        switch (rotation)
        {
            case Direction.South:
                c1 = CornerLayers.NE;
                c2 = CornerLayers.NW;
                break;
            case Direction.East:
                c1 = CornerLayers.NE;
                c2 = CornerLayers.SE;
                break;
            case Direction.North:
                c1 = CornerLayers.SE;
                c2 = CornerLayers.SW;
                break;
            case Direction.West:
                c1 = CornerLayers.NW;
                c2 = CornerLayers.SW;
                break;
            default:
                c1 = CornerLayers.NE;
                c2 = CornerLayers.NW;
                break;
        }

        corner1 = 0;
        corner2 = 0;

        return _sprite.LayerMapTryGet(
                   sprite.AsNullable(),
                   c1,
                   out corner1,
                   false) &&
               _sprite.LayerMapTryGet(
                   sprite.AsNullable(),
                   c2,
                   out corner2,
                   false);
    }

    internal bool HasClassicNorthNeighbour(Entity<IconSmoothComponent> source)
    {
        if (!_classicXformQuery.TryComp(source, out var transform) ||
            !transform.Anchored ||
            transform.GridUid is not { } gridUid ||
            !_classicGridQuery.TryComp(gridUid, out var grid))
        {
            return false;
        }

        var position = _mapSystem.TileIndicesFor(
            gridUid,
            grid,
            transform.Coordinates);

        var gridAngle = _transform.GetWorldRotation(gridUid) + _eyeManager.CurrentEye.Rotation;
        var gridRotation = gridAngle.GetCardinalDir();
        Direction topDir;

        switch (gridRotation)
        {
            case Direction.South:
                topDir = Direction.North;
                break;
            case Direction.East:
                topDir = Direction.East;
                break;
            case Direction.North:
                topDir = Direction.South;
                break;
            case Direction.West:
                topDir = Direction.West;
                break;
            default:
                topDir = Direction.North;
                break;
        }

        return HasMatchingClassicNeighbour(
            source.Comp,
            _mapSystem.GetAnchoredEntitiesEnumerator(
                gridUid,
                grid,
                position.Offset(topDir)));
    }

    private bool HasMatchingClassicNeighbour(
        IconSmoothComponent source,
        AnchoredEntitiesEnumerator candidates)
    {
        while (candidates.MoveNext(out var candidate))
        {
            if (!_classicSmoothQuery.TryComp(candidate, out var other) ||
                other.SmoothKey == null ||
                !other.Enabled ||
                (other.SmoothKey != source.SmoothKey &&
                 !source.AdditionalKeys.Contains(other.SmoothKey)))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
