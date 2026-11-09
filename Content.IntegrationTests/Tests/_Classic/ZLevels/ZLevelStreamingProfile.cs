using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Classic.Station;
using Content.Server._Classic.ZLevels.Core;
using Content.Server.GameTicking;
using Content.Shared._Classic.CCVar;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Tag;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Profiling;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

/// <summary>
/// Reproducible server profiling scenario; run explicitly in Release. It measures the real system
/// updates and allocations, without rendering or a wall-clock timing assertion on CI hardware.
/// </summary>
[TestFixture, Explicit]
[EnsureCVar(Side.Server, typeof(CVars), nameof(CVars.ProfEnabled), true)]
public sealed class ZLevelStreamingProfile : GameTest
{
    private static readonly ProtoId<GameMapPrototype> Colony = "ClassicClassic";
    private static readonly int[] ProfileDepths = [0, -1, -2, -3];
    private const int BiomeChunkSize = 8;
    private const int WarmupStableTicks = 12;
    private const int WarmupTimeoutTicks = 1200;

    [TestPrototypes]
    private const string ProfilePrototypes = @"
- type: entity
  id: ClassicStreamingProfileViewer
  components:
    - type: Physics
      bodyType: KinematicController
    - type: Fixtures
      fixtures:
        body:
          shape: !type:PhysShapeCircle
            radius: 0.35
          density: 1
          layer: [SmallMobLayer]
          mask: [SmallMobMask]
    - type: ClassicZPhysics
";

    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    [EnsureCVar(Side.Server, typeof(Content.Shared.CCVar.CCVars), nameof(Content.Shared.CCVar.CCVars.GridFill), false)]
    public async Task ProfileColonyStreaming()
    {
        // Keep comparisons reproducible even when the server's default changes.
        await OverrideCVar(Side.Server, ClassicCCVars.AtmosEnabled, true);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var transform = em.System<SharedTransformSystem>();
        var profiler = Server.ResolveDependency<ProfManager>();
        var sessions = await Server.AddDummySessions(1);
        EntityUid network = default;
        EntityUid viewer = default;
        EntityUid surface = default;
        EntityUid surfaceGrid = default;
        var terrainGrids = new Dictionary<int, EntityUid>();
        var profileOrigins = new Dictionary<int, Vector2>();
        var activatedDepths = new HashSet<int>();
        var samples = new Dictionary<string, List<TimeAndAllocSample>>();
        long cursor = 0;

        List<(int Depth, EntityUid GridUid, MapGridComponent Grid)> GetBiomeGrids(EntityUid networkUid)
        {
            var result = new List<(int, EntityUid, MapGridComponent)>();
            var seen = new HashSet<EntityUid>();
            var levels = em.GetComponent<ClassicZMapNetworkComponent>(networkUid);
            foreach (var (depth, level) in levels.ZLevels)
            {
                if (level is not { } mapUid)
                    continue;

                if (em.TryGetComponent<MapGridComponent>(mapUid, out var mapGrid) &&
                    em.HasComponent<BiomeComponent>(mapUid) &&
                    seen.Add(mapUid))
                {
                    result.Add((depth, mapUid, mapGrid));
                }

                var mapId = em.GetComponent<TransformComponent>(mapUid).MapID;
                foreach (var candidate in map.GetAllGrids(mapId))
                {
                    if (!em.HasComponent<BiomeComponent>(candidate.Owner) || !seen.Add(candidate.Owner))
                        continue;
                    result.Add((depth, candidate.Owner, candidate.Comp));
                }
            }

            return result;
        }

        async Task WarmDepth(int depth, Vector2 localPosition)
        {
            var gridUid = terrainGrids[depth];
            await Server.WaitPost(() =>
            {
                transform.SetCoordinates(viewer, new EntityCoordinates(gridUid, localPosition));
                Assert.That(em.GetComponent<TransformComponent>(viewer).ParentUid, Is.EqualTo(gridUid),
                    $"The profiling viewer was not parented to the terrain grid at depth {depth}.");
            });

            var stableTicks = 0;
            var previousLoaded = -1;
            var previousEntities = -1;
            var settled = false;
            var diagnostics = string.Empty;
            var ticks = 0;
            var started = Stopwatch.GetTimestamp();

            for (; ticks < WarmupTimeoutTicks && !settled; ticks++)
            {
                await Server.WaitRunTicks(1);
                await Server.WaitPost(() =>
                {
                    var biome = em.GetComponent<BiomeComponent>(gridUid);
                    var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(gridUid);
                    var grid = em.GetComponent<MapGridComponent>(gridUid);
                    var viewerXform = em.GetComponent<TransformComponent>(viewer);
                    var viewerTile = map.WorldToTile(gridUid, grid, transform.GetWorldPosition(viewerXform));
                    var viewerChunk = SharedMapSystem.GetChunkIndices(viewerTile, BiomeChunkSize) * BiomeChunkSize;
                    var loaded = biome.LoadedChunks.Count;
                    var entities = biome.LoadedEntities.Values.Sum(chunk => chunk.Count);
                    var noPartialWork = streaming.PartialLoads.Count == 0 && streaming.PartialUnloads.Count == 0;
                    var landingLoaded = loaded > 0;

                    if (noPartialWork && landingLoaded && loaded == previousLoaded && entities == previousEntities)
                        stableTicks++;
                    else
                        stableTicks = 0;

                    previousLoaded = loaded;
                    previousEntities = entities;
                    settled = stableTicks >= WarmupStableTicks;
                    diagnostics =
                        $"grid={gridUid} viewer_chunk={viewerChunk} loaded={loaded} entities={entities} " +
                        $"partial_loads={streaming.PartialLoads.Count} partial_load_cells={streaming.PartialLoads.Values.Sum()} " +
                        $"partial_unloads={streaming.PartialUnloads.Count} pending_unloads={streaming.PendingUnloads.Count} " +
                        $"stable_ticks={stableTicks}";
                });
            }

            Assert.That(settled, Is.True,
                $"Biome streaming at depth {depth} did not settle in {WarmupTimeoutTicks} ticks: {diagnostics}");

            if (depth == -1)
            {
                await Server.WaitPost(() =>
                {
                    var biome = em.GetComponent<BiomeComponent>(gridUid);
                    var rock = biome.LoadedEntities.Values.First(chunk => chunk.Count > 0).Keys.First();
                    var metadata = em.GetComponent<MetaDataComponent>(rock);
                    var prototype = metadata.EntityPrototype!;
                    var serializer = Server.ResolveDependency<ISerializationManager>();
                    TestContext.Out.WriteLine($"ROCK default={em.IsDefault(rock)} prototype={prototype.ID} components={em.GetComponents(rock).Count()} expected={prototype.Components.Count + 2}");
                    TestContext.Out.WriteLine($"ROCK tags={string.Join(',', em.GetComponent<TagComponent>(rock).Tags)} prototype_components={string.Join(',', prototype.Components.Keys)}");
                    TestContext.Out.WriteLine($"ROCK components={string.Join(',', em.GetComponents(rock).Select(comp => em.ComponentFactory.GetRegistration(comp.GetType()).Name))}");
                    foreach (var component in em.GetComponents(rock))
                    {
                        var type = component.GetType();
                        if (component is TransformComponent or MetaDataComponent)
                            continue;
                        var name = em.ComponentFactory.GetRegistration(type).Name;
                        if (!prototype.Components.TryGetValue(name, out var baseline))
                            TestContext.Out.WriteLine($"ROCK added={name}");
                        else if (!serializer.DataFieldEquals(type, component, baseline.Component))
                            TestContext.Out.WriteLine($"ROCK changed={name}");
                    }
                });
            }

            activatedDepths.Add(depth);
            TestContext.Out.WriteLine(
                $"ACTIVATION depth={depth} ticks={ticks} elapsed_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} {diagnostics}");
        }

        try
        {
            await Server.WaitPost(() =>
            {
                Server.ResolveDependency<IRobustRandom>().SetSeed(42);
                var prototype = Server.ProtoMan.Index(Colony);
                em.System<GameTicker>().LoadGameMap(prototype, out var mapId,
                    DeserializationOptions.Default with { InitializeMaps = true });
                surface = map.GetMap(mapId);
                network = em.GetComponent<ClassicZMapComponent>(surface).NetworkUid;
                var biomeGrids = GetBiomeGrids(network);
                Assert.That(biomeGrids.Select(entry => entry.Depth).Distinct(),
                    Is.EquivalentTo(ProfileDepths),
                    "The profiling map must expose a biome grid at every generated depth.");

                foreach (var depth in ProfileDepths)
                {
                    var candidates = biomeGrids.Where(entry => entry.Depth == depth).ToArray();
                    Assert.That(candidates, Is.Not.Empty, $"No biome terrain grid exists at depth {depth}.");
                    terrainGrids[depth] = candidates.MaxBy(entry => entry.Grid.ChunkCount).GridUid;
                }

                surfaceGrid = terrainGrids[0];
                var surfaceBounds = em.GetComponent<MapGridComponent>(surfaceGrid).LocalAABB;
                profileOrigins[0] = surfaceBounds.Center;
                for (var i = 1; i < ProfileDepths.Length; i++)
                    profileOrigins[ProfileDepths[i]] = new Vector2(1000.1f + i * 256f, 1000.1f);
                // Match a real mob's physical footprint. A fixtureless entity deliberately uses
                // the conservative 3x3 safety fallback and would make the cold-entry profile
                // measure a test artifact instead of normal player chunk loading.
                viewer = em.SpawnEntity("ClassicStreamingProfileViewer",
                    new EntityCoordinates(surfaceGrid, profileOrigins[0]));
                Server.PlayerMan.SetAttachedEntity(sessions[0], viewer);
                Assert.That(em.GetComponent<TransformComponent>(viewer).ParentUid, Is.EqualTo(surfaceGrid),
                    "The profiling viewer must begin parented to the real surface station grid.");
            });

            // Underground depths use separate cold coordinates. The surface begins inside the
            // actual station grid so GridTraversal cannot reparent the viewer to the bare map
            // before its biome has generated the leading chunks.
            foreach (var depth in ProfileDepths)
                await WarmDepth(depth, profileOrigins[depth]);

            Assert.That(activatedDepths, Is.EquivalentTo(ProfileDepths),
                "The profile must exercise the surface and all three generated underground levels.");

            // Return to the actual surface grid and wait for its range to settle before measuring
            // idle/boundary behavior. This also verifies that re-entry after deep traversal works.
            await WarmDepth(0, profileOrigins[0]);
            await Server.WaitPost(() =>
            {
                var grid = map.GetAllGrids(em.GetComponent<TransformComponent>(surface).MapID)
                    .MaxBy(candidate => candidate.Comp.ChunkCount);
                var roofSystem = em.System<SharedRoofSystem>();
                var roof = em.GetComponent<RoofComponent>(grid);
                var center = map.WorldToTile(grid, grid.Comp, profileOrigins[0]);
                var bounds = new Box2(center.X - 17, center.Y - 17, center.X + 17, center.Y + 17);
                var counts = new int[2];
                for (var mode = 0; mode < 2; mode++)
                {
                    var warmEntities = mode == 0 || roofSystem.HasRoofEntities(grid, bounds);
                    for (var x = center.X - 16; x < center.X + 16; x++)
                    for (var y = center.Y - 16; y < center.Y + 16; y++)
                        roofSystem.GetColor((grid.Owner, grid.Comp, roof), new Vector2i(x, y), warmEntities);

                    var start = Stopwatch.GetTimestamp();
                    for (var frame = 0; frame < 20; frame++)
                    {
                        var checkEntities = mode == 0 || roofSystem.HasRoofEntities(grid, bounds);
                        for (var x = center.X - 16; x < center.X + 16; x++)
                        for (var y = center.Y - 16; y < center.Y + 16; y++)
                        {
                            if (roofSystem.GetColor((grid.Owner, grid.Comp, roof), new Vector2i(x, y), checkEntities) != null)
                                counts[mode]++;
                        }
                    }
                    TestContext.Out.WriteLine($"ROOF_SCAN mode={mode} tiles=20480 elapsed_ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3}");
                }
                Assert.That(counts[1], Is.EqualTo(counts[0]), "Batched roof checks must retain the same rendered tile colors.");
            });
            foreach (var phase in new[] { "idle", "boundary", "exploration", "recovery", "cold-entry" })
            {
                var measuredTickTime = TimeSpan.Zero;
                var measuredTicks = 0;
                await Server.WaitPost(() =>
                {
                    samples.Clear();
                    cursor = profiler.Buffer.LogWriteOffset;
                });
                for (var step = 0; step < 40; step++)
                {
                    if (phase == "cold-entry" && step == 0)
                    {
                        // A direct entry into untouched dense -Z1 terrain captures the foreground
                        // safety batch and the first ordinary background chunks, rather than
                        // averaging that spike away inside an already warmed surface walk.
                        await Server.WaitPost(() => transform.SetCoordinates(
                            viewer,
                            new EntityCoordinates(terrainGrids[-1], new Vector2(2400.1f, 1400.1f))));
                    }
                    if (phase is "boundary" or "exploration")
                    {
                        var origin = profileOrigins[0];
                        var boundary = MathF.Floor(origin.X / BiomeChunkSize) * BiomeChunkSize + BiomeChunkSize;
                        var x = phase == "boundary"
                            ? boundary + (step % 2 == 0 ? -0.1f : 0.1f)
                            : origin.X + step * 2f;
                        await Server.WaitPost(() => transform.SetWorldPosition(viewer, new Vector2(x, origin.Y)));
                    }
                    var ticksStarted = Stopwatch.GetTimestamp();
                    await Server.WaitRunTicks(6);
                    measuredTickTime += Stopwatch.GetElapsedTime(ticksStarted);
                    measuredTicks += 6;
                    await Server.WaitPost(() =>
                    {
                        var buffer = profiler.Buffer;
                        for (var i = Math.Max(cursor, buffer.LogWriteOffset - buffer.LogBuffer.Length); i < buffer.LogWriteOffset; i++)
                        {
                            var entry = buffer.Log(i);
                            if (entry.Type != ProfLogType.Value || entry.Value.Value.Type != ProfValueType.TimeAllocSample)
                                continue;
                            var name = profiler.GetString(entry.Value.StringId);
                            if (!samples.TryGetValue(name, out var list))
                                samples[name] = list = new List<TimeAndAllocSample>();
                            list.Add(entry.Value.Value.TimeAllocSample);
                        }
                        cursor = buffer.LogWriteOffset;
                    });
                }

                foreach (var (name, values) in samples.OrderByDescending(pair => pair.Value.Sum(value => value.Time)).Take(12))
                {
                    var times = values.Select(value => value.Time * 1000d).Order().ToArray();
                    TestContext.Out.WriteLine($"PROFILE {phase} {name}: n={times.Length} mean_ms={times.Average():F3} p95_ms={times[(int) ((times.Length - 1) * .95)]:F3} max_ms={times[^1]:F3} alloc_KiB={values.Sum(value => value.Alloc) / 1024d:F1}");
                }
                // This is end-to-end test-harness wall time, useful for relative comparisons but
                // deliberately not presented as the production server tick rate.
                TestContext.Out.WriteLine(
                    $"HARNESS_TICK_WALLCLOCK {phase}: ticks={measuredTicks} avg_ms={measuredTickTime.TotalMilliseconds / measuredTicks:F3} throughput_tps={measuredTicks / measuredTickTime.TotalSeconds:F1}");
                await Server.WaitPost(() =>
                {
                    foreach (var (depth, grid) in terrainGrids.OrderBy(pair => pair.Key))
                    {
                        if (em.TryGetComponent<BiomeComponent>(grid, out var biome))
                        {
                            em.TryGetComponent<ClassicBiomeStreamingComponent>(grid, out var streaming);
                            TestContext.Out.WriteLine($"TERRAIN {phase} z={depth} chunks={biome.LoadedChunks.Count} partial={streaming?.PartialLoads.Count ?? 0} partial_cells={streaming?.PartialLoads.Values.Sum() ?? 0} priority_cells={streaming?.PriorityLoadedCells.Values.Sum(BitOperations.PopCount) ?? 0} pending_unloads={streaming?.PendingUnloads.Count ?? 0} entities={biome.LoadedEntities.Values.Sum(chunk => chunk.Count)} modified={biome.ModifiedTiles.Values.Sum(chunk => chunk.Count)}");
                        }
                    }
                });
            }
        }
        finally
        {
            await Server.RemoveDummySession(sessions[0]);
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }
}
