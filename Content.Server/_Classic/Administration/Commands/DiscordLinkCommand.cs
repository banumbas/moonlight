using System.Linq;
using Content.Server.Administration;
using Content.Server._NullLink.PlayerData;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server._Classic.Administration.Commands;

[AdminCommand(AdminFlags.Permissions)]
public sealed class DiscordLinkCommand : IConsoleCommand
{
    public string Command => "linkdiscord";
    public string Description => "Links a player's Discord ID and optionally updates their Discord roles.";
    public string Help => "Usage: linkdiscord <player_name_or_guid> <discord_id> [roleId1 roleId2 ...]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        var playerMgr = IoCManager.Resolve<IPlayerManager>();
        var nullLinkPlayerMgr = IoCManager.Resolve<INullLinkPlayerManager>();

        NetUserId userId;
        if (Guid.TryParse(args[0], out var guid))
        {
            userId = new NetUserId(guid);
        }
        else if (playerMgr.TryGetSessionByUsername(args[0], out var session))
        {
            userId = session.UserId;
        }
        else
        {
            shell.WriteError($"Player '{args[0]}' not found.");
            return;
        }

        if (!ulong.TryParse(args[1], out var discordId))
        {
            shell.WriteError($"Invalid Discord ID: '{args[1]}'. Must be a 64-bit unsigned integer.");
            return;
        }

        List<ulong>? roles = null;
        if (args.Length > 2)
        {
            roles = new List<ulong>();
            for (var i = 2; i < args.Length; i++)
            {
                if (ulong.TryParse(args[i], out var roleId))
                    roles.Add(roleId);
                else
                    shell.WriteError($"Warning: '{args[i]}' is not a valid ulong role ID, skipping.");
            }
        }

        nullLinkPlayerMgr.LinkPlayerDiscord(userId, discordId, roles);
        shell.WriteLine($"Successfully linked player '{args[0]}' ({userId}) to Discord ID {discordId}" + (roles != null ? $" with {roles.Count} roles." : "."));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "player");
        }

        if (args.Length == 2)
            return CompletionResult.FromHint("discord_id");

        return CompletionResult.FromHint("role_id");
    }
}
