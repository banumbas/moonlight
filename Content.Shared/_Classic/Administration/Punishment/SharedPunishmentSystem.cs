using Content.Shared.Chat;
using Content.Shared.Emoting;
using Content.Shared.Examine;
using Content.Shared.Paper;

namespace Content.Shared._Classic.Administration.Punishment;

public abstract partial class SharedPunishmentSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PunishmentComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PunishmentComponent, PaperWriteAttemptEvent>(OnPaperWriteAttempt);
        SubscribeLocalEvent<PunishmentComponent, EmoteAttemptEvent>(OnEmoteAttempt);
    }

    private void OnExamined(Entity<PunishmentComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.MutedChannels != ChatChannel.None)
        {
            var channelsStr = ent.Comp.MutedChannels.ToString();
            args.PushMarkup(Loc.GetString("punishment-examine-muted-channels", ("channels", channelsStr)));
        }

        if (ent.Comp.PaperMuted)
        {
            args.PushMarkup(Loc.GetString("punishment-examine-paper-muted"));
        }

        if (ent.Comp.ForcedPacifism)
        {
            args.PushMarkup(Loc.GetString("punishment-examine-pacified"));
        }
    }

    private void OnPaperWriteAttempt(Entity<PunishmentComponent> ent, ref PaperWriteAttemptEvent args)
    {
        if (!ent.Comp.PaperMuted)
            return;

        args.Cancelled = true;
        args.FailReason = Loc.GetString("punishment-paper-write-blocked");
    }

    private void OnEmoteAttempt(Entity<PunishmentComponent> ent, ref EmoteAttemptEvent args)
    {
        if ((ent.Comp.MutedChannels & ChatChannel.Emotes) != 0)
            args.Cancel();
    }
}
