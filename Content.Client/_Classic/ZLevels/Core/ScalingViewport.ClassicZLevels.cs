/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Client._Classic.ZLevels.Core;
using Content.Shared.CCVar;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ITileDefinitionManager _tile = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    private ClassicClientZLevelsSystem? _zLevels;
    private SharedMapSystem? _mapSystem;
    private SharedTransformSystem? _zTransform;
    private IClydeViewport? _lowerViewport;
    private UIBox2 _lowerVisiblePixels;

    [ViewVariables] public Vector2i LowerLevelRenderSize { get; private set; }

    private EntityQuery<TransformComponent>? _xformQuery;
    private EntityQuery<MapComponent>? _mapQuery;

    private IEye? _fallbackEye;

    /// <summary>
    /// We are looking for at least one empty tile on the screen.
    /// This is used to ensure that it makes sense to draw the z-planes and that they are visible.
    /// </summary>
    public bool TryFindEmptyTiles(EntityUid mapUid, IClydeViewport viewport, bool captureBounds = false)
    {
        if (_xformQuery is null || !_xformQuery.Value.TryComp(mapUid, out var xform))
            return true;

        var mapId = xform.MapID;

        // IClydeViewport.LocalToWorld expects viewport-local pixels. The old code fed
        // control-local pixels into IEyeManager.ScreenToMap, whose contract requires
        // absolute screen coordinates, which could create false opening detections.
        var size = viewport.Size;
        var corner0 = viewport.LocalToWorld(Vector2.Zero).Position;
        var corner1 = viewport.LocalToWorld(new Vector2(size.X, 0)).Position;
        var corner2 = viewport.LocalToWorld(new Vector2(0, size.Y)).Position;
        var corner3 = viewport.LocalToWorld(size).Position;

        var minX = MathF.Min(MathF.Min(corner0.X, corner1.X), MathF.Min(corner2.X, corner3.X));
        var minY = MathF.Min(MathF.Min(corner0.Y, corner1.Y), MathF.Min(corner2.Y, corner3.Y));

        if (_mapSystem is null)
            return true;

        if (!_mapSystem.TryFindGridAt(mapUid, new Vector2(minX, minY), out var gridUid, out var grid))
        {
            // A corner can itself be an empty tile. Find the grid around the hole instead
            // of making that small opening trigger a full-screen lower-level pass.
            var maxX = MathF.Max(MathF.Max(corner0.X, corner1.X), MathF.Max(corner2.X, corner3.X));
            var maxY = MathF.Max(MathF.Max(corner0.Y, corner1.Y), MathF.Max(corner2.Y, corner3.Y));
            var state = (Uid: gridUid, Grid: grid, Area: 0f);
            _mapSystem.FindGridsIntersecting(mapUid, new Box2(minX, minY, maxX, maxY), ref state,
                static (EntityUid uid, MapGridComponent candidate, ref (EntityUid Uid, MapGridComponent? Grid, float Area) found) =>
                {
                    var area = candidate.LocalAABB.Width * candidate.LocalAABB.Height;
                    if (found.Grid == null || area > found.Area)
                        found = (uid, candidate, area);
                    return true;
                }, approx: true);
            if (state.Grid == null)
                return true;

            gridUid = state.Uid;
            grid = state.Grid;
        }

        // All four corners are required for a rotated grid/camera.
        var tile0 = _mapSystem.TileIndicesFor(gridUid, grid, new MapCoordinates(corner0, mapId));
        var tile1 = _mapSystem.TileIndicesFor(gridUid, grid, new MapCoordinates(corner1, mapId));
        var tile2 = _mapSystem.TileIndicesFor(gridUid, grid, new MapCoordinates(corner2, mapId));
        var tile3 = _mapSystem.TileIndicesFor(gridUid, grid, new MapCoordinates(corner3, mapId));
        var tileBottomLeft = Vector2i.ComponentMin(Vector2i.ComponentMin(tile0, tile1), Vector2i.ComponentMin(tile2, tile3));
        var tileTopRight = Vector2i.ComponentMax(Vector2i.ComponentMax(tile0, tile1), Vector2i.ComponentMax(tile2, tile3));

        // Tile events invalidate only the affected chunks in the system-owned cache. A viewport
        // cache without those events discarded the entire map whenever terrain streamed in.
        _zLevels ??= _entityManager.System<ClassicClientZLevelsSystem>();
        if (captureBounds)
        {
            if (!_zLevels.OpeningCache.TryGetOpeningBounds((gridUid, grid),
                    tileBottomLeft - Vector2i.One, tileTopRight + Vector2i.One, _mapSystem, _tile, out var localBounds))
                return false;

            _zTransform ??= _entityManager.System<SharedTransformSystem>();
            var matrix = _zTransform.GetWorldMatrix(gridUid) * viewport.GetWorldToLocalMatrix();
            var pixels = matrix.TransformBox(localBounds);
            _lowerVisiblePixels = new UIBox2(pixels.Left, pixels.Bottom, pixels.Right, pixels.Top);
            return true;
        }

        return _zLevels.OpeningCache.HasOpeningInTileBounds(
            (gridUid, grid),
            tileBottomLeft - Vector2i.One,
            tileTopRight + Vector2i.One,
            _mapSystem,
            _tile);
    }

    private void RenderZLevels(IClydeViewport viewport, IRenderHandle handle)
    {
        LowerLevelRenderSize = Vector2i.Zero;
        if (_eye is null)
            return;

        _fallbackEye = _eye;
        var fallbackClearColor = viewport.ClearColor;

        // Auxiliary camera/admin viewports have their own eye. The Z renderer is
        // player-map based, so running it there both renders the wrong map and repeats
        // all world passes unnecessarily.
        if (!ReferenceEquals(_eyeManager.MainViewport, this))
        {
            viewport.Render();
            return;
        }

        // Cache frequently accessed components/systems
        _xformQuery ??= _entityManager.GetEntityQuery<TransformComponent>();
        _mapQuery ??= _entityManager.GetEntityQuery<MapComponent>();

        // Cache systems and components
        _zLevels ??= _entityManager.System<ClassicClientZLevelsSystem>();
        _mapSystem ??= _entityManager.System<SharedMapSystem>();

        if (_player.LocalEntity is null)
        {
            viewport.Render();
            return;
        }

        if (!_entityManager.TryGetComponent<ClassicZLevelViewerComponent>(_player.LocalEntity.Value, out var zLevelViewer))
        {
            viewport.Render();
            return;
        }

        if (!_xformQuery.Value.TryComp(_player.LocalEntity, out var playerXform))
        {
            viewport.Render();
            return;
        }

        playerXform = _zLevels.GetViewedTransform(_player.LocalEntity.Value, playerXform);
        if (playerXform.MapUid is null)
        {
            viewport.Render();
            return;
        }

        var zMapUid = playerXform.MapUid.Value;
        var lookUp = 0;
        if (zLevelViewer.LookUp)
            lookUp = _zLevels.GetVisibleZLevelsAbove(_player.LocalEntity.Value, zMapUid);

        var maxLevelsBelow = Math.Clamp(
            _configuration.GetCVar(CCVars.ClassicZLevelsRenderingMaxZLevelsBelowRendering),
            0,
            ClassicSharedZLevelsSystem.MaxZLevelsBelowRendering);

        var lowestDepth = 0;
        var cropLower = _configuration.GetCVar(CCVars.ClassicZLevelsRenderingCropLowerLevels);
        _lowerVisiblePixels = UIBox2.FromDimensions(Vector2.Zero, viewport.Size);
        for (var i = 0; i >= -maxLevelsBelow; i--)
        {
            var checkingMap = zMapUid;

            if (i != 0)
            {
                if (!_zLevels.TryMapOffset(zMapUid, i, out var mapUidBelow))
                    continue;

                checkingMap = mapUidBelow;
            }

            lowestDepth = i;

            // The opening state of the deepest permitted level cannot reveal another
            // level, so scanning its tiles cannot affect this frame's render range.
            if (i == -maxLevelsBelow)
                break;

            if (!TryFindEmptyTiles(checkingMap, viewport, captureBounds: cropLower && i == 0))
                break;
        }

        var lowerBounds = UIBox2i.FromDimensions(Vector2i.Zero, viewport.Size);
        var lowerViewport = viewport;
        var lowerCameraOffset = Vector2.Zero;
        if (lowestDepth < 0 && cropLower)
        {
            // Keep a guard band for edge sprites and lighting/blur kernels. Pixel density stays
            // identical to the main view; only work hidden behind the current floor is omitted.
            var padding = viewport.RenderScale * EyeManager.PixelsPerMeter * 3f;
            lowerBounds = ClassicZLevelRenderRegion.Cover(_lowerVisiblePixels, viewport.Size,
                _lowerViewport?.Size ?? Vector2i.Zero, padding);
            if (lowerBounds.Size != viewport.Size)
            {
                if (_lowerViewport?.Size != lowerBounds.Size)
                {
                    _lowerViewport?.Dispose();
                    _lowerViewport = _clyde.CreateViewport(lowerBounds.Size,
                        new TextureSampleParameters { Filter = false }, name: "classic-lower-z");
                }

                lowerViewport = _lowerViewport;
                lowerViewport.RenderScale = viewport.RenderScale;
                lowerCameraOffset = viewport.LocalToWorld(lowerBounds.Center).Position -
                                    viewport.LocalToWorld((Vector2) viewport.Size / 2f).Position;
            }
        }

        if (lowestDepth < 0)
            LowerLevelRenderSize = lowerViewport.Size;

        var configuredLightBlur = _configuration.GetCVar(CVars.LightBlur);
        var suppressLowerLightBlur = configuredLightBlur &&
                                     !_configuration.GetCVar(CCVars.ClassicZLevelsRenderingLowerLevelLightBlur);
        var lightBlurSuppressed = false;
        var drawLowerContentLightBlur = _configuration.GetCVar(CCVars.ClassicZLevelsRenderingLowerLevelContentLightBlur);
        var drawLowerAmbientOcclusion = _configuration.GetCVar(CCVars.ClassicZLevelsRenderingLowerLevelAmbientOcclusion);
        var useDirectLowerLightTarget =
            _configuration.GetCVar(CCVars.ClassicZLevelsRenderingLowerLevelDirectLightTarget) &&
            !drawLowerContentLightBlur;

        var configuredSoftShadows = _configuration.GetCVar(CVars.LightSoftShadows);
        var suppressLowerSoftShadows = configuredSoftShadows &&
                                       !_configuration.GetCVar(CCVars.ClassicZLevelsRenderingLowerLevelSoftShadows);
        var softShadowsSuppressed = false;

        //From the lowest depth to the highest, render each level
        try
        {
            for (var depth = lowestDepth; depth <= lookUp; depth++)
            {
                var target = depth < 0 ? lowerViewport : viewport;
                if (depth == 0)
                {
                    if (!ReferenceEquals(lowerViewport, viewport))
                    {
                        handle.RenderInRenderTarget(viewport.RenderTarget, () =>
                        {
                            var screen = handle.DrawingHandleScreen;
                            screen.SetTransform(Matrix3x2.Identity);
                            screen.UseShader(null);
                            screen.DrawTextureRect(lowerViewport.RenderTarget.Texture, (UIBox2) lowerBounds);
                        }, Color.Black);
                    }
                    viewport.Eye = _fallbackEye;
                }
                else
                {
                    if (!_zLevels.TryMapOffset(zMapUid, depth, out var mapUidBelow))
                        continue;

                    if (!_mapQuery.Value.TryComp(mapUidBelow, out var mapComp))
                        continue;

                    Angle rotation = _fallbackEye.Rotation * -1;
                    var offset = rotation.ToWorldVec() * ClassicClientZLevelsSystem.ZLevelOffset * depth;

                    target.Eye = new ZEye(lowestDepth, depth, lookUp)
                    {
                        Position = new MapCoordinates(_fallbackEye.Position.Position, mapComp.MapId),
                        DrawFov = _fallbackEye.DrawFov && depth >= 0,
                        DrawLight = _fallbackEye.DrawLight,
                        Offset = _fallbackEye.Offset + offset + (depth < 0 ? lowerCameraOffset : Vector2.Zero),
                        Rotation = _fallbackEye.Rotation,
                        Scale = _fallbackEye.Scale,
                        DrawContentLightBlur = depth >= 0 || drawLowerContentLightBlur,
                        DrawAmbientOcclusion = depth >= 0 || drawLowerAmbientOcclusion,
                        UseDirectContentLightTarget = depth < 0 && useDirectLowerLightTarget,
                    };
                }

                var shouldSuppressLightBlur = suppressLowerLightBlur && depth < 0;
                if (shouldSuppressLightBlur != lightBlurSuppressed)
                {
                    _configuration.SetCVar(CVars.LightBlur, shouldSuppressLightBlur ? false : configuredLightBlur);
                    lightBlurSuppressed = shouldSuppressLightBlur;
                }

                var shouldSuppressSoftShadows = suppressLowerSoftShadows && depth < 0;
                if (shouldSuppressSoftShadows != softShadowsSuppressed)
                {
                    _configuration.SetCVar(
                        CVars.LightSoftShadows,
                        shouldSuppressSoftShadows ? false : configuredSoftShadows);
                    softShadowsSuppressed = shouldSuppressSoftShadows;
                }

                target.ClearColor = depth == lowestDepth ? Color.Black : null;
                target.Render();
            }
        }
        finally
        {
            if (lightBlurSuppressed)
                _configuration.SetCVar(CVars.LightBlur, configuredLightBlur);
            if (softShadowsSuppressed)
                _configuration.SetCVar(CVars.LightSoftShadows, configuredSoftShadows);

            // Restore the Eye and render state even if a viewport pass fails.
            Eye = _fallbackEye;
            viewport.Eye = Eye;
            viewport.ClearColor = fallbackClearColor;
            if (_lowerViewport != null)
                _lowerViewport.Eye = null;
        }
    }

    public sealed partial class ZEye(int lowest, int depth, int high) : Robust.Shared.Graphics.Eye, IClassicZLevelRenderQuality
    {
        public int LowestDepth = lowest;
        public int Depth = depth;
        public int HighestDepth = high;

        public bool DrawContentLightBlur { get; init; } = true;
        public bool DrawAmbientOcclusion { get; init; } = true;
        public bool UseDirectContentLightTarget { get; init; }
    }
}
