using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Client._Classic.Vehicles;

[RegisterComponent]
public sealed partial class VehicleTurretTrackedMuzzleFlashComponent : Component
{
    public EntityUid Weapon;
    public Vector2 Offset = Vector2.Zero;
    public Angle RotationOffset = Angle.Zero;
}
