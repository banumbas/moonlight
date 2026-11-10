using Content.Shared.Gravity;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Moonlight.ZLevels.Gravity;

public sealed partial class MoonlightAutoGridGravitySystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MoonlightAutoGridGravityComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<MoonlightAutoGridGravityComponent, MapInitEvent>(OnComponentInit);
        SubscribeLocalEvent<GridInitializeEvent>(OnGridInit);
    }

    private void OnComponentInit(Entity<MoonlightAutoGridGravityComponent> ent, ref ComponentInit args)
    {
        if (!_map.IsInitialized(ent.Owner))
            return;

        EnableMapGravity(ent.Owner);
    }

    // Fires when the component is added to the map entity.
    // If the map is already initialized (zLevelsComponentOverrides flow), iterate existing grids.
    // If not yet initialized, GridInitializeEvent handles each grid as it comes up.
    private void OnComponentInit(Entity<MoonlightAutoGridGravityComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<MapComponent>(ent, out var mapComp) || !_map.IsInitialized(ent.Owner))
            return;

        EnableMapGravity(ent.Owner, mapComp);
    }

    private void EnableMapGravity(EntityUid mapUid, MapComponent? mapComp = null)
    {
        if (!TryComp(mapUid, out mapComp))
            return;

        foreach (var grid in _map.GetAllGrids(mapComp.MapId))
            EnableGravity(grid.Owner);

        EnableGravity(mapUid);
    }

    // Fires for every grid that initializes. Handles both map-load time (component already on map)
    // and runtime grid spawning (e.g. shuttles arriving).
    private void OnGridInit(GridInitializeEvent ev)
    {
        var mapUid = Transform(ev.EntityUid).MapUid;
        if (mapUid == null || !HasComp<MoonlightAutoGridGravityComponent>(mapUid.Value))
            return;

        EnableGravity(ev.EntityUid);
    }

    private void EnableGravity(EntityUid ent)
    {
        var gravity = EnsureComp<GravityComponent>(ent);
        gravity.Inherent = true;
        gravity.Enabled = true;
        Dirty(ent, gravity);

        var xform = Transform(ent);
        _transform.SetLocalRotation(ent, Angle.Zero, xform);
        xform.NoLocalRotation = true;
    }
}
