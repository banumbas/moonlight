using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleAutopilotComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2i Direction = new Vector2i(1, 0);
}
