using Content.Server._Classic.SupplyPods;
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Classic.SupplyPods;
using Content.Shared.Spawners.Components;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Classic.Spawning;

/// <summary>
/// Replaces the default late-join / arrivals spawning with supply pod delivery.
/// Players arrive on the station inside a falling supply pod instead of
/// teleporting to a late-join spawn point.
///
/// This system intercepts <see cref="PlayerSpawningEvent"/> with high priority
/// so it runs before the default <see cref="SpawnPointSystem"/>.
/// </summary>
public sealed partial class ClassicSupplyPodSpawningSystem : EntitySystem
{
    [Dependency] private readonly ClassicSupplyPodSystem _supplyPod = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        // High priority so we run before the default SpawnPointSystem handler.
        // The default handler only spawns if SpawnResult is still null.
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning, before: [typeof(SpawnPointSystem)]);
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // Only intercept late-join spawns, not round-start or observer
        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        // Find a valid spawn location: late-join spawn points on the station
        var coords = FindSpawnLocation(args.Station);
        if (coords == null)
            return;

        // Spawn the player mob at the location first
        var mob = _stationSpawning.SpawnPlayerMob(
            coords.Value,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);

        if (!mob.IsValid())
            return;

        // Deliver the player via supply pod. The pod will contain the player.
        // Use the Default visual (default_falling_animation), not Nanotrasen.
        _supplyPod.Deliver(
            coords.Value,
            new List<EntityUid> { mob },
            visual: ClassicSupplyPodVisual.Default,
            openOnLand: true,
            despawnTime: 0f);

        args.SpawnResult = mob;
    }

    /// <summary>
    /// Finds a suitable landing location for the supply pod.
    /// Uses existing late-join spawn points if available.
    /// </summary>
    private EntityCoordinates? FindSpawnLocation(EntityUid? station)
    {
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        var possiblePositions = new ValueList<EntityCoordinates>();

        while (points.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (spawnPoint.SpawnType != SpawnPointType.LateJoin)
                continue;

            if (station != null && _station.GetOwningStation(uid, xform) != station)
                continue;

            possiblePositions.Add(xform.Coordinates);
        }

        if (possiblePositions.Count == 0)
            return null;

        return _random.Pick(possiblePositions);
    }
}
