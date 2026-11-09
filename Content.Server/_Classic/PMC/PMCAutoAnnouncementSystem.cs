using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Utility;

namespace Content.Server._Classic.PMC;

/// <summary>
///     Handles automatically broadcasting an announcement for PMC 5 minutes after round start.
/// </summary>
public sealed partial class PMCAutoAnnouncementSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var roundDuration = _ticker.RoundDuration();

        var query = EntityQueryEnumerator<PMCAutoAnnouncementComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var announcement, out var gameRule))
        {
            if (announcement.Announced)
                continue;

            if (!_ticker.IsGameRuleActive(uid, gameRule))
                continue;

            if (roundDuration < announcement.Delay)
                continue;

            announcement.Announced = true;

            var stationName = "the station";
            if (TryComp<NukeopsRuleComponent>(uid, out var nukeops) && nukeops.TargetStation != null)
            {
                stationName = Name(nukeops.TargetStation.Value);
            }
            else
            {
                var stations = _station.GetStations();
                if (stations.Count > 0 && stations[0].Valid)
                    stationName = Name(stations[0]);
            }

            var sender = Loc.GetString(announcement.Sender);
            var message = Loc.GetString(announcement.Message, ("station", stationName));

            _chat.DispatchGlobalAnnouncement(
                message,
                sender: sender,
                playSound: true,
                colorOverride: announcement.Color);
        }
    }
}
