using Content.Server._Moonlight.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [Dependency] private MoonlightGameRuleMapFilterSystem _moonlightGameRuleMapFilter = default!;

    /// <summary>
    /// Returns whether a game rule is allowed by the Moonlight auxiliary-map filter.
    /// </summary>
    public bool CanAddGameRule(EntProtoId ruleId)
    {
        return _moonlightGameRuleMapFilter.CanAddGameRule(ruleId);
    }
}
