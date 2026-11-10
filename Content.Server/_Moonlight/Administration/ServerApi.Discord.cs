#pragma warning disable IDE0130 // Namespace does not match folder structure
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server._NullLink.PlayerData;
using Robust.Server.ServerStatus;
using Robust.Shared.Network;

namespace Content.Server.Administration;

public sealed partial class ServerApi
{
    private void RegisterDiscordApi()
    {
        RegisterHandler(HttpMethod.Post, "/admin/actions/discord/link", ActionDiscordLink);
    }

    private async Task ActionDiscordLink(IStatusHandlerContext context)
    {
        var body = await ReadJson<DiscordLinkBody>(context);
        if (body == null)
            return;

        if (string.IsNullOrWhiteSpace(body.User))
        {
            await RespondError(context, ErrorCode.BadRequest, HttpStatusCode.BadRequest, "Field 'user' is required");
            return;
        }

        NetUserId userId;
        if (Guid.TryParse(body.User, out var guid))
        {
            userId = new NetUserId(guid);
        }
        else if (_playerManager.TryGetSessionByUsername(body.User, out var session))
        {
            userId = session.UserId;
        }
        else
        {
            await RespondError(context, ErrorCode.PlayerNotFound, HttpStatusCode.NotFound, $"Player '{body.User}' not found");
            return;
        }

        var actorStr = "DiscordBot";
        if (context.RequestHeaders.TryGetValue("Actor", out var actorHeader) && !string.IsNullOrEmpty(actorHeader))
        {
            try
            {
                var actor = System.Text.Json.JsonSerializer.Deserialize<Actor>(actorHeader.ToString());
                if (actor != null)
                    actorStr = FormatLogActor(actor);
            }
            catch
            {
                // Optional actor header
            }
        }

        await RunOnMainThread(async () =>
        {
            var nullLinkMgr = IoCManager.Resolve<INullLinkPlayerManager>();
            nullLinkMgr.LinkPlayerDiscord(userId, body.DiscordId, body.Roles);
            _sawmill.Info($"Linked user {userId} to Discord ID {body.DiscordId} with {body.Roles?.Count ?? 0} roles via API by {actorStr}.");
            await RespondOk(context);
        });
    }

    public sealed record DiscordLinkBody(
        [property: JsonPropertyName("user")] string User,
        [property: JsonPropertyName("discordId")] ulong DiscordId,
        [property: JsonPropertyName("roles")] List<ulong>? Roles
    );
}
