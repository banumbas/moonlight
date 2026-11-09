using Content.Server._Classic.SupplyPods;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._Classic.Salvage.Fulton;
using Content.Shared._Classic.SupplyPods;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Station.Components;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Classic.Cargo;

/// <summary>
/// Cargo delivery system using supply pods. Instead of teleporting cargo orders
/// to a trade station via shuttle, this system intercepts the FulfillCargoOrderEvent
/// and delivers orders via falling supply pods at invisible delivery pallets.
///
/// The invisible pallets ("ClassicSupplyPodDeliveryPad") are placed on the map and
/// visible only to admins with "Show Spawns" enabled. Orders are delivered to a
/// RANDOM pad each time (not always the same one).
/// </summary>
public sealed partial class ClassicCargoPodDeliverySystem : EntitySystem
{
    [Dependency] private readonly ClassicSupplyPodSystem _supplyPod = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedClassicCargoFultonSystem _fulton = default!;

    private EntityQuery<ClassicSupplyPodDeliveryPadComponent> _padQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<StationCargoOrderDatabaseComponent> _orderDbQuery;

    public override void Initialize()
    {
        base.Initialize();

        _padQuery = GetEntityQuery<ClassicSupplyPodDeliveryPadComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _orderDbQuery = GetEntityQuery<StationCargoOrderDatabaseComponent>();

        // Intercept cargo fulfillment BEFORE the default telepad handler.
        SubscribeLocalEvent<FulfillCargoOrderEvent>(OnFulfillCargoOrder, before: [typeof(CargoSystem)]);
    }

    private void OnFulfillCargoOrder(ref FulfillCargoOrderEvent args)
    {
        if (args.Handled)
            return;

        // Find a RANDOM available delivery pad on the station
        var pad = FindDeliveryPad(args.Station);
        if (pad == EntityUid.Invalid)
            return;

        var padCoords = _transform.GetMoverCoordinates(pad);

        // Require an active FultonSensorTower in range of the delivery pad,
        // same dependency as CargoFulton. If there is no powered tower nearby,
        // do NOT handle the event: the order will fall through to the standard
        // telepad handler (or be rejected by the existing "unfulfilled" logic).
        if (!_fulton.TryFindActiveSensorTower(pad, out _))
            return;

        // Get the printer output prototype from the station's order database
        string? paperProto = "PaperCargoInvoice";
        if (_orderDbQuery.TryComp(args.Station, out var orderDb))
            paperProto = orderDb.PrinterOutput;

        // Spawn the items. They will be placed inside the pod, not on the ground.
        var items = new List<EntityUid>();

        for (var i = 0; i < args.Order.OrderQuantity; i++)
        {
            // Spawn the item at the pad coordinates (temporary), then move into the pod.
            var item = Spawn(args.Order.ProductId, padCoords);
            _transform.Unanchor(item);
            if (item.IsValid())
                items.Add(item);

            // Spawn the cargo invoice paper and attach it to the pod delivery too,
            // so the manifests travel WITH the pod rather than appearing on the ground.
            if (paperProto != null)
            {
                var printed = Spawn(paperProto, padCoords);
                if (printed.IsValid())
                    items.Add(printed);
            }
        }

        if (items.Count == 0)
            return;

        args.Handled = true;
        args.FulfillmentEntity = pad;

        // Deliver all items (products + invoices) in a single supply pod.
        _supplyPod.Deliver(
            padCoords,
            items,
            visual: ClassicSupplyPodVisual.Nanotrasen,
            openOnLand: true,
            despawnTime: 0f);
    }

    /// <summary>
    /// Finds a RANDOM delivery pad on the given station. Delivery pads are the
    /// invisible ClassicSupplyPodDeliveryPad entities placed on the map.
    /// </summary>
    private EntityUid FindDeliveryPad(Entity<StationDataComponent> stationData)
    {
        var pads = new ValueList<EntityUid>();
        var stationGrids = stationData.Comp.Grids;

        var query = EntityQueryEnumerator<ClassicSupplyPodDeliveryPadComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                pads.Add(uid);
        }

        if (pads.Count == 0)
            return EntityUid.Invalid;

        // Pick a random pad so deliveries are distributed, not always the same one.
        return _random.Pick(pads);
    }
}

/// <summary>
/// Marker component for invisible supply pod delivery pads.
/// These are placed on the map where cargo should be delivered via supply pods.
/// Visible only to admins with spawn display enabled.
/// </summary>
[RegisterComponent]
public sealed partial class ClassicSupplyPodDeliveryPadComponent : Component
{
}
