/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared.Maps;
using Content.Shared.Toggleable;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared._Moonlight.ZLevels.Core.EntitySystems;

public abstract partial class MoonlightSharedZLevelsSystem
{
    [Dependency] protected ITileDefinitionManager TilDefMan = null!;
    [Dependency] private EntityQuery<EyeComponent> _viewerEyeQuery = default!;

    private void InitializeView()
    {
        SubscribeLocalEvent<MoonlightZLevelViewerComponent, ToggleActionEvent>(OnToggleLookUp);
    }

    private void OnToggleLookUp(Entity<MoonlightZLevelViewerComponent> entity, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        if (args.Action.Owner != entity.Comp.ActionEntity)
            return;

        args.Handled = true;

        entity.Comp.LookUp = !entity.Comp.LookUp;
        DirtyField(entity, entity.Comp, nameof(MoonlightZLevelViewerComponent.LookUp));

        _actions.SetToggled(entity.Comp.ActionEntity, entity.Comp.LookUp);
        LookUpChanged(entity);
    }

    /// <summary>Server hook for updating PVS subscriptions when upward rendering is toggled.</summary>
    protected virtual void LookUpChanged(Entity<MoonlightZLevelViewerComponent> entity)
    {
    }

    /// <summary>
    /// Calculates how many z-levels above the entity's current position are visible (i.e. not blocked by an opaque tile),
    /// up to <see cref="MaxZLevelsAboveRendering"/>.
    /// </summary>
    public int GetVisibleZLevelsAbove(EntityUid ent, Entity<MoonlightZMapComponent?>? currentMapUid = null)
    {
        if (!TryComp<MoonlightZLevelViewerComponent>(ent, out var viewer))
            return 0;

        var (viewMap, worldPos) = GetViewedPosition(ent);
        currentMapUid ??= viewMap;

        if (currentMapUid is null)
            return 0;

        var tile = new Vector2i(
            (int) MathF.Floor(worldPos.X),
            (int) MathF.Floor(worldPos.Y));
        if (viewer.VisibleZLevelsAboveMap != currentMapUid.Value.Owner ||
            viewer.VisibleZLevelsAboveTile != tile)
        {
            return 0;
        }

        var maxVisibleLevels = Math.Min(viewer.VisibleZLevelsAbove, MaxZLevelsAboveRendering);
        if (maxVisibleLevels == 0)
            return 0;

        if (!TryMapUp(currentMapUid.Value, out var checkingMap))
            return 0;

        var visibleLevels = 0;

        for (var i = 1; i <= maxVisibleLevels; i++)
        {
            // No grid or no chunk at this position means there's simply nothing there (open space),
            // not an opaque roof, so it shouldn't block sight upward. Mirrors HasOpaqueAbove below.
            if (_map.TryFindGridAt(checkingMap, worldPos, out var gridUid, out var grid) &&
                _map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef))
            {
                var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Tile.TypeId];
                if (!tileDef.Transparent)
                    break;
            }

            visibleLevels++;

            if (i == maxVisibleLevels || !TryMapUp(checkingMap.AsNullable(), out checkingMap))
                break;
        }

        return visibleLevels;
    }

    /// <summary>
    /// Resolves the eye target, including remote views on maps outside the Z network.
    /// Rendering and auxiliary PVS eyes must use the same map.
    /// </summary>
    public TransformComponent GetViewedTransform(EntityUid viewer, TransformComponent? fallback = null)
    {
        if (_viewerEyeQuery.TryComp(viewer, out var eye) &&
            eye.Target is { } target &&
            _transformQuery.TryComp(target, out var targetXform))
        {
            return targetXform;
        }

        return fallback ?? Transform(viewer);
    }

    /// <summary>
    /// Resolves the spatial view position for roof checks and auxiliary eyes. Camera projection and
    /// screen effects are excluded; remote views can supply their own horizontal offset.
    /// </summary>
    public (EntityUid? Map, Vector2 Position) GetViewedPosition(EntityUid viewer, TransformComponent? fallback = null)
    {
        var xform = GetViewedTransform(viewer, fallback);
        var ev = new MoonlightGetViewPositionEvent(xform.Owner, _transform.GetWorldPosition(xform));
        RaiseLocalEvent(viewer, ref ev);
        return (xform.MapUid, ev.Position);
    }

    /// <summary>
    /// Checks whether any grid on the map above has an opaque (non-transparent) tile at the given world position.
    /// </summary>
    [PublicAPI]
    public bool HasOpaqueAbove(Vector2 worldPos, Entity<MoonlightZMapComponent?> currentMap)
    {
        if (!TryMapUp(currentMap, out var mapAboveUid))
            return false;

        if (!_map.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        if (!_map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef))
            return false;

        return !((ContentTileDefinition)TilDefMan[tileRef.Tile.TypeId]).Transparent;
    }

    public bool TryFindZShotOpening(
        EntityUid sourceMap,
        EntityUid targetMap,
        int offset,
        Vector2 from,
        Vector2 to,
        out Vector2 opening,
        bool preferOpeningAwayFromSource = false,
        float maxSourceDistanceFromOpeningEdgeTiles = float.PositiveInfinity)
    {
        opening = default;
        if (offset == 0)
            return false;

        var openingMap = offset < 0 ? sourceMap : targetMap;
        if (!_gridQuery.TryComp(openingMap, out var grid))
            return false;

        var sourceTile = preferOpeningAwayFromSource
            ? _map.WorldToTile(openingMap, grid, from)
            : default;

        var fallbackOpening = Vector2.Zero;
        var hasFallbackOpening = false;

        var maxSourceDistanceFromOpeningCenter = float.IsPositiveInfinity(maxSourceDistanceFromOpeningEdgeTiles)
            ? float.PositiveInfinity
            : grid.TileSize * (0.5f + Math.Max(0f, maxSourceDistanceFromOpeningEdgeTiles));

        var maxSourceDistanceSquared = maxSourceDistanceFromOpeningCenter * maxSourceDistanceFromOpeningCenter;
        var selectedOpening = Vector2.Zero;

        var localFrom = _map.WorldToLocal(openingMap, grid, from) / grid.TileSize;
        var localTo = _map.WorldToLocal(openingMap, grid, to) / grid.TileSize;

        var localDelta = localTo - localFrom;

        var currentTile = new Vector2i((int) MathF.Floor(localFrom.X), (int) MathF.Floor(localFrom.Y));
        var endTile = new Vector2i((int) MathF.Floor(localTo.X), (int) MathF.Floor(localTo.Y));

        var stepX = Math.Sign(localDelta.X);
        var stepY = Math.Sign(localDelta.Y);

        var tDeltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / localDelta.X);
        var tDeltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / localDelta.Y);

        var nextBoundaryX = stepX > 0 ? currentTile.X + 1f : currentTile.X;
        var nextBoundaryY = stepY > 0 ? currentTile.Y + 1f : currentTile.Y;

        var tMaxX = stepX == 0 ? float.PositiveInfinity : (nextBoundaryX - localFrom.X) / localDelta.X;
        var tMaxY = stepY == 0 ? float.PositiveInfinity : (nextBoundaryY - localFrom.Y) / localDelta.Y;

        while (true)
        {
            if (TryUseOpeningTile(currentTile))
            {
                opening = selectedOpening;
                return true;
            }

            if (currentTile == endTile)
                break;

            if (tMaxX < tMaxY)
            {
                currentTile += new Vector2i(stepX, 0);
                tMaxX += tDeltaX;
                continue;
            }

            if (tMaxY < tMaxX)
            {
                currentTile += new Vector2i(0, stepY);
                tMaxY += tDeltaY;
                continue;
            }

            currentTile += new Vector2i(stepX, stepY);
            tMaxX += tDeltaX;
            tMaxY += tDeltaY;
        }

        if (!hasFallbackOpening)
            return false;

        opening = fallbackOpening;
        return true;

        bool TryUseOpeningTile(Vector2i tile)
        {
            if (_map.TryGetTileRef(openingMap, grid, tile, out var tileRef) && !MoonlightZLevelOpeningCache.IsOpeningTile(tileRef.Tile, TilDefMan))
                return false;

            var openingCenter = _map.ToCenterCoordinates(openingMap, tile, grid).Position;
            if (Vector2.DistanceSquared(from, openingCenter) > maxSourceDistanceSquared)
                return false;

            if (preferOpeningAwayFromSource && tile == sourceTile)
            {
                if (hasFallbackOpening)
                    return false;

                fallbackOpening = openingCenter;
                hasFallbackOpening = true;

                return false;
            }

            selectedOpening = openingCenter;
            return true;
        }
    }
}

/// <summary>Allows a remote view to offset the position used for spatial visibility checks.</summary>
[ByRefEvent]
public record struct MoonlightGetViewPositionEvent(EntityUid Target, Vector2 Position);
