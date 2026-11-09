using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Projectiles;

/// <summary>
/// Attached to ground fire hazards created by flame streams.
/// Links to the chemical fuel's combustion properties.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true, raiseAfterAutoHandleState: true)]
public sealed partial class FlameTileFireComponent : Component
{
    /// <summary>
    /// ID of the reagent flame effect prototype governing visual and combustion properties.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ReagentFlameEffectPrototype> Effect = "Napalm";
}
