using Content.Server._NullLink.Helpers;
using Content.Server.EUI;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._NullLink.PlayerData;

public sealed partial class NullLinkPlayerManager
{
    [Dependency] private EuiManager _euiManager = default!;

    private readonly HashSet<ICommonSession> _discordPromptOpen = [];

    // ask the cluster for the live link state and nudge the player if no discord is tied to the account
    private void CheckDiscordLink(ICommonSession session)
    {
        if (_discordPromptOpen.Contains(session))
        // classic start
            return;

        if (_playerById.TryGetValue(session.UserId, out var playerData) && playerData.DiscordId != 0)
        // classic end
            return;

        var url = GetDiscordAuthUrl(session.UserId.ToString());
        if (string.IsNullOrEmpty(url))
            return;

    // classic start
    if (_actors.TryGetServerGrain(out var serverGrain))
    {
    // classic-end
        serverGrain.GetPlayerDiscordId(session.UserId)
            .Then(discordId =>
            {
                if (discordId != 0) // classic edit
                // classic start
                    if (_playerById.TryGetValue(session.UserId, out var data))
                        data.DiscordId = discordId;
                else
                // classic end
                    _taskManager.RunOnMainThread(() => OpenDiscordPrompt(session, url));
            })
            .FireAndForget(err => _sawmill.Error($"Discord link check failed for {session.UserId}: {err}"));
    }
    // classic start
    else
        _taskManager.RunOnMainThread(() => OpenDiscordPrompt(session, url));
    }
    // classic end

    private void OpenDiscordPrompt(ICommonSession session, string url)
    {
        if (session.Status == SessionStatus.Disconnected || !_discordPromptOpen.Add(session))
            return;

        var eui = new DiscordLinkEui(this, url);
        _euiManager.OpenEui(eui, session);
        eui.StateDirty();
    }

    internal void OnDiscordPromptClosed(ICommonSession session)
        => _discordPromptOpen.Remove(session);
}
