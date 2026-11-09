using Content.Shared.UserInterface;
using Content.Shared.Whitelist;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Classic.Vendors;

public abstract partial class SharedClassicAutomatedVendorSystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<EntProtoId, int> _globalStock = new();

    private EntityQuery<ClassicVendorUserComponent> _userQuery;

    public override void Initialize()
    {
        base.Initialize();

        _userQuery = GetEntityQuery<ClassicVendorUserComponent>();

        SubscribeLocalEvent<ClassicAutomatedVendorComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<ClassicAutomatedVendorComponent, ClassicVendorVendBuiMsg>(OnVendMessage);
    }

    private void OnOpenAttempt(Entity<ClassicAutomatedVendorComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (ent.Comp.UserWhitelist != null && _whitelist.IsWhitelistFail(ent.Comp.UserWhitelist, args.User))
        {
            args.Cancel();
        }
    }

    protected virtual void OnVendMessage(Entity<ClassicAutomatedVendorComponent> vendor, ref ClassicVendorVendBuiMsg args)
    {
        var user = args.Actor;

        if (!_userQuery.TryComp(user, out var userComp))
            return;

        if (args.Section < 0 || args.Section >= vendor.Comp.Sections.Count)
            return;

        var section = vendor.Comp.Sections[args.Section];

        if (args.Entry < 0 || args.Entry >= section.Entries.Count)
            return;

        var entry = section.Entries[args.Entry];

        // Point validation
        var userPoints = vendor.Comp.PointsType == null
            ? userComp.Points
            : userComp.ExtraPoints?.GetValueOrDefault(vendor.Comp.PointsType) ?? 0;

        if (entry.Points != null && entry.Points.Value > 0 && userPoints < entry.Points.Value)
            return; // Not enough points

        if (entry.Stock != null && entry.Stock.Value <= 0)
            return;

        if (section.TakeOne != null && userComp.TakeOne.Contains(section.TakeOne))
            return;

        if (section.TakeAll != null && userComp.TakeAll.Contains((section.TakeAll, entry.Id.Id)))
            return;

        // Deduct points
        if (entry.Points != null && entry.Points.Value > 0)
        {
            if (vendor.Comp.PointsType == null)
            {
                userComp.Points -= entry.Points.Value;
            }
            else
            {
                userComp.ExtraPoints ??= new();
                userComp.ExtraPoints[vendor.Comp.PointsType] = userPoints - entry.Points.Value;
            }
        }

        // Global stock update across all vendor entities
        if (entry.Stock != null)
        {
            var newStock = Math.Max(0, entry.Stock.Value - 1);
            SetGlobalStock(entry.Id, newStock);
        }

        if (section.TakeOne != null)
        {
            userComp.TakeOne.Add(section.TakeOne);
        }

        if (section.TakeAll != null)
        {
            userComp.TakeAll.Add((section.TakeAll, entry.Id.Id));
        }

        Dirty(user, userComp);
    }

    public void SetGlobalStock(EntProtoId itemId, int newStock)
    {
        _globalStock[itemId] = newStock;

        var query = EntityQueryEnumerator<ClassicAutomatedVendorComponent>();
        while (query.MoveNext(out var uid, out var vendorComp))
        {
            var changed = false;
            foreach (var sec in vendorComp.Sections)
            {
                foreach (var ent in sec.Entries)
                {
                    if (ent.Id == itemId && ent.Stock != null)
                    {
                        if (ent.Stock != newStock)
                        {
                            ent.Stock = newStock;
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                Dirty(uid, vendorComp);
            }
        }
    }
}
