using Content.Shared._Classic.PMC;
using Content.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Server._Classic.PMC;

public sealed partial class OrbitalDesignatorSystem : SharedOrbitalDesignatorSystem
{
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    private EntityQuery<UseDelayComponent> _useDelayQuery;

    public override void Initialize()
    {
        base.Initialize();

        _useDelayQuery = GetEntityQuery<UseDelayComponent>();

        SubscribeLocalEvent<OrbitalDesignatorComponent, OrbitalDesignatorDoAfterEvent>(OnDoAfter);
    }

    private void OnDoAfter(Entity<OrbitalDesignatorComponent> ent, ref OrbitalDesignatorDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (_useDelayQuery.TryComp(ent, out var useDelay))
        {
            if (_useDelay.IsDelayed((ent.Owner, useDelay)))
                return;

            _useDelay.TryResetDelay((ent.Owner, useDelay));
        }

        var coords = GetCoordinates(args.TargetPosition);
        if (coords.IsValid(EntityManager))
        {
            Spawn(ent.Comp.MarkerPrototype, coords);
        }

        args.Handled = true;
    }
}
