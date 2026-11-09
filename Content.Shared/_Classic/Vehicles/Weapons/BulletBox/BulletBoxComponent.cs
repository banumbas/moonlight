using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(BulletBoxSystem))]
public sealed partial class BulletBoxComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Amount = 600;

    [DataField, AutoNetworkedField]
    public int Max = 600;

    [DataField(required: true), AutoNetworkedField]
    public EntProtoId BulletType;

    [DataField, AutoNetworkedField]
    public string? UsedIn;

    [DataField, AutoNetworkedField]
    public EntProtoId? CartridgeType;

    [DataField, AutoNetworkedField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1.5);
}
