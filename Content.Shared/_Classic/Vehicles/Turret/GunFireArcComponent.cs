using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles.Turret;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunFireArcComponent : Component
{
    [DataField, AutoNetworkedField]
    public Angle Arc = Angle.FromDegrees(90);

    [DataField, AutoNetworkedField]
    public Angle AngleOffset = Angle.Zero;
}
