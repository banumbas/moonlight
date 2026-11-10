using Content.Client._Moonlight.ZCollapse.Overlays;
using Content.Shared._Moonlight.ZCollapse.Events;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client._Moonlight.ZCollapse;

public sealed partial class MoonlightZCollapseClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;

    public Dictionary<NetEntity, Dictionary<Vector2i, int>>? Grids;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MoonlightZCollapseOverlayToggledEvent>(OnOverlayToggled);
        SubscribeNetworkEvent<MoonlightZCollapseOverlaySnapshotEvent>(OnSnapshotUpdate);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay<MoonlightZCollapseDebugOverlay>();
    }

    private void OnOverlayToggled(MoonlightZCollapseOverlayToggledEvent ev)
    {
        if (ev.IsEnabled)
            _overlayMan.AddOverlay(new MoonlightZCollapseDebugOverlay());
        else
        {
            _overlayMan.RemoveOverlay<MoonlightZCollapseDebugOverlay>();
            Grids = null;
        }
    }

    private void OnSnapshotUpdate(MoonlightZCollapseOverlaySnapshotEvent ev)
    {
        Grids ??= new Dictionary<NetEntity, Dictionary<Vector2i, int>>();

        foreach (var (grid, tiles) in ev.Grids)
        {
            Grids[grid] = tiles;
        }
    }
}


