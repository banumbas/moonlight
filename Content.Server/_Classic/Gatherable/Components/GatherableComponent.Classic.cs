using Content.Shared.Tools;
using Robust.Shared.Prototypes;

namespace Content.Server.Gatherable.Components;

/// <summary>
/// Classic-only tool quality extensions for gatherable entities.
/// </summary>
public sealed partial class GatherableComponent
{
    /// <summary>
    /// Tool qualities that can be used in addition to <see cref="ToolWhitelist"/>.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ToolQualityPrototype>> ToolQualities = [];
}
