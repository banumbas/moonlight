using Content.Shared._Classic.Vendors;
using Robust.Client.GameObjects;

namespace Content.Client._Classic.Vendors;

public sealed partial class ClassicAutomatedVendorSystem : SharedClassicAutomatedVendorSystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClassicVendorUserComponent, AfterAutoHandleStateEvent>(OnUserCompState);
        SubscribeLocalEvent<ClassicAutomatedVendorComponent, AfterAutoHandleStateEvent>(OnVendorCompState);
    }

    private void OnUserCompState(Entity<ClassicVendorUserComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshOpenUis();
    }

    private void OnVendorCompState(Entity<ClassicAutomatedVendorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_ui.TryGetOpenUi(ent.Owner, ClassicAutomatedVendorUI.Key, out var bui) && bui is ClassicAutomatedVendorBui vendorBui)
        {
            vendorBui.Refresh();
        }
    }

    private void RefreshOpenUis()
    {
        var query = EntityQueryEnumerator<ClassicAutomatedVendorComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_ui.TryGetOpenUi(uid, ClassicAutomatedVendorUI.Key, out var bui) && bui is ClassicAutomatedVendorBui vendorBui)
            {
                vendorBui.Refresh();
            }
        }
    }
}
