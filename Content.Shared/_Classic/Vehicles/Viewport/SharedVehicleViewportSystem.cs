using System.Numerics;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Camera;

namespace Content.Shared._Classic.Vehicles.Viewport;

/// <summary>Keeps the exterior camera and spatial visibility at the same vehicle-local position.</summary>
public abstract partial class SharedVehicleViewportSystem : EntitySystem
{
    [Dependency] protected SharedEyeSystem _eye = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityQuery<EyeComponent> _eyeQuery = default!;
    [Dependency] private EntityQuery<ClassicVehicleComponent> _vehicleQuery = default!;
    [Dependency] private EntityQuery<VehicleViewportUserComponent> _viewportQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleViewToggleComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
        SubscribeLocalEvent<VehicleViewToggleComponent, ClassicGetViewPositionEvent>(OnGetViewPosition);
    }

    private void OnGetEyeOffset(Entity<VehicleViewToggleComponent> ent, ref GetEyeOffsetEvent args)
    {
        if (_eyeQuery.TryComp(ent, out var eye) && eye.Target is { } target)
            args.Offset += GetExteriorOffset(ent, target);
    }

    private void OnGetViewPosition(Entity<VehicleViewToggleComponent> ent, ref ClassicGetViewPositionEvent args)
    {
        args.Position += GetExteriorOffset(ent, args.Target);
    }

    private Vector2 GetExteriorOffset(EntityUid viewer, EntityUid target)
    {
        if (!_vehicleQuery.TryComp(target, out var vehicle))
            return Vector2.Zero;

        var offset = vehicle.CameraOffset;
        if (_viewportQuery.TryComp(viewer, out var viewport) && viewport.PeekTarget == null)
            offset = viewport.ExteriorOffset;

        return _transform.GetWorldRotation(target).RotateVec(offset);
    }
}
