using Robust.Shared.GameStates;

namespace Content.Shared._Moonlight.Salvage.Fulton;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FultonSensorTowerComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 10f;
}
