using Robust.Shared.Prototypes;

namespace Content.Server._Moonlight.GameTicking.Rules.Components;

/// <summary>Adds map rules after this station's Z-levels and planetary terrain are ready.</summary>
[RegisterComponent, Access(typeof(MoonlightMergeMapRuleSystem))]
public sealed partial class MoonlightStationMapRulesComponent : Component
{
    /// <summary>Rules bound to this station's Z-network when they are added.</summary>
    [DataField]
    public List<EntProtoId> Rules = [];
}
