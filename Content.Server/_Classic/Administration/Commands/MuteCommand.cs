using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Classic.Administration.Commands;

[AdminCommand(AdminFlags.Ban)]
public sealed class MuteCommand : IConsoleCommand
{
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IBanManager _bans = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    private static readonly Dictionary<string, string> AvailableChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        { "local", "Punish:Mute:Local" },
        { "whisper", "Punish:Mute:Whisper" },
        { "radio", "Punish:Mute:Radio" },
        { "looc", "Punish:Mute:LOOC" },
        { "ooc", "Punish:Mute:OOC" },
        { "emotes", "Punish:Mute:Emotes" },
        { "dead", "Punish:Mute:Dead" },
        { "paper", "Punish:Mute:Paper" },
    };

    public string Command => "mute";
    public string Description => "Заглушает игрока в каналах чата.";
    public string Help => "Использование: mute <игрок> [минуты] [причина] [каналы (all/local,radio,ooc...)]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Help);
            return;
        }

        var targetName = args[0];
        uint minutes = 0;
        if (args.Length > 1 && !uint.TryParse(args[1], out minutes))
        {
            shell.WriteError($"Неверное количество минут: '{args[1]}'. Укажите число (0 = навсегда).");
            return;
        }

        var reason = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : "Нарушение правил общения";

        var channelsArg = args.Length > 3 ? args[3] : "all";
        var selectedRoles = new List<string>();

        if (channelsArg.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            selectedRoles.AddRange(AvailableChannels.Values);
        }
        else
        {
            var parts = channelsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (AvailableChannels.TryGetValue(part, out var roleName))
                    selectedRoles.Add(roleName);
                else
                    shell.WriteLine($"Неизвестный канал '{part}', пропущен.");
            }
        }

        if (selectedRoles.Count == 0)
        {
            shell.WriteError("Не выбрано ни одного корректного канала для заглушения.");
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(targetName);
        if (located == null)
        {
            shell.WriteError($"Игрок '{targetName}' не найден.");
            return;
        }

        var targetUid = located.UserId;
        var targetHwid = located.LastHWId;
        var now = DateTimeOffset.UtcNow;

        await _bans.CreatePunishments(
            targetUid,
            located.Username,
            shell.Player?.UserId,
            null,
            targetHwid,
            selectedRoles,
            minutes,
            NoteSeverity.Medium,
            reason,
            now
        );

        await _bans.WebhookUpdateRoleBans(
            targetUid,
            located.Username,
            shell.Player?.UserId,
            null,
            targetHwid,
            selectedRoles,
            minutes,
            NoteSeverity.Medium,
            reason,
            now
        );

        var durationStr = minutes == 0 ? "навсегда" : $"на {minutes} мин.";
        var feedback = $"Игрок {located.Username} заглушен {durationStr} в каналах ({selectedRoles.Count}). Причина: {reason}";
        shell.WriteLine(feedback);
        _chat.SendAdminAlert($"Администратор {shell.Player?.Name ?? "Консоль"} заглушил игрока {located.Username} {durationStr}. Причина: {reason}");
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

        if (args.Length == 2)
        {
            return CompletionResult.FromHintOptions(
                new CompletionOption[]
                {
                    new("5", "5 минут"),
                    new("10", "10 минут"),
                    new("30", "30 минут"),
                    new("60", "1 час"),
                    new("1440", "1 день"),
                    new("0", "Навсегда")
                },
                "Длительность (минуты)"
            );
        }

        if (args.Length == 3)
        {
            return CompletionResult.FromHint("Причина заглушения");
        }

        if (args.Length == 4)
        {
            return CompletionResult.FromHintOptions(
                new CompletionOption[]
                {
                    new("all", "Все каналы"),
                    new("local,whisper", "Только локальный чат"),
                    new("radio", "Только рация"),
                    new("ooc,looc", "OOC и LOOC"),
                },
                "Каналы (через запятую)"
            );
        }

        return CompletionResult.Empty;
    }
}
