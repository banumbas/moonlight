using Content.Shared._Moonlight.Vehicles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(BulletBoxSystem))]
public sealed partial class RefillableByBulletBoxComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId? BulletType;
}
