/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Moonlight.ZLevels.Core;
using Content.Server.Administration;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Moonlight.ZLevels.Mapping.Commands;

[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class MoonlightInitializeZNetworkCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private MoonlightZLevelsSystem _zLevels = default!;

    public override string Command => "znetwork-initialize";
    public override string Description => "Initialize all zNetwork maps.";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var options = new List<CompletionOption>();
        var query = _entities.EntityQueryEnumerator<MoonlightZMapNetworkComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var zLevelComp, out var meta))
        {
            options.Add(new CompletionOption(_entities.GetNetEntity(uid).ToString(), meta.EntityName));
        }

        return CompletionResult.FromHintOptions(options, "zNetwork net entity");
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        // get the target
        EntityUid? target;

        if (!NetEntity.TryParse(args[0], out var targetNet) ||
            !_entities.TryGetEntity(targetNet, out target))
        {
            shell.WriteError($"Unable to find entity {args[1]}");
            return;
        }

        if (!_entities.TryGetComponent<MoonlightZMapNetworkComponent>(target, out var levelComp))
        {
            shell.WriteError($"Target entity doesnt have MoonlightZLevelsNetworkComponent {args[1]}");
            return;
        }

        _zLevels.InitializeZNetwork((target.Value, levelComp));
        shell.WriteLine("Done.");
    }
}
