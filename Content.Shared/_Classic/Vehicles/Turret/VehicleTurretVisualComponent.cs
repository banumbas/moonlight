using Content.Shared._Classic.Vehicles;
using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class VehicleTurretVisualComponent : Component
{
    [AutoNetworkedField]
    public NetEntity Turret;

    [NonSerialized]
    public bool SpriteInitialized;
}
