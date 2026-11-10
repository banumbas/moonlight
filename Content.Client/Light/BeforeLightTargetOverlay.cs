using Content.Client.Graphics;
using Content.Client._Moonlight.ZLevels.Core; // Moonlight-Edit
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client.Light;

/// <summary>
/// Handles an enlarged lighting target so content can use large blur radii.
/// </summary>
public sealed partial class BeforeLightTargetOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    [Dependency] private IClyde _clyde = default!;

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    public Box2Rotated EnlargedBounds;

    /// <summary>
    /// In metres
    /// </summary>
    private float _skirting = 2f;

    public const int ContentZIndex = -10;

    public BeforeLightTargetOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ContentZIndex;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Moonlight-Edit start - PlanetLightSystem makes the engine clear this target to transparent.
        // The regular enlarged-target path restores the map ambient color below, so the direct path
        // must do the same or lower Z-levels would become black outside explicit lights.
        if (args.Viewport.Eye is IMoonlightZLevelRenderQuality { UseDirectContentLightTarget: true })
        {
            EnlargedBounds = args.WorldBounds;
            args.WorldHandle.RenderInRenderTarget(
                args.Viewport.LightRenderTarget,
                static () => { },
                _clyde.GetClearColor(args.MapUid));
            return;
        }
        // Moonlight-Edit end

        // Code is weird but I don't think engine should be enlarging the lighting render target arbitrarily either, maybe via cvar?
        // The problem is the blur has no knowledge of pixels outside the viewport so with a large enough blur radius you get sampling issues.
        var size = args.Viewport.LightRenderTarget.Size + (int) (_skirting * EyeManager.PixelsPerMeter);
        EnlargedBounds = args.WorldBounds.Enlarged(_skirting / 2f);

        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());

        // This just exists to copy the lightrendertarget and write back to it.
        if (res.EnlargedLightTarget?.Size != size)
        {
            res.EnlargedLightTarget = _clyde
                .CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "enlarged-light-copy");
        }

        args.WorldHandle.RenderInRenderTarget(res.EnlargedLightTarget,
            () =>
            {
            }, _clyde.GetClearColor(args.MapUid));
    }

    internal CachedResources GetCachedForViewport(IClydeViewport viewport)
    {
        return _resources.GetForViewport(viewport,
            static _ => throw new InvalidOperationException(
                "Expected BeforeLightTargetOverlay to have created its resources"));
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();

        base.DisposeBehavior();
    }

    internal sealed class CachedResources : IDisposable
    {
        public IRenderTexture EnlargedLightTarget = default!;

        public void Dispose()
        {
            EnlargedLightTarget?.Dispose();
        }
    }
}
