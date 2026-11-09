using Content.Shared.VendingMachines;
using Robust.Shared.Timing;

namespace Content.Server._Classic.VendingMachines;

public sealed partial class AutoRestockVendingMachineSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AutoRestockVendingMachineComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<AutoRestockVendingMachineComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextRestock = _timing.CurTime + GetRestockDelay(ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AutoRestockVendingMachineComponent, VendingMachineComponent>();
        while (query.MoveNext(out var uid, out var autoRestock, out var vending))
        {
            if (_timing.CurTime < autoRestock.NextRestock)
                continue;

            autoRestock.NextRestock += GetRestockDelay(autoRestock);

            Restock(uid, autoRestock, vending);
        }
    }

    private void Restock(EntityUid uid, AutoRestockVendingMachineComponent autoRestock, VendingMachineComponent vending)
    {
        var item = autoRestock.Item.Id;
        if (vending.Inventory.TryGetValue(item, out var entry))
        {
            if (entry.Amount >= autoRestock.MaxStock)
                return;

            entry.Amount++;
        }
        else
        {
            vending.Inventory[item] = new VendingMachineInventoryEntry(InventoryType.Regular, item, 1);
        }

        Dirty(uid, vending);
    }

    private static TimeSpan GetRestockDelay(AutoRestockVendingMachineComponent component)
    {
        return component.RestockDelay > TimeSpan.Zero
            ? component.RestockDelay
            : TimeSpan.FromSeconds(1);
    }
}
