using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Alert;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.ZLevels.Alert;

public sealed partial class ClassicZLevelAlertSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly EntityQuery<ClassicZMapComponent> _zMapQuery = default!;
    [Dependency] private readonly EntityQuery<AlertsComponent> _alertsQuery = default!;
    [Dependency] private readonly EntityQuery<TransformComponent> _xformQuery = default!;

    public static readonly ProtoId<AlertPrototype> AlertZLevel = "ClassicZLevel";

    public const short MinDepth = -5;
    public const short MaxDepth = 5;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AlertSyncEvent>(OnAlertSync);
        SubscribeLocalEvent<EntParentChangedMessage>(OnEntParentChanged);
        SubscribeLocalEvent<AlertsComponent, ClassicZLevelMapMoveEvent>(OnZLevelMove);
    }

    private void OnAlertSync(AlertSyncEvent ev)
    {
        if (_alertsQuery.TryComp(ev.Euid, out var alerts))
            TryUpdateAlert((ev.Euid, alerts));
    }

    private void OnEntParentChanged(ref EntParentChangedMessage args)
    {
        if (args.OldMapId == args.Transform.MapUid)
            return;

        if (_alertsQuery.TryComp(args.Entity, out var alerts))
            TryUpdateAlert((args.Entity, alerts), args.Transform);
    }

    private void OnZLevelMove(Entity<AlertsComponent> ent, ref ClassicZLevelMapMoveEvent args)
    {
        var severity = (short) Math.Clamp(args.CurrentZLevel, MinDepth, MaxDepth);
        _alerts.ShowAlert(ent.AsNullable(), AlertZLevel, severity);
    }

    public bool TryGetZLevel(EntityUid uid, out int depth, TransformComponent? xform = null)
    {
        depth = 0;

        xform ??= _xformQuery.GetComponent(uid);
        if (xform.MapUid is not { } mapUid || TerminatingOrDeleted(mapUid))
            return false;

        if (!_zMapQuery.TryComp(mapUid, out var zMap))
            return false;

        depth = zMap.Depth;
        return true;
    }

    public bool TryUpdateAlert(Entity<AlertsComponent> ent, TransformComponent? xform = null)
    {
        if (TryGetZLevel(ent.Owner, out var depth, xform))
        {
            var severity = (short) Math.Clamp(depth, MinDepth, MaxDepth);
            _alerts.ShowAlert(ent.AsNullable(), AlertZLevel, severity);
            return true;
        }

        _alerts.ClearAlert(ent.AsNullable(), AlertZLevel);
        return false;
    }
}
