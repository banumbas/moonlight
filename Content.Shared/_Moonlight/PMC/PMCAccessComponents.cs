using Robust.Shared.GameStates;

namespace Content.Shared._Moonlight.PMC;

[RegisterComponent, NetworkedComponent]
public sealed partial class PMCAssaultComponent : Component
{
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PMCMedicComponent : Component
{
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PMCSpecialistComponent : Component
{
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PMCCommanderComponent : Component
{
}
