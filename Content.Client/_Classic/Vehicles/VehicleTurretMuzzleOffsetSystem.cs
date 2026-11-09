using System.Numerics;
using Content.Shared._Classic.Vehicles;
using Content.Shared._Classic.Vehicles.Turret;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._Classic.Vehicles;

public sealed class VehicleTurretMuzzleOffsetSystem : EntitySystem
{
    [Dependency] private readonly GunMuzzleOffsetSystem _gunMuzzleOffset = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly VehicleTurretMuzzleSystem _turretMuzzle = default!;
    [Dependency] private readonly VehicleTurretVisualSystem _turretVisual = default!;

    public override void Initialize()
    {
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<VehicleTurretTrackedMuzzleFlashComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var trackedFlash, out var xform))
        {
            if (TerminatingOrDeleted(trackedFlash.Weapon))
                continue;

            if (!TryGetGunPose(trackedFlash.Weapon, null, out var origin, out var rotation))
                continue;

            var originMap = _transform.ToMapCoordinates(origin);
            xform.ActivelyLerping = false;
            var effectRotation = (rotation + trackedFlash.RotationOffset).Reduced();
            _transform.SetWorldRotationNoLerp((uid, xform), effectRotation);
            _transform.SetWorldPosition((uid, xform), originMap.Position + effectRotation.RotateVec(trackedFlash.Offset));
        }
    }

    public bool TryGetGunOrigin(EntityUid weaponUid, EntityCoordinates? target, out EntityCoordinates origin)
    {
        return TryGetGunPose(weaponUid, target, out origin, out _);
    }

    public bool TryGetGunPose(
        EntityUid weaponUid,
        EntityCoordinates? target,
        out EntityCoordinates origin,
        out Angle rotation)
    {
        origin = default;
        rotation = Angle.Zero;

        if (!TryComp(weaponUid, out VehicleTurretComponent? turret))
            return false;

        if (!_turretVisual.TryGetRenderedPose(weaponUid, out origin, out rotation))
        {
            origin = _transform.GetMoverCoordinates(weaponUid);
            rotation = _transform.GetWorldRotation(weaponUid);
        }

        EntityCoordinates? aimTarget = target;
        if (aimTarget == null &&
            TryComp(weaponUid, out GunComponent? gun) &&
            gun.ShootCoordinates is { } shootCoordinates)
        {
            aimTarget = shootCoordinates;
        }

        if (TryComp(weaponUid, out GunMuzzleOffsetComponent? gunMuzzle))
        {
            if (_gunMuzzleOffset.TryGetMuzzleCoordinates(weaponUid, gunMuzzle, aimTarget, out var muzzleCoords, out var muzzleRotation))
            {
                origin = muzzleCoords;
                rotation = muzzleRotation;
            }
        }

        if (TryComp(weaponUid, out VehicleTurretMuzzleComponent? turretMuzzle))
            origin = _turretMuzzle.GetMuzzleCoordinates(weaponUid, turretMuzzle, origin);

        return true;
    }


}
