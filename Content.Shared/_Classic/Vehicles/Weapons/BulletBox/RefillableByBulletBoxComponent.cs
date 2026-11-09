using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(BulletBoxSystem))]
public sealed partial class RefillableByBulletBoxComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId? BulletType;
}
