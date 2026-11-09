using Content.Server._Classic.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [Dependency] private ClassicGameRuleMapFilterSystem _classicGameRuleMapFilter = default!;

    /// <summary>
    /// Returns whether a game rule is allowed by the Classic auxiliary-map filter.
    /// </summary>
    public bool CanAddGameRule(EntProtoId ruleId)
    {
        return _classicGameRuleMapFilter.CanAddGameRule(ruleId);
    }
}
