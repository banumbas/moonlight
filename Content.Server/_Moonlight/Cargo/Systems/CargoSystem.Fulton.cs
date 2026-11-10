using Content.Server.Cargo.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    public bool MoonlightCanSellEntity(EntityUid uid)
    {
        if (!_xformQuery.TryGetComponent(uid, out var xform))
            return false;

        return MoonlightTryGetSellEntityValue(uid, xform, out _);
    }

    private bool MoonlightTryGetSellEntityValue(EntityUid uid, TransformComponent xform, out double value)
    {
        value = 0;

        if (xform.Anchored || _blacklistQuery.HasComponent(uid) || !CanSell(uid, xform))
            return false;

        value = _pricing.GetPrice(uid);
        return value != 0;
    }

    public bool MoonlightTrySellEntity(EntityUid uid, EntityUid station, out double value)
    {
        value = 0;

        if (!TryComp<StationBankAccountComponent>(station, out var bankAccount) ||
            !_xformQuery.TryGetComponent(uid, out var xform) ||
            !MoonlightTryGetSellEntityValue(uid, xform, out value))
        {
            return false;
        }

        var sellComponent = CompOrNull<OverrideSellComponent>(uid);
        var sold = new HashSet<EntityUid> { uid };
        var ev = new EntitySoldEvent(sold, station);
        RaiseLocalEvent(ref ev);

        Del(uid);
        MoonlightApplySaleRevenue((station, bankAccount), sellComponent, value);
        Dirty(station, bankAccount);
        return true;
    }

    private void MoonlightApplySaleRevenue(
        Entity<StationBankAccountComponent> stationBank,
        OverrideSellComponent? sellComponent,
        double value,
        Dictionary<ProtoId<CargoAccountPrototype>, double>? baseDistribution = null)
    {
        Dictionary<ProtoId<CargoAccountPrototype>, double> distribution;
        if (sellComponent != null)
        {
            var cut = _lockboxCutEnabled ? stationBank.Comp.LockboxCut : stationBank.Comp.PrimaryCut;
            distribution = new Dictionary<ProtoId<CargoAccountPrototype>, double>
            {
                { sellComponent.OverrideAccount, cut },
                { stationBank.Comp.PrimaryAccount, 1.0 - cut },
            };
        }
        else
        {
            distribution = baseDistribution ?? CreateAccountDistribution(stationBank);
        }

        UpdateBankAccount((stationBank.Owner, (StationBankAccountComponent?) stationBank.Comp), (int) Math.Round(value), distribution, false);
    }
}
