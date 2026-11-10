using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Moonlight.ZCollapse.Commands;

/// <summary>
/// Toggles the ZCollapse tile-stability debug overlay for the calling player.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class MoonlightShowGridStabilityCommand : LocalizedEntityCommands
{
    [Dependency] private MoonlightZCollapseSystem _collapse = default!;

    public override string Command => "showgridstability";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var session = shell.Player;
        if (session == null)
            return;

        _collapse.ToggleDebugView(session);
    }
}


