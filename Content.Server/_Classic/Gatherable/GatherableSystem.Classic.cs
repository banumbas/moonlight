using Content.Server.Gatherable.Components;
using Content.Shared.Tools.Systems;

namespace Content.Server.Gatherable;

public sealed partial class GatherableSystem
{
    [Dependency] private SharedToolSystem _tool = default!;

    private bool TryGatherClassic(Entity<GatherableComponent> gatherable, EntityUid tool, EntityUid user)
    {
        foreach (var quality in gatherable.Comp.ToolQualities)
        {
            if (!_tool.HasQuality(tool, quality))
                continue;

            Gather(gatherable, user);
            return true;
        }

        return false;
    }
}
