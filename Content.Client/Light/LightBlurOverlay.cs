using Content.Client.Graphics;
using Content.Client._Moonlight.ZLevels.Core; // Moonlight-Edit
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client.Light;

/// <summary>
/// Essentially handles blurring for content-side light overlays.
/// </summary>
public sealed partial class LightBlurOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IOverlayManager _overlay = default!;

    public const int ContentZIndex = TileEmissionOverlay.ContentZIndex + 1;

    private readonly OverlayResourceCache<CachedResources> _resources = new();

    public LightBlurOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ContentZIndex;
    }

    // Moonlight-Edit
    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye is not IMoonlightZLevelRenderQuality quality ||
               quality.DrawContentLightBlur && !quality.UseDirectContentLightTarget;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return;

        var beforeOverlay = _overlay.GetOverlay<BeforeLightTargetOverlay>();
        var beforeLightRes = beforeOverlay.GetCachedForViewport(args.Viewport);
        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());

        var size = beforeLightRes.EnlargedLightTarget.Size;

        if (res.BlurTarget?.Size != size)
        {
            res.BlurTarget?.Dispose(); // Moonlight-Add
            res.BlurTarget = _clyde
                .CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "enlarged-light-blur");
        }

        var target = beforeLightRes.EnlargedLightTarget;
        // Yeah that's all this does keep walkin.
        _clyde.BlurRenderTarget(args.Viewport, target, res.BlurTarget, args.Viewport.Eye, 14f * 5f);
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();

        base.DisposeBehavior();
    }

    private sealed class CachedResources : IDisposable
    {
        public IRenderTarget? BlurTarget;

        public void Dispose()
        {
            BlurTarget?.Dispose();
        }
    }
}
