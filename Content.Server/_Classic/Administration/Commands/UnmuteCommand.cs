using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Classic.Administration.Commands;

[AdminCommand(AdminFlags.Ban)]
public sealed class UnmuteCommand : IConsoleCommand
{
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IBanManager _bans = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    public string Command => "unmute";
    public string Description => "Снимает все активные муты с указанного игрока.";
    public string Help => "Использование: unmute <игрок>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var targetName = args[0];
        var located = await _locator.LookupIdByNameOrIdAsync(targetName);
        if (located == null)
        {
            shell.WriteError($"Игрок '{targetName}' не найден.");
            return;
        }

        var unbannedCount = await _bans.UnmutePlayer(located.UserId, shell.Player?.UserId);
        if (unbannedCount > 0)
        {
            var msg = $"Сняты все муты ({unbannedCount} каналов) с игрока {located.Username}.";
            shell.WriteLine(msg);
            _chat.SendAdminAlert($"Администратор {shell.Player?.Name ?? "Консоль"} снял все муты с игрока {located.Username} ({unbannedCount} ограничений).");
        }
        else
        {
            shell.WriteLine($"У игрока {located.Username} нет активных мутов.");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(),
                "Имя игрока"
            );
        }

        return CompletionResult.Empty;
    }
}
