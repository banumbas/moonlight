using Content.Server.Administration.Managers;
using Content.Shared._Classic.Administration.Punishment;
using Content.Shared.Chat;
using Content.Shared.CombatMode.Pacification;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Classic.Administration.Punishment;

public sealed partial class PunishmentSystem : SharedPunishmentSystem
{
    [Dependency] private readonly IBanManager _ban = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextUpdateTime = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<RoleBansUpdatedEvent>(OnRoleBansUpdated);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        UpdatePunishments(args.Entity, args.Player.UserId);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (TryComp<PunishmentComponent>(args.Entity, out var comp))
            RemovePunishments(args.Entity, comp);
    }

    private void OnRoleBansUpdated(RoleBansUpdatedEvent args)
    {
        if (_player.TryGetSessionById(args.UserId, out var session) && session.AttachedEntity is { } entity)
        {
            UpdatePunishments(entity, args.UserId);
        }
    }

    public void UpdatePunishments(EntityUid uid, NetUserId userId)
    {
        var punishments = _ban.GetPunishments(userId);
        TryComp<PunishmentComponent>(uid, out var comp);

        if (punishments == null || punishments.Count == 0)
        {
            RemovePunishments(uid, comp);
            return;
        }

        var mutedChannels = ChatChannel.None;
        var paperMuted = false;
        var pacifism = false;

        foreach (var ban in punishments)
        {
            switch (ban.Role)
            {
                case "Punish:Mute:Local": mutedChannels |= ChatChannel.Local; break;
                case "Punish:Mute:Whisper": mutedChannels |= ChatChannel.Whisper; break;
                case "Punish:Mute:Radio": mutedChannels |= ChatChannel.Radio; break;
                case "Punish:Mute:LOOC": mutedChannels |= ChatChannel.LOOC; break;
                case "Punish:Mute:OOC": mutedChannels |= ChatChannel.OOC; break;
                case "Punish:Mute:Emotes": mutedChannels |= ChatChannel.Emotes; break;
                case "Punish:Mute:Dead": mutedChannels |= ChatChannel.Dead; break;
                case "Punish:Mute:Paper": paperMuted = true; break;
                case "Punish:Pacifism": pacifism = true; break;
            }
        }

        if (mutedChannels == ChatChannel.None && !paperMuted && !pacifism)
        {
            RemovePunishments(uid, comp);
            return;
        }

        comp ??= EnsureComp<PunishmentComponent>(uid);

        if (comp.MutedChannels == mutedChannels && comp.PaperMuted == paperMuted && comp.ForcedPacifism == pacifism)
            return;

        comp.MutedChannels = mutedChannels;
        comp.PaperMuted = paperMuted;

        if (pacifism && !comp.ForcedPacifism)
        {
            EnsureComp<PacifiedComponent>(uid);
        }
        else if (!pacifism && comp.ForcedPacifism)
        {
            RemComp<PacifiedComponent>(uid);
        }

        comp.ForcedPacifism = pacifism;
        Dirty(uid, comp);
    }

    private void RemovePunishments(EntityUid uid, PunishmentComponent? comp)
    {
        if (comp == null)
            return;

        if (comp.ForcedPacifism && HasComp<PacifiedComponent>(uid))
            RemComp<PacifiedComponent>(uid);

        RemComp<PunishmentComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdateTime)
            return;

        _nextUpdateTime = _timing.CurTime + UpdateInterval;

        // Query only entities that actually have a PunishmentComponent instead of polling all online players
        var query = EntityQueryEnumerator<PunishmentComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out _, out var actor))
        {
            UpdatePunishments(uid, actor.PlayerSession.UserId);
        }
    }
}
