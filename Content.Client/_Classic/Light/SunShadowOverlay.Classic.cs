/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

// Namespace does not match folder structure
#pragma warning disable IDE0130
namespace Content.Client.Light;

public sealed partial class SunShadowOverlay
{
    private static readonly ProtoId<ShaderPrototype> StencilClearShaderPrototype = "StencilClear";
    private static readonly ProtoId<ShaderPrototype> StencilMaskShaderPrototype = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> UnroofedMixShaderPrototype = "StencilDraw";

    private readonly List<Entity<SunShadowComponent>> _sunShadows = new();
    private readonly List<WorldRect> _roofRects = new();
    private readonly ShaderInstance _stencilClearShader;
    private readonly ShaderInstance _stencilMaskShader;
    private readonly ShaderInstance _stencilRestoreShader;
    private readonly ShaderInstance _unroofedMixShader;

    /// <summary>
    /// A map-wide sun is authoritative. Grid components remain a fallback for legacy maps that put
    /// sunlight on their terrain grid instead of the map entity.
    /// </summary>
    private List<Entity<SunShadowComponent>> GetSunShadows(EntityUid mapUid)
    {
        _sunShadows.Clear();

        if (_entManager.TryGetComponent(mapUid, out SunShadowComponent? mapSun))
        {
            _sunShadows.Add((mapUid, mapSun));
            return _sunShadows;
        }

        foreach (var grid in _grids)
        {
            if (_entManager.TryGetComponent(grid.Owner, out SunShadowComponent? gridSun))
                _sunShadows.Add((grid.Owner, gridSun));
        }

        return _sunShadows;
    }

    /// <summary>
    /// Marks roofed pixels in the light target's stencil buffer. <see cref="UnroofedMixShaderPrototype"/>
    /// then applies the already-blurred shadow texture only where the stencil remains clear.
    /// </summary>
    private void PrepareSunShadowRoofStencil(
        DrawingHandleWorld worldHandle,
        Matrix3x2 worldToTarget,
        Box2Rotated worldBounds)
    {
        // This target may have stencil data from an earlier render pass. Clear only the viewport
        // footprint; the shader writes transparent pixels, leaving the light color untouched.
        worldHandle.SetTransform(worldToTarget);
        worldHandle.UseShader(_stencilClearShader);
        worldHandle.DrawRect(worldBounds.CalcBoundingBox(), Color.White);

        worldHandle.UseShader(_stencilMaskShader);

        foreach (var grid in _grids)
        {
            var hasImplicitRoof = _entManager.HasComponent<ImplicitRoofComponent>(grid.Owner);
            _entManager.TryGetComponent(grid.Owner, out RoofComponent? roof);

            if (!hasImplicitRoof && roof == null)
                continue;

            var gridToTarget = Matrix3x2.Multiply(_xformSys.GetWorldMatrix(grid.Owner), worldToTarget);
            worldHandle.SetTransform(gridToTarget);

            var tiles = _map.GetTilesEnumerator(grid.Owner, grid, worldBounds);
            var checkEntities = false;

            if (!hasImplicitRoof && roof != null)
            {
                var localBounds = _xformSys.GetInvWorldMatrix(grid.Owner)
                    .TransformBox(worldBounds.CalcBoundingBox())
                    .Enlarged(grid.Comp.TileSize);
                checkEntities = _roof.HasRoofEntities(grid.Owner, localBounds);
            }

            _roofRects.Clear();

            while (tiles.MoveNext(out var tile))
            {
                if (!hasImplicitRoof &&
                    roof != null &&
                    _roof.GetColor((grid.Owner, grid.Comp, roof), tile.GridIndices, checkEntities) == null)
                {
                    continue;
                }

                _roofRects.Add(new WorldRect(_lookup.GetLocalBounds(tile, grid.Comp.TileSize), Color.White));
            }

            worldHandle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_roofRects));
        }
    }

    /// <summary>
    /// BeforeLighting overlays share the light target's stencil attachment with the engine light
    /// pass. Point lights expect untouched pixels to contain 0xFF, so restore that invariant after
    /// using the attachment as a temporary roof mask.
    /// </summary>
    private void RestoreLightStencil(
        DrawingHandleWorld worldHandle,
        Matrix3x2 worldToTarget,
        Box2Rotated worldBounds)
    {
        worldHandle.SetTransform(worldToTarget);
        worldHandle.UseShader(_stencilRestoreShader);
        worldHandle.DrawRect(worldBounds.CalcBoundingBox(), Color.White);
    }
}
