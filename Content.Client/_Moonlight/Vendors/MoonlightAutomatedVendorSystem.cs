using Content.Shared._Moonlight.Vendors;
using Robust.Client.GameObjects;

namespace Content.Client._Moonlight.Vendors;

public sealed partial class MoonlightAutomatedVendorSystem : SharedMoonlightAutomatedVendorSystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoonlightVendorUserComponent, AfterAutoHandleStateEvent>(OnUserCompState);
        SubscribeLocalEvent<MoonlightAutomatedVendorComponent, AfterAutoHandleStateEvent>(OnVendorCompState);
    }

    private void OnUserCompState(Entity<MoonlightVendorUserComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshOpenUis();
    }

    private void OnVendorCompState(Entity<MoonlightAutomatedVendorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_ui.TryGetOpenUi(ent.Owner, MoonlightAutomatedVendorUI.Key, out var bui) && bui is MoonlightAutomatedVendorBui vendorBui)
        {
            vendorBui.Refresh();
        }
    }

    private void RefreshOpenUis()
    {
        var query = EntityQueryEnumerator<MoonlightAutomatedVendorComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_ui.TryGetOpenUi(uid, MoonlightAutomatedVendorUI.Key, out var bui) && bui is MoonlightAutomatedVendorBui vendorBui)
            {
                vendorBui.Refresh();
            }
        }
    }
}
