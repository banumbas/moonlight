using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Classic.Station;
using Content.Server._Classic.ZLevels.Core;
using Content.Server.Parallax;
using Content.Shared._Classic.Vehicles;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared._Classic.CCVar;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Content.Shared.Sprite;
using Content.Shared.Tag;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Temperature.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

// This regression fixture controls streaming budgets and inspects generated terrain directly.
#pragma warning disable RA0002

[TestFixture]
[EnsureCVar(Side.Server, typeof(ClassicCCVars), nameof(ClassicCCVars.AtmosEnabled), true)]
public sealed class BiomeStreamingTest : GameTest
{
    private static readonly ProtoId<BiomeTemplatePrototype> UndergroundBiome = "ClassicUndergroundDirtStone";

    [TestPrototypes]
    private const string MarkerPrototypes = @"
- type: biomeMarkerLayer
  id: ClassicStreamingMarkerNodeTest
  prototype: FloraRockSolid
  size: 8
  radius: 1
  maxCount: 5

- type: biomeMarkerLayer
  id: ClassicStreamingMarkerLifecycleTest
  prototype: ClassicStreamingMarkerLifecycleEntityTest
  size: 8
  radius: 4
  maxCount: 1

- type: biomeMarkerLayer
  id: ClassicStreamingCarpLifecycleTest
  prototype: MobCarpDungeon
  size: 8
  radius: 4
  maxCount: 1

- type: entity
  id: ClassicStreamingMarkerLifecycleEntityTest
  components:
    - type: Transform
      anchored: true

- type: biomeTemplate
  id: ClassicStreamingTileOnlyTest
  layers:
    - !type:BiomeTileLayer
      threshold: -1.0
      tile: ClassicDirt

- type: entity
  id: ClassicStreamingSpawnerTest
  parent: MarkerBase
  components:
    - type: RandomSpawner
      prototypes:
        - FloraTree
      offset: 0

- type: biomeTemplate
  id: ClassicStreamingSpawnerTest
  layers:
    - !type:BiomeEntityLayer
      threshold: -1.0
      allowedTiles:
        - ClassicDirt
      entities:
        - ClassicStreamingSpawnerTest
    - !type:BiomeTileLayer
      threshold: -1.0
      tile: ClassicDirt

- type: entity
  id: ClassicStreamingPersistentSpawnerSourceTest
  parent: MarkerBase
  components:
    - type: RandomSpawner
      prototypes:
        - ClassicStreamingUnanchoredOutputTest
      offset: 0.1
      deleteSpawnerAfterSpawn: false

- type: entity
  id: ClassicStreamingUnanchoredOutputTest
  components:
    - type: Transform
      anchored: false

- type: entity
  id: ClassicStreamingTimedAnchoredEffectTest
  components:
    - type: Transform
      anchored: true
    - type: TimedDespawn
      lifetime: 60

- type: biomeTemplate
  id: ClassicStreamingPersistentSpawnerTest
  layers:
    - !type:BiomeEntityLayer
      threshold: -1.0
      allowedTiles:
        - ClassicDirt
      entities:
        - ClassicStreamingPersistentSpawnerSourceTest
    - !type:BiomeTileLayer
      threshold: -1.0
      tile: ClassicDirt

- type: entity
  id: ClassicStreamingUnanchoredRootTest
  components:
    - type: Transform
      anchored: false

- type: biomeTemplate
  id: ClassicStreamingUnanchoredRootTest
  layers:
    - !type:BiomeEntityLayer
      threshold: -1.0
      allowedTiles:
        - ClassicDirt
      entities:
        - ClassicStreamingUnanchoredRootTest
    - !type:BiomeTileLayer
      threshold: -1.0
      tile: ClassicDirt

- type: entity
  id: ClassicStreamingPhysicalViewerTest
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

    public override PoolSettings PoolSettings => new() { Connected = true };

    [Test]
    public async Task BiomeRangeMatchesScaledPriorityPvsHalfSide()
    {
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetMaxUpdateRange, 32f);
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetPvsPriorityRange, 32f);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid mapUid = default;

        try
        {
            await Server.WaitPost(() =>
            {
                mapUid = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    mapUid,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(mapUid);
                streaming.BackgroundChunksPerTick = 64;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                streaming.UnloadDelay = TimeSpan.FromHours(1);
                map.InitializeMap(mapUid);

                var viewer = em.SpawnEntity(null, new EntityCoordinates(mapUid, new Vector2(4.5f, 4.5f)));
                em.EnsureComponent<EyeComponent>(viewer).PvsScale = 1.5f;
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(mapUid);
                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks.Count, Is.EqualTo(49),
                        "A 32m PVS side scaled by 1.5 is a 24m radius (seven 8m chunks here), not the old 64m radius.");
                    Assert.That(streaming.PartialLoads, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (mapUid != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(mapUid));
        }
    }

    [Test]
    public async Task RenderOnlyCameraCannotForceUrgentBiomeWalls()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var subscribers = em.System<ViewSubscriberSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid camera = default;
        EntityUid safeMap = default;
        EntityUid player = default;

        try
        {
            await Server.WaitPost(() =>
            {
                safeMap = map.CreateMap();
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(terrain, Server.ProtoMan.Index(UndergroundBiome), 42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.WorkBudget = TimeSpan.Zero;
                streaming.UnloadDelay = TimeSpan.FromHours(1);
                map.InitializeMap(terrain);

                player = em.SpawnEntity(null, new EntityCoordinates(safeMap, Vector2.Zero));
                Server.PlayerMan.SetAttachedEntity(session, player);
                camera = em.SpawnEntity(null, new EntityCoordinates(terrain, new Vector2(1000.5f, 1000.5f)));
                subscribers.AddViewSubscriber(camera, session);

                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                Assert.Multiple(() =>
                {
                    Assert.That(streaming.ViewerCells, Is.Empty,
                        "A render-only camera must not enter the pre-physics safety lane.");
                    Assert.That(streaming.PriorityLoadedCells, Is.Empty);
                    Assert.That(streaming.PartialLoads, Is.Empty,
                        "A zero background budget must prevent a camera from spawning even one wall.");
                    Assert.That(biome.LoadedEntities, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (camera != EntityUid.Invalid)
                    subscribers.RemoveViewSubscriber(camera, session);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
            });
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
            if (safeMap != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(safeMap));
        }
    }

    [Test]
    public async Task PhysicalViewerUsesSweptFixtureFootprintInsteadOfFixedSquare()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var physics = em.System<SharedPhysicsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.WorkBudget = TimeSpan.Zero;
                streaming.UnloadDelay = TimeSpan.FromHours(1);
                map.InitializeMap(terrain);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.ViewerCells, Is.EquivalentTo(new[] { new Vector2i(4, 4) }),
                        "A stationary 0.35m body must not synchronously generate the old 3x3 wall square.");
                    Assert.That(BitOperations.PopCount(streaming.PriorityLoadedCells[Vector2i.Zero]), Is.EqualTo(1));
                });

                physics.SetLinearVelocity(
                    viewer,
                    new Vector2(4.5f, 0f),
                    body: em.GetComponent<PhysicsComponent>(viewer));
                biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.ViewerCells,
                        Is.EquivalentTo(new[] { new Vector2i(4, 4), new Vector2i(5, 4) }),
                        "The two-tick sweep must generate only the current and forward cells.");
                    Assert.That(BitOperations.PopCount(streaming.PriorityLoadedCells[Vector2i.Zero]), Is.EqualTo(2));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task AdjacentLandingCellsRequireReachableZDirection()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var above = map.CreateMap(runMapInit: false);
                var current = map.CreateMap(runMapInit: false);
                var below = map.CreateMap(runMapInit: false);
                var tileOnly = Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest");
                foreach (var terrain in new[] { above, current, below })
                {
                    biomes.EnsurePlanet(terrain, tileOnly, 42);
                    var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                    streaming.BackgroundChunksPerTick = 0;
                    streaming.WorkBudget = TimeSpan.Zero;
                    streaming.UnloadDelay = TimeSpan.FromHours(1);
                }

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [above] = 1,
                    [current] = 0,
                    [below] = -1,
                }), Is.True);
                map.InitializeMap(above);
                map.InitializeMap(current);
                map.InitializeMap(below);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(current, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var aboveStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(above);
                var belowStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(below);
                var expectedPrefetch = new[]
                {
                    new Vector2i(0, 0),
                    new Vector2i(0, 8),
                    new Vector2i(8, 0),
                    new Vector2i(8, 8),
                };
                Assert.Multiple(() =>
                {
                    Assert.That(aboveStreaming.PartialLoads, Is.Empty,
                        "Adjacent support prefetch must stay inside the background lane.");
                    Assert.That(belowStreaming.PartialLoads, Is.Empty,
                        "Adjacent support prefetch must stay inside the background lane.");
                    Assert.That(aboveStreaming.AdjacentPrefetchChunks, Is.EquivalentTo(expectedPrefetch));
                    Assert.That(belowStreaming.AdjacentPrefetchChunks, Is.EquivalentTo(expectedPrefetch));
                    Assert.That(aboveStreaming.ViewerCells, Is.Empty);
                    Assert.That(belowStreaming.ViewerCells, Is.Empty);
                });

                var zPhysics = em.GetComponent<ClassicZPhysicsComponent>(viewer);
                zPhysics.Velocity = -1f;
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(belowStreaming.PartialLoads, Does.ContainKey(Vector2i.Zero));
                    Assert.That(belowStreaming.PartialLoads[Vector2i.Zero], Is.Zero);
                    Assert.That(BitOperations.PopCount(belowStreaming.PriorityLoadedCells[Vector2i.Zero]),
                        Is.EqualTo(1));
                    Assert.That(BitOperations.PopCount(belowStreaming.MaterializedCells[Vector2i.Zero]),
                        Is.EqualTo(1));
                    Assert.That(em.GetComponent<BiomeComponent>(below).LoadedChunks, Is.Empty);
                    Assert.That(aboveStreaming.PartialLoads, Is.Empty);
                });

                zPhysics.Velocity = 1f;
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(aboveStreaming.PartialLoads, Does.ContainKey(Vector2i.Zero));
                    Assert.That(BitOperations.PopCount(aboveStreaming.PriorityLoadedCells[Vector2i.Zero]),
                        Is.EqualTo(1));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task VehicleInteriorPreparesExteriorExitBeforeTeleport()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        EntityUid interior = default;
        EntityUid upper = default;
        EntityUid upperOne = default;
        EntityUid upperTwo = default;
        EntityUid upperThree = default;
        EntityUid lower = default;
        EntityUid vehicle = default;
        EntityUid viewer = default;
        EntityUid groundedViewer = default;
        EntityUid exit = default;

        try
        {
            await Server.WaitPost(() =>
            {
                upper = map.CreateMap(runMapInit: false);
                upperOne = map.CreateMap(runMapInit: false);
                upperTwo = map.CreateMap(runMapInit: false);
                upperThree = map.CreateMap(runMapInit: false);
                lower = map.CreateMap(runMapInit: false);
                interior = map.CreateMap(runMapInit: false);
                var tileOnly = Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest");
                var terrains = new[] { upperThree, upperTwo, upperOne, upper, lower };
                for (var i = 0; i < terrains.Length; i++)
                    biomes.EnsurePlanet(terrains[i], tileOnly, 24 + i);
                var streamings = terrains
                    .Select(uid => em.EnsureComponent<ClassicBiomeStreamingComponent>(uid))
                    .ToArray();
                foreach (var streaming in streamings)
                {
                    streaming.BackgroundChunksPerTick = 0;
                    streaming.WorkBudget = TimeSpan.Zero;
                    streaming.UnloadDelay = TimeSpan.FromHours(1);
                }

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [upperThree] = 3,
                    [upperTwo] = 2,
                    [upperOne] = 1,
                    [upper] = 0,
                    [lower] = -1,
                }), Is.True);
                foreach (var terrain in terrains)
                    map.InitializeMap(terrain);
                map.InitializeMap(interior);

                vehicle = em.SpawnEntity(null, new EntityCoordinates(upper, new Vector2(4.5f, 4.5f)));
                var enter = em.EnsureComponent<VehicleEnterComponent>(vehicle);
                enter.EntryPoints.Add(new VehicleEntryPoint { Offset = new Vector2(0f, 2f) });
                enter.ExitOffset = new Vector2(2f, 1f);
                enter.ExitDoAfter = 0f;
                var link = em.EnsureComponent<VehicleInteriorLinkComponent>(interior);
                link.Vehicle = vehicle;

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(interior, new Vector2(1.5f, 1.5f)));
                em.EnsureComponent<DoAfterComponent>(viewer);
                exit = em.SpawnEntity(null, new EntityCoordinates(interior, new Vector2(1.5f, 2.5f)));
                em.EnsureComponent<VehicleExitComponent>(exit).EntryIndex = int.MaxValue;
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                em.EnsureComponent<VehicleInteriorOccupantComponent>(viewer).Vehicle = vehicle;

                biomes.Update(0);
                var upperStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(upper);
                var lowerStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(lower);
                Assert.That(upperStreaming.ViewerCells, Does.Contain(new Vector2i(6, 5)));
                var expectedUpperPrefetch = new[]
                {
                    new Vector2i(0, 0),
                    new Vector2i(0, 8),
                    new Vector2i(8, 0),
                    new Vector2i(8, 8),
                };
                foreach (var terrain in new[] { upperOne, upperTwo, upperThree })
                {
                    Assert.That(em.GetComponent<ClassicBiomeStreamingComponent>(terrain).AdjacentPrefetchChunks,
                        Is.EquivalentTo(expectedUpperPrefetch));
                }

                transform.SetWorldPosition(vehicle, new Vector2(84.5f, 84.5f));
                var exitTile = new Vector2i(86, 85);
                var exitChunk = new Vector2i(80, 80);
                Assert.That(upperStreaming.MaterializedCells, Does.Not.ContainKey(exitChunk));
                Assert.That(lowerStreaming.MaterializedCells, Does.Not.ContainKey(exitChunk));

                var zPhysics = em.GetComponent<ClassicZPhysicsComponent>(viewer);
                zPhysics.LocalPosition = 0.25f;
                zPhysics.Velocity = 0.2f;
                zPhysics.VelocityGravity = false;
                zPhysics.Fallable = false;
                zPhysics.CurrentZLevel = 99;

                var activate = new ActivateInWorldEvent(viewer, exit, false);
                em.EventBus.RaiseLocalEvent(exit, activate);
                Assert.That(activate.Handled, Is.True);
            });

            await Server.WaitRunTicks(2);

            await Server.WaitPost(() =>
            {
                var xform = em.GetComponent<TransformComponent>(viewer);
                var zPhysics = em.GetComponent<ClassicZPhysicsComponent>(viewer);
                var upperStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(upper);
                var lowerStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(lower);
                var exitTile = new Vector2i(86, 85);
                var exitChunk = new Vector2i(80, 80);
                var local = exitTile - exitChunk;
                var exitBit = 1UL << (local.X * 8 + local.Y);
                Assert.Multiple(() =>
                {
                    Assert.That(xform.MapUid, Is.EqualTo(upper));
                    Assert.That(transform.GetWorldPosition(xform), Is.EqualTo(new Vector2(86.5f, 85.5f)));
                    Assert.That(zPhysics.CurrentZLevel, Is.Zero);
                    Assert.That(zPhysics.CachedTile, Is.EqualTo(exitTile));
                    Assert.That(zPhysics.CachedGroundHeight, Is.Zero);
                    Assert.That(zPhysics.LocalPosition, Is.GreaterThan(0f));
                    Assert.That(zPhysics.Velocity, Is.EqualTo(0.2f));
                    Assert.That(em.HasComponent<VehicleInteriorOccupantComponent>(viewer), Is.False);
                    Assert.That(em.GetComponent<MetaDataComponent>(viewer).Flags & MetaDataFlags.ExtraTransformEvents,
                        Is.Not.Zero);
                    Assert.That(upperStreaming.MaterializedCells.GetValueOrDefault(exitChunk) & exitBit,
                        Is.Not.Zero);
                    Assert.That(lowerStreaming.MaterializedCells.GetValueOrDefault(exitChunk) & exitBit,
                        Is.Not.Zero);
                });

                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.That(viewerComp.Eyes.Select(eye => em.GetComponent<TransformComponent>(eye).MapUid),
                    Does.Contain(lower));
                transform.SetMapCoordinates(
                    viewer,
                    new MapCoordinates(new Vector2(86.5f, 85.5f), em.GetComponent<MapComponent>(lower).MapId));
                Assert.That(viewerComp.Eyes, Is.Empty);
                transform.SetMapCoordinates(
                    viewer,
                    new MapCoordinates(new Vector2(90.5f, 90.5f), em.GetComponent<MapComponent>(upper).MapId));
                Assert.That(viewerComp.Eyes.Select(eye => em.GetComponent<TransformComponent>(eye).MapUid),
                    Does.Contain(lower));

                groundedViewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(interior, new Vector2(1.5f, 1.5f)));
                em.EnsureComponent<DoAfterComponent>(groundedViewer);
                em.EnsureComponent<VehicleInteriorOccupantComponent>(groundedViewer).Vehicle = vehicle;
                Server.PlayerMan.SetAttachedEntity(session, groundedViewer);
                var groundedPhysics = em.GetComponent<ClassicZPhysicsComponent>(groundedViewer);
                Assert.Multiple(() =>
                {
                    Assert.That(groundedPhysics.Fallable, Is.True);
                    Assert.That(groundedPhysics.VelocityGravity, Is.True);
                });
                var activate = new ActivateInWorldEvent(groundedViewer, exit, false);
                em.EventBus.RaiseLocalEvent(exit, activate);
                Assert.That(activate.Handled, Is.True);
            });

            await Server.WaitRunTicks(8);

            await Server.WaitPost(() =>
            {
                var groundedXform = em.GetComponent<TransformComponent>(groundedViewer);
                var groundedPhysics = em.GetComponent<ClassicZPhysicsComponent>(groundedViewer);
                Assert.Multiple(() =>
                {
                    Assert.That(groundedXform.MapUid, Is.EqualTo(upper));
                    Assert.That(groundedPhysics.CurrentZLevel, Is.Zero);
                    Assert.That(groundedPhysics.CachedTile, Is.EqualTo(new Vector2i(86, 85)));
                    Assert.That(groundedPhysics.CachedGroundHeight, Is.Zero);
                    Assert.That(groundedPhysics.LocalPosition, Is.GreaterThanOrEqualTo(0f));
                });

                transform.SetWorldPosition(vehicle, new Vector2(164.5f, 164.5f));
                var ghost = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(interior, new Vector2(1.5f, 1.5f)));
                em.EnsureComponent<GhostComponent>(ghost);
                em.EnsureComponent<VehicleInteriorOccupantComponent>(ghost).Vehicle = vehicle;
                var ghostPhysics = em.GetComponent<ClassicZPhysicsComponent>(ghost);
                ghostPhysics.CurrentZLevel = 99;
                ghostPhysics.CachedTile = new Vector2i(999, 999);
                ghostPhysics.CachedGroundHeight = 7f;
                var ghostChunk = new Vector2i(160, 160);
                var upperStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(upper);
                var lowerStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(lower);
                Assert.That(upperStreaming.MaterializedCells, Does.Not.ContainKey(ghostChunk));
                Assert.That(lowerStreaming.MaterializedCells, Does.Not.ContainKey(ghostChunk));

                var ghostActivate = new ActivateInWorldEvent(ghost, exit, false);
                em.EventBus.RaiseLocalEvent(exit, ghostActivate);
                Assert.Multiple(() =>
                {
                    Assert.That(ghostActivate.Handled, Is.True);
                    Assert.That(em.GetComponent<TransformComponent>(ghost).MapUid, Is.EqualTo(upper));
                    Assert.That(ghostPhysics.CurrentZLevel, Is.EqualTo(99));
                    Assert.That(ghostPhysics.CachedTile, Is.EqualTo(new Vector2i(999, 999)));
                    Assert.That(ghostPhysics.CachedGroundHeight, Is.EqualTo(7f));
                    Assert.That(upperStreaming.MaterializedCells, Does.Not.ContainKey(ghostChunk));
                    Assert.That(lowerStreaming.MaterializedCells, Does.Not.ContainKey(ghostChunk));
                });

                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                var streamingStates = new[]
                {
                    (Biome: em.GetComponent<BiomeComponent>(upper), Streaming: upperStreaming),
                    (Biome: em.GetComponent<BiomeComponent>(lower), Streaming: lowerStreaming),
                };
                foreach (var (_, streaming) in streamingStates)
                {
                    streaming.BackgroundChunksPerTick = 64;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.UnloadEntitiesPerSlice = 64;
                    streaming.UnloadEntitiesPerTick = 1024;
                    streaming.UnloadCellsPerSlice = 64;
                    streaming.UnloadDelay = TimeSpan.Zero;
                    streaming.ZLevelCacheDuration = TimeSpan.Zero;
                    streaming.AdjacentPrefetchCacheDuration = TimeSpan.Zero;
                    streaming.WorkBudget = TimeSpan.FromSeconds(1);
                }

                var distantPosition = new Vector2(500.5f, 500.5f);
                var distantChunk = new Vector2i(496, 496);
                Assert.That(streamingStates.All(state =>
                    !state.Streaming.PreviousActiveChunks.Contains(distantChunk)), Is.True);
                em.EventBus.RaiseEvent(
                    EventSource.Local,
                    new ClassicVehiclePrepareExitEvent(new MapCoordinates(
                        distantPosition,
                        em.GetComponent<MapComponent>(upper).MapId)));

                foreach (var (_, streaming) in streamingStates)
                {
                    Assert.That(streaming.PartialLoads, Does.ContainKey(distantChunk));
                    Assert.That(streaming.PendingUnloads, Does.ContainKey(distantChunk));
                    Assert.That(streaming.MaterializedCells, Does.ContainKey(distantChunk));
                }

                for (var i = 0; i < 8; i++)
                    biomes.Update(0);

                foreach (var (biome, streaming) in streamingStates)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(biome.LoadedChunks, Does.Not.Contain(distantChunk));
                        Assert.That(biome.LoadedEntities, Does.Not.ContainKey(distantChunk));
                        Assert.That(biome.LoadedDecals, Does.Not.ContainKey(distantChunk));
                        Assert.That(streaming.PartialLoads, Does.Not.ContainKey(distantChunk));
                        Assert.That(streaming.PartialUnloads, Does.Not.ContainKey(distantChunk));
                        Assert.That(streaming.PendingUnloads, Does.Not.ContainKey(distantChunk));
                        Assert.That(streaming.PriorityLoadedCells, Does.Not.ContainKey(distantChunk));
                        Assert.That(streaming.MaterializedCells, Does.Not.ContainKey(distantChunk));
                    });
                }
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
            if (interior != EntityUid.Invalid && em.EntityExists(interior))
                await Server.WaitPost(() => em.DeleteEntity(interior));
        }
    }

    [Test]
    public async Task OpaqueCurrentLevelPrewarmsBoundedLowerBiome()
    {
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetMaxUpdateRange, 32f);
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetPvsPriorityRange, 32f);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var current = map.CreateMap(runMapInit: false);
                var lower = map.CreateMap(runMapInit: false);
                var tileOnly = Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest");
                biomes.EnsurePlanet(current, tileOnly, 24);
                biomes.EnsurePlanet(lower, tileOnly, 42);
                foreach (var terrain in new[] { current, lower })
                {
                    var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                    streaming.BackgroundChunksPerTick = 64;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.WorkBudget = TimeSpan.FromSeconds(1);
                    streaming.UnloadDelay = TimeSpan.FromHours(1);
                }

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [current] = 0,
                    [lower] = -1,
                });
                map.InitializeMap(current);
                map.InitializeMap(lower);

                // Exercise an exact PVS/chunk boundary: cache padding must not interpret an
                // unmaterialized neighbouring cache chunk as an opening.
                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(current, Vector2.Zero));
                Server.PlayerMan.SetAttachedEntity(session, viewer);

                biomes.Update(0);
                biomes.Update(0);
                for (var i = 0; i < 8; i++)
                    biomes.Update(0);
                var currentBiome = em.GetComponent<BiomeComponent>(current);
                var lowerBiome = em.GetComponent<BiomeComponent>(lower);
                var lowerStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(lower);
                var expectedPrefetch = new HashSet<Vector2i>
                {
                    new(-8, -8),
                    new(-8, 0),
                    new(0, -8),
                    new(0, 0),
                };
                Assert.Multiple(() =>
                {
                    Assert.That(lowerBiome.LoadedChunks, Is.EquivalentTo(expectedPrefetch));
                    Assert.That(lowerStreaming.PartialLoads, Is.Empty,
                        "The bounded lower prefetch must finish in the background.");
                    Assert.That(lowerStreaming.PriorityLoadedCells, Is.Empty);
                    Assert.That(lowerStreaming.AdjacentPrefetchChunks, Is.EquivalentTo(expectedPrefetch));
                });

                var currentGrid = em.GetComponent<MapGridComponent>(current);
                Assert.That(map.TryGetTileRef(current, currentGrid, Vector2i.Zero, out var opaqueTile), Is.True);

                // Two writes may share LastTileModifiedTick. Exact event invalidation must see
                // that the opening was closed again instead of exposing the lower viewport.
                map.SetTile(current, currentGrid, Vector2i.Zero, Tile.Empty);
                map.SetTile(current, currentGrid, Vector2i.Zero, opaqueTile.Tile);
                biomes.Update(0);
                Assert.That(lowerBiome.LoadedChunks, Is.EquivalentTo(expectedPrefetch));

                // A real opening invalidates the cache. The opening is a visibility gate, not a
                // generation clip: once the lower eye is visible its complete PVS range must be
                // streamed instead of leaving a single biome chunk surrounded by void.
                var currentStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(current);
                currentStreaming.WorkBudget = TimeSpan.FromTicks(1);
                map.SetTile(current, currentGrid, Vector2i.Zero, Tile.Empty);
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(lowerBiome.LoadedChunks, Is.EquivalentTo(expectedPrefetch),
                        "An incomplete opening snapshot must expose only the bounded lower prefetch.");
                    Assert.That(lowerStreaming.PartialLoads, Is.Empty);
                    Assert.That(lowerStreaming.PriorityLoadedCells, Is.Empty);
                    Assert.That(currentStreaming.OpeningScans.Values.Any(scan =>
                            scan.Phase != ClassicBiomeOpeningScanPhase.Complete),
                        Is.True,
                        "A full PVS opening snapshot must be resumed instead of scanned atomically.");
                });

                const int expectedLowerChunks = 25;
                for (var i = 0; i < 128 && lowerBiome.LoadedChunks.Count != expectedLowerChunks; i++)
                    biomes.Update(0);

                var currentTileIsEmpty = map.TryGetTileRef(
                    current,
                    currentGrid,
                    Vector2i.Zero,
                    out var currentTile) && currentTile.Tile.IsEmpty;
                Assert.That(lowerBiome.LoadedChunks.Count, Is.EqualTo(expectedLowerChunks),
                    $"current_chunks={currentBiome.LoadedChunks.Count} current_partial={currentStreaming.PartialLoads.Count} " +
                    $"current_tile_empty={currentTileIsEmpty} lower_partial={lowerStreaming.PartialLoads.Count} " +
                    $"opening_scans=[{string.Join(';', currentStreaming.OpeningScans.Values.Select(scan => $"{scan.Phase}:{scan.NextChunk}/{scan.NextOpeningChunk}"))}]");
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task MovingOpeningViewportReusesChunkMasks()
    {
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetMaxUpdateRange, 32f);
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetPvsPriorityRange, 32f);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var current = map.CreateMap(runMapInit: false);
                var lower = map.CreateMap(runMapInit: false);
                var tileOnly = Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest");
                biomes.EnsurePlanet(current, tileOnly, 24);
                biomes.EnsurePlanet(lower, tileOnly, 42);
                foreach (var terrain in new[] { current, lower })
                {
                    var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                    streaming.BackgroundChunksPerTick = 64;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.WorkBudget = TimeSpan.FromSeconds(1);
                    streaming.UnloadDelay = TimeSpan.Zero;
                    streaming.MaxOpeningCacheEntries = 8;
                }

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [current] = 0,
                    [lower] = -1,
                }), Is.True);
                map.InitializeMap(current);
                map.InitializeMap(lower);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(current, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var currentGrid = em.GetComponent<MapGridComponent>(current);
                for (var i = 0; i < 64 && em.GetComponent<BiomeComponent>(current).LoadedChunks.Count < 25; i++)
                    biomes.Update(0);
                map.SetTile(current, currentGrid, new Vector2i(4, 4), Tile.Empty);

                var lowerBiome = em.GetComponent<BiomeComponent>(lower);
                var currentStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(current);
                for (var i = 0; i < 64 && lowerBiome.LoadedChunks.Count < 25; i++)
                    biomes.Update(0);
                Assert.That(lowerBiome.LoadedChunks.Count, Is.EqualTo(25));

                for (var i = 0; i < 7; i++)
                {
                    transform.SetWorldPosition(viewer, new Vector2(5.5f + i, 4.5f));
                    biomes.Update(0);
                    Assert.Multiple(() =>
                    {
                        Assert.That(currentStreaming.OpeningScans.Count, Is.LessThanOrEqualTo(2), $"step={i}");
                        Assert.That(lowerBiome.LoadedChunks.Count, Is.GreaterThanOrEqualTo(25), $"step={i}");
                        Assert.That(em.GetComponent<ClassicBiomeStreamingComponent>(lower).PartialUnloads,
                            Is.Empty, $"step={i}");
                    });
                }
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task OpeningScansOnlyResetForIntersectingTileChanges()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                map.InitializeMap(terrain);

                var nearFirst = Vector2i.Zero;
                var nearLast = new Vector2i(7, 7);
                var nearKey = new ClassicBiomeOpeningScanKey(
                    Vector2i.Zero,
                    Vector2i.Zero,
                    nearFirst,
                    nearLast);
                var nearScan = new ClassicBiomeOpeningScanState(
                    Vector2i.Zero,
                    Vector2i.Zero,
                    nearFirst,
                    nearLast,
                    0,
                    1,
                    TimeSpan.Zero);
                var farFirst = new Vector2i(80, 80);
                var farLast = new Vector2i(87, 87);
                var farKey = new ClassicBiomeOpeningScanKey(
                    new Vector2i(10, 10),
                    new Vector2i(10, 10),
                    farFirst,
                    farLast);
                var farScan = new ClassicBiomeOpeningScanState(
                    new Vector2i(10, 10),
                    new Vector2i(10, 10),
                    farFirst,
                    farLast,
                    0,
                    1,
                    TimeSpan.Zero);
                streaming.OpeningScans.Add(nearKey, nearScan);
                streaming.OpeningScans.Add(farKey, farScan);

                var grid = em.GetComponent<MapGridComponent>(terrain);
                var plating = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                map.SetTile(terrain, grid, new Vector2i(1, 1), plating);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.OpeningScans, Does.Not.ContainKey(nearKey));
                    Assert.That(streaming.OpeningScans, Does.ContainKey(farKey));
                    Assert.That(farScan.Revision, Is.GreaterThan(0));
                });

                map.SetTile(terrain, grid, new Vector2i(81, 81), plating);
                Assert.That(streaming.OpeningScans, Does.Not.ContainKey(farKey));
            });
        }
        finally
        {
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task MissingOpeningChunkDoesNotConsumeForcedOpeningProgress()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.WorkBudget = TimeSpan.Zero;
                streaming.OpeningCellsPerSlice = 1;
                streaming.MaxOpeningCacheEntries = 1;
                map.InitializeMap(terrain);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                biome.LoadedChunks.Add(Vector2i.Zero);

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BiomeSystem).GetMethod("BeginClassicStreamingBudget", flags)!.Invoke(biomes, null);
                var method = typeof(BiomeSystem).GetMethod("TryGetClassicOpeningWorldBounds", flags)!;
                var missingArgs = new object[]
                {
                    terrain,
                    biome,
                    grid,
                    new Box2(80f, 80f, 81f, 81f),
                    default(Box2),
                };
                Assert.That(method.Invoke(biomes, missingArgs), Is.False);

                var readyArgs = new object[]
                {
                    terrain,
                    biome,
                    grid,
                    new Box2(0f, 0f, 1f, 1f),
                    default(Box2),
                };
                Assert.That(method.Invoke(biomes, readyArgs), Is.False);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.OpeningScans.Count, Is.EqualTo(1));
                    Assert.That(streaming.OpeningScans.Values.Any(scan =>
                        scan.RequiredFirstChunk == new Vector2i(10, 10)), Is.False);
                    Assert.That(streaming.OpeningScans.Values.Single(scan =>
                            scan.RequiredFirstChunk == Vector2i.Zero).Phase,
                        Is.EqualTo(ClassicBiomeOpeningScanPhase.Scanning));
                });

                var readyScan = streaming.OpeningScans.Values.Single();
                for (var i = 0; i < 8 && readyScan.Phase != ClassicBiomeOpeningScanPhase.Complete; i++)
                {
                    typeof(BiomeSystem).GetMethod("BeginClassicStreamingBudget", flags)!.Invoke(biomes, null);
                    method.Invoke(biomes, readyArgs);
                }

                method.Invoke(biomes, missingArgs);
                Assert.Multiple(() =>
                {
                    Assert.That(readyScan.Phase, Is.EqualTo(ClassicBiomeOpeningScanPhase.Complete));
                    Assert.That(streaming.OpeningScans.Count, Is.EqualTo(1));
                    Assert.That(streaming.OpeningScans.Values.Single(), Is.SameAs(readyScan));
                });
            });
        }
        finally
        {
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task OpeningScanCacheCapsCurrentGeneration()
    {
        var biomes = Server.EntMan.System<BiomeSystem>();

        await Server.WaitPost(() =>
        {
            var streaming = new ClassicBiomeStreamingComponent
            {
                MaxOpeningCacheEntries = 1,
                OpeningScanGeneration = 7,
            };
            var first = Vector2i.Zero;
            var second = new Vector2i(1, 0);
            var firstKey = new ClassicBiomeOpeningScanKey(first, first, first, first);
            var firstScan = new ClassicBiomeOpeningScanState(first, first, first, first, 0, 7, TimeSpan.Zero)
            {
                Phase = ClassicBiomeOpeningScanPhase.Scanning,
            };
            firstScan.OpeningChunkMasks[first] = 0;
            streaming.OpeningScans.Add(firstKey, firstScan);
            streaming.OpeningScans.Add(
                new ClassicBiomeOpeningScanKey(second, second, second, second),
                new ClassicBiomeOpeningScanState(second, second, second, second, 0, 7, TimeSpan.Zero));

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(BiomeSystem).GetMethod("PruneClassicOpeningScans", flags)!.Invoke(biomes, new object[] { streaming });

            Assert.Multiple(() =>
            {
                Assert.That(streaming.OpeningScans.Count, Is.EqualTo(1));
                Assert.That(streaming.OpeningScans, Does.ContainKey(firstKey));
            });
        });
    }

    [Test]
    public async Task OpeningScanOverflowEventuallyGetsBoundedAdmission()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.MaxOpeningCacheEntries = 1;
                streaming.OpeningCellsPerSlice = 1;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                biome.LoadedChunks.Add(Vector2i.Zero);
                biome.LoadedChunks.Add(new Vector2i(8, 0));

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var begin = typeof(BiomeSystem).GetMethod("BeginClassicStreamingBudget", flags)!;
                var request = typeof(BiomeSystem).GetMethod("TryGetClassicOpeningWorldBounds", flags)!;
                var firstBounds = new Box2(2f, 2f, 3f, 3f);
                var overflowBounds = new Box2(10f, 2f, 11f, 3f);
                var overflowChunk = new Vector2i(1, 0);
                var overflowAdmitted = false;
                var overflowProgressed = false;

                for (var tick = 0; tick < 8 && !overflowProgressed; tick++)
                {
                    streaming.OpeningScanGeneration++;
                    begin.Invoke(biomes, null);
                    if ((tick & 1) == 0)
                    {
                        Request(firstBounds);
                        Request(overflowBounds);
                    }
                    else
                    {
                        Request(overflowBounds);
                        Request(firstBounds);
                    }

                    Assert.That(streaming.OpeningScans.Count, Is.LessThanOrEqualTo(1), $"tick={tick}");
                    var overflow = streaming.OpeningScans.Values.SingleOrDefault(scan =>
                        scan.RequiredFirstChunk == overflowChunk);
                    if (overflow == null)
                        continue;

                    overflowAdmitted = true;
                    overflowProgressed |= overflow.Phase != ClassicBiomeOpeningScanPhase.WaitingForChunks ||
                                          overflow.NextChunk != overflowChunk;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(overflowAdmitted, Is.True);
                    Assert.That(overflowProgressed, Is.True);
                    Assert.That(streaming.OpeningScans.Count, Is.LessThanOrEqualTo(1));
                });

                void Request(Box2 bounds)
                {
                    request.Invoke(biomes, new object[]
                    {
                        terrain,
                        biome,
                        grid,
                        bounds,
                        default(Box2),
                    });
                }
            });
        }
        finally
        {
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task TransformedSeparateBiomeGridsStreamInTheirOwnLocalCoordinates()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var upperMap = map.CreateMap(runMapInit: false);
                var lowerMap = map.CreateMap(runMapInit: false);
                var upperGrid = map.CreateGridEntity(upperMap);
                var lowerGrid = map.CreateGridEntity(lowerMap);
                upperGrid.Comp.CanSplit = false;
                lowerGrid.Comp.CanSplit = false;

                // The same world-space point belongs to unrelated local tiles on the two biome
                // grids. Distinct rotations also make an upper-local-as-lower-local shortcut fail.
                var upperRotation = Angle.FromDegrees(90);
                var lowerRotation = Angle.FromDegrees(-90);
                transform.SetWorldPositionRotation(upperGrid, new Vector2(40f, -20f), upperRotation);
                var upperViewerTile = new Vector2i(12, 20);
                var worldViewer = map.GridTileToWorldPos(upperGrid, upperGrid.Comp, upperViewerTile);
                var lowerViewerTile = new Vector2i(34, 6);
                var lowerViewerCenter = (Vector2) lowerViewerTile + new Vector2(0.5f);
                transform.SetWorldPositionRotation(
                    lowerGrid,
                    worldViewer - lowerRotation.RotateVec(lowerViewerCenter),
                    lowerRotation);

                var tileOnly = Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest");
                var upperBiome = em.EnsureComponent<BiomeComponent>(upperGrid);
                biomes.SetSeed(upperGrid, upperBiome, 24, false);
                biomes.SetTemplate(upperGrid, upperBiome, tileOnly, false);
                var lowerBiome = em.EnsureComponent<BiomeComponent>(lowerGrid);
                biomes.SetSeed(lowerGrid, lowerBiome, 42, false);
                biomes.SetTemplate(lowerGrid, lowerBiome, tileOnly, false);
                lowerBiome.MarkerLayers.Add("ClassicStreamingMarkerNodeTest");

                var upperStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(upperGrid);
                var lowerStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(lowerGrid);
                foreach (var streaming in new[] { upperStreaming, lowerStreaming })
                {
                    streaming.BackgroundChunksPerTick = 64;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.WorkBudget = TimeSpan.FromSeconds(1);
                    streaming.UnloadDelay = TimeSpan.FromHours(1);
                }
                lowerStreaming.MarkerAreaCellsPerSlice = 1;

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [upperMap] = 0,
                    [lowerMap] = -1,
                }), Is.True);
                map.InitializeMap(upperMap);
                map.InitializeMap(lowerMap);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    map.GridTileToLocal(upperGrid, upperGrid.Comp, upperViewerTile));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var lowerMapId = em.GetComponent<MapComponent>(lowerMap).MapId;
                var mappedLowerTile = map.TileIndicesFor(
                    lowerGrid,
                    lowerGrid.Comp,
                    new MapCoordinates(worldViewer, lowerMapId));
                Assert.That(mappedLowerTile, Is.EqualTo(lowerViewerTile),
                    "The regression setup must place the two unrelated local tiles at one world point.");

                biomes.Update(0);
                biomes.Update(0);

                var upperViewerChunk = new Vector2i(8, 16);
                var lowerPrefetch = new HashSet<Vector2i>
                {
                    new(24, 0),
                    new(24, 8),
                    new(32, 0),
                    new(32, 8),
                };
                Assert.Multiple(() =>
                {
                    Assert.That(upperStreaming.ViewerCells, Is.EquivalentTo(new[] { upperViewerTile }),
                        "Physical viewer safety cells must be upper-grid-local, not world coordinates.");
                    Assert.That(upperStreaming.ViewerChunks, Is.EquivalentTo(new[] { upperViewerChunk }));
                    Assert.That(lowerStreaming.ViewerCells, Is.Empty);
                    Assert.That(lowerStreaming.ViewerChunks, Is.Empty);
                    Assert.That(lowerBiome.LoadedChunks, Is.EquivalentTo(lowerPrefetch));
                    Assert.That(lowerStreaming.AdjacentPrefetchChunks, Is.EquivalentTo(lowerPrefetch));
                    Assert.That(lowerStreaming.MarkerAreaScan, Is.Null,
                        "A lower prefetch request must not request a render-only marker area.");
                });

                Assert.That(map.TryGetTileRef(
                    upperGrid.Owner,
                    upperGrid.Comp,
                    upperViewerTile,
                    out var upperTile), Is.True);
                Assert.That(upperTile.Tile.IsEmpty, Is.False);
                map.SetTile(upperGrid, upperViewerTile, Tile.Empty);

                // The opening proves visibility; generation still covers the complete lower-eye
                // PVS range in the lower grid's own transformed local coordinates.
                var expectedLowerChunks = new HashSet<Vector2i>();
                for (var x = 16; x <= 48; x += 8)
                for (var y = -16; y <= 16; y += 8)
                    expectedLowerChunks.Add(new Vector2i(x, y));
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(lowerBiome.LoadedChunks, Is.EquivalentTo(lowerPrefetch),
                        "An incomplete transformed opening snapshot must expose only the bounded prefetch.");
                    Assert.That(upperStreaming.OpeningScans.Values.Any(scan =>
                            scan.Phase != ClassicBiomeOpeningScanPhase.Complete),
                        Is.True,
                        "The transformed PVS opening snapshot must be resumed over bounded slices.");
                });

                for (var i = 0;
                     i < 64 && !lowerBiome.LoadedChunks.ToHashSet().SetEquals(expectedLowerChunks);
                     i++)
                {
                    biomes.Update(0);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(lowerBiome.LoadedChunks, Is.EquivalentTo(expectedLowerChunks),
                        "The lower eye must stream its complete PVS in lower-grid-local coordinates.");
                    Assert.That(lowerStreaming.ViewerCells, Is.EquivalentTo(new[] { lowerViewerTile }));
                    Assert.That(lowerStreaming.ViewerChunks, Is.Empty,
                        "A render-only lower eye must not enter the physical viewer lane.");
                    Assert.That(lowerStreaming.MarkerAreaScan, Is.Not.Null,
                        "The visible lower local bounds must request marker work.");
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task RoofBatchPreservesTileMasksAndEntityColors()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var roofSystem = em.System<SharedRoofSystem>();
        EntityUid mapUid = default;
        try
        {
            await Server.WaitAssertion(() =>
            {
                mapUid = map.CreateMap();
                var grid = map.CreateGridEntity(mapUid);
                grid.Comp.CanSplit = false;
                var plating = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                map.SetTile(grid, Vector2i.Zero, plating);
                map.SetTile(grid, new Vector2i(1, 0), plating);
                var roof = em.EnsureComponent<RoofComponent>(grid);
                var roofEntity = (grid.Owner, grid.Comp, roof);
                var bounds = new Box2(-1, -1, 3, 2);
                roofSystem.SetRoof(roofEntity, Vector2i.Zero, true);
                Assert.That(roofSystem.HasRoofEntities(grid, bounds), Is.False);
                Assert.That(roofSystem.GetColor(roofEntity, Vector2i.Zero, false), Is.EqualTo(roof.Color));
                Assert.That(roofSystem.GetColor(roofEntity, new Vector2i(1, 0), false), Is.Null);

                var wall = em.SpawnEntity("WallSolid", map.GridTileToLocal(grid, grid.Comp, new Vector2i(1, 0)));
                var entityRoof = em.GetComponent<IsRoofComponent>(wall);
                entityRoof.Color = Color.Red;
                var checkEntities = roofSystem.HasRoofEntities(grid, bounds);
                Assert.That(checkEntities, Is.True);
                Assert.That(roofSystem.GetColor(roofEntity, new Vector2i(1, 0), checkEntities), Is.EqualTo(Color.Red));

                entityRoof.Enabled = false;
                checkEntities = roofSystem.HasRoofEntities(grid, bounds);
                Assert.That(checkEntities, Is.False);
                Assert.That(roofSystem.GetColor(roofEntity, new Vector2i(1, 0), checkEntities), Is.Null);
                Assert.That(roofSystem.GetColor(roofEntity, Vector2i.Zero, checkEntities), Is.EqualTo(roof.Color));
            });
        }
        finally
        {
            if (mapUid != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(mapUid));
        }
    }

    [Test]
    public async Task BackgroundChunkLoadsAreSlicedAndViewerPromotionIsImmediate()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var underground = map.CreateMap(runMapInit: false);
                var surface = map.CreateMap();
                biomes.EnsurePlanet(underground, Server.ProtoMan.Index(UndergroundBiome), 42);
                var grid = em.GetComponent<MapGridComponent>(underground);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(underground);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 8;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                streaming.UnloadDelay = TimeSpan.FromHours(1);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [underground] = -1,
                });
                if (!map.IsInitialized(underground))
                    map.InitializeMap(underground);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(underground, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var biome = em.GetComponent<BiomeComponent>(underground);

                biomes.Update(0);
                var firstPartial = streaming.PartialLoads.Single();
                var partialChunk = firstPartial.Key;
                Assert.Multiple(() =>
                {
                    Assert.That(firstPartial.Value, Is.EqualTo(4));
                    Assert.That(biome.LoadedChunks, Does.Not.Contain(partialChunk),
                        "A partial chunk must not be exposed to the unload path as complete.");
                    Assert.That(biome.LoadedEntities[partialChunk].Count, Is.EqualTo(5),
                        "The physical cell ahead of the sequential cursor must be the only urgent addition.");
                    Assert.That(CountNonEmptyTiles(partialChunk), Is.EqualTo(9));
                    Assert.That(BitOperations.PopCount(streaming.PriorityLoadedCells[partialChunk]), Is.EqualTo(1));
                });

                for (var i = 0; i < 20 && !biome.LoadedChunks.Contains(partialChunk); i++)
                {
                    var before = biome.LoadedEntities[partialChunk].Count;
                    biomes.Update(0);
                    var after = biome.LoadedEntities[partialChunk].Count;
                    Assert.That(after - before, Is.InRange(0, 4));
                }

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PartialLoads.ContainsKey(partialChunk), Is.False);
                    Assert.That(streaming.PriorityLoadedCells.ContainsKey(partialChunk), Is.False);
                    Assert.That(biome.LoadedChunks, Does.Contain(partialChunk));
                    Assert.That(biome.LoadedEntities[partialChunk].Count, Is.EqualTo(64));
                    Assert.That(CountNonEmptyTiles(partialChunk), Is.EqualTo(64));
                });

                // Moving away from an unfinished chunk must cancel it incrementally. Completing
                // 56 invisible wall cells merely to delete all 64 again was a movement-amplified
                // source of TTA spikes.
                streaming.UnloadDelay = TimeSpan.Zero;
                transform.SetWorldPosition(viewer, new Vector2(1004.5f, 1004.5f));
                biomes.Update(0);
                var inactivePartial = streaming.PartialLoads.Single();
                var inactiveChunk = inactivePartial.Key;
                Assert.That(inactivePartial.Value, Is.EqualTo(4));
                streaming.BackgroundChunksPerTick = 0;
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                var largestCursor = inactivePartial.Value;
                for (var i = 0; i < 192; i++)
                {
                    biomes.Update(0);
                    if (streaming.PartialLoads.TryGetValue(inactiveChunk, out var cursor))
                        largestCursor = Math.Max(largestCursor, cursor);
                    if (!streaming.PartialLoads.ContainsKey(inactiveChunk) &&
                        !streaming.PartialUnloads.ContainsKey(inactiveChunk) &&
                        !biome.LoadedEntities.ContainsKey(inactiveChunk))
                    {
                        break;
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(largestCursor, Is.EqualTo(4),
                        "An inactive partial chunk must never be completed in the background.");
                    Assert.That(streaming.PartialLoads.ContainsKey(inactiveChunk), Is.False);
                    Assert.That(streaming.PartialUnloads.ContainsKey(inactiveChunk), Is.False);
                    Assert.That(biome.LoadedChunks, Does.Not.Contain(inactiveChunk));
                    Assert.That(biome.LoadedEntities.ContainsKey(inactiveChunk), Is.False);
                    Assert.That(CountNonEmptyTiles(inactiveChunk), Is.Zero);
                    Assert.That(streaming.PendingUnloads.ContainsKey(inactiveChunk), Is.False);
                    Assert.That(biome.ModifiedTiles.ContainsKey(inactiveChunk), Is.False,
                        "Never-generated partial cells must not become permanent modifications during finalize.");
                });

                // A fully cancelled partial chunk must remain regenerable. The old all-64-cell
                // finalize path poisoned untouched cells as ModifiedTiles and left permanent
                // holes on this second visit.
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 8;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                transform.SetWorldPosition(
                    viewer,
                    new Vector2(inactiveChunk.X + 4.5f, inactiveChunk.Y + 4.5f));
                for (var i = 0; i < 32 && !biome.LoadedChunks.Contains(inactiveChunk); i++)
                    biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks, Does.Contain(inactiveChunk));
                    Assert.That(biome.LoadedEntities[inactiveChunk].Count, Is.EqualTo(64));
                    Assert.That(CountNonEmptyTiles(inactiveChunk), Is.EqualTo(64));
                    Assert.That(biome.ModifiedTiles.ContainsKey(inactiveChunk), Is.False);
                });

                int CountNonEmptyTiles(Vector2i origin)
                {
                    var count = 0;
                    for (var x = 0; x < 8; x++)
                    for (var y = 0; y < 8; y++)
                    {
                        var indices = origin + new Vector2i(x, y);
                        if (map.TryGetTileRef(underground, grid, indices, out var tile) && !tile.Tile.IsEmpty)
                            count++;
                    }

                    return count;
                }
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task PartialUnloadRollsBackBeforeViewerCanObserveMissingWalls()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var underground = map.CreateMap(runMapInit: false);
                var surface = map.CreateMap();
                biomes.EnsurePlanet(underground, Server.ProtoMan.Index(UndergroundBiome), 42);
                var grid = em.GetComponent<MapGridComponent>(underground);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(underground);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.UnloadEntitiesPerSlice = 2;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [underground] = -1,
                });
                if (!map.IsInitialized(underground))
                    map.InitializeMap(underground);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(underground, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(underground);
                var origin = Vector2i.Zero;
                var originalEntities = biome.LoadedEntities[origin].Keys.ToHashSet();
                Assert.That(originalEntities.Count, Is.EqualTo(64));

                streaming.BackgroundChunksPerTick = 0;
                transform.SetWorldPosition(viewer, new Vector2(1004.5f, 1004.5f));
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks, Does.Not.Contain(origin));
                    Assert.That(streaming.PartialUnloads.TryGetValue(origin, out var partial), Is.True);
                    Assert.That(partial!.Phase, Is.EqualTo(ClassicBiomePartialUnloadPhase.Entities));
                    Assert.That(partial.RemovedEntityCells.Count, Is.EqualTo(2));
                    Assert.That(biome.LoadedEntities[origin].Count, Is.EqualTo(62));
                });

                // Viewer promotion runs before optional streaming work and must synchronously
                // roll back the two missing walls, without rebuilding the other 62.
                transform.SetWorldPosition(viewer, new Vector2(4.5f, 4.5f));
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PartialUnloads.ContainsKey(origin), Is.False);
                    Assert.That(biome.LoadedChunks, Does.Contain(origin));
                    Assert.That(biome.LoadedEntities[origin].Count, Is.EqualTo(64));
                    Assert.That(biome.LoadedEntities[origin].Keys.Intersect(originalEntities).Count(), Is.EqualTo(62));
                });

                foreach (var tile in biome.LoadedEntities[origin].Values)
                {
                    var anchored = map.GetAnchoredEntities(underground, grid, tile);
                    var count = 0;
                    while (anchored.MoveNext(out _))
                        count++;
                    Assert.That(count, Is.EqualTo(1), $"Rollback duplicated the wall at {tile}.");
                }
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task ExpiredBudgetStillStartsAndFinishesPendingUnload()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(terrain, Server.ProtoMan.Index(UndergroundBiome), 42);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 1;
                streaming.ViewerSafetyRadius = 0;
                streaming.UnloadEntitiesPerSlice = 1;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                var viewer = em.SpawnEntity(null, new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var origin = Vector2i.Zero;
                Assert.That(streaming.PartialLoads, Does.ContainKey(origin),
                    "The setup must leave bounded terrain work for the inactive-unload path.");

                transform.SetWorldPosition(viewer, new Vector2(1004.5f, 1004.5f));
                for (var i = 0; i < 200; i++)
                {
                    biomes.Update(0);
                    if (!streaming.PartialLoads.ContainsKey(origin) &&
                        !streaming.PartialUnloads.ContainsKey(origin) &&
                        !streaming.PendingUnloads.ContainsKey(origin))
                    {
                        break;
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PendingUnloads, Does.Not.ContainKey(origin),
                        "A mature pending unload must not starve behind an already-expired soft budget.");
                    Assert.That(streaming.PartialUnloads, Does.Not.ContainKey(origin));
                    Assert.That(streaming.PartialLoads, Does.Not.ContainKey(origin));
                    Assert.That(biome.LoadedChunks, Does.Not.Contain(origin));
                    Assert.That(biome.LoadedEntities, Does.Not.ContainKey(origin));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task ReturningToCancellingPartialChunkRestoresItsCursorWithoutPromotion()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(terrain, Server.ProtoMan.Index(UndergroundBiome), 42);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 8;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.UnloadEntitiesPerSlice = 2;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                map.InitializeMap(terrain);

                var viewer = em.SpawnEntity(null, new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var origin = Vector2i.Zero;
                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PartialLoads[origin], Is.EqualTo(8));
                    Assert.That(BitOperations.PopCount(streaming.PriorityLoadedCells[origin]), Is.EqualTo(9));
                    Assert.That(biome.LoadedEntities[origin].Count, Is.EqualTo(17));
                });

                streaming.BackgroundChunksPerTick = 0;
                transform.SetWorldPosition(viewer, new Vector2(1004.5f, 1004.5f));
                biomes.Update(0);
                Assert.That(streaming.PartialUnloads.TryGetValue(origin, out var cancelling), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(cancelling!.WasPartial, Is.True);
                    Assert.That(cancelling.PartialCursor, Is.EqualTo(8));
                    Assert.That(cancelling.RemovedEntityCells.Count, Is.EqualTo(2));
                });

                // Re-enter at a different, never-generated part of the same chunk while two old
                // priority walls are gone. The new physical safety cells and the removed walls
                // are restored immediately, but the remainder stays bounded background work.
                streaming.WorkBudget = TimeSpan.Zero;
                transform.SetWorldPosition(viewer, new Vector2(1.5f, 6.5f));
                biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PartialUnloads.ContainsKey(origin), Is.False);
                    Assert.That(streaming.PartialLoads[origin], Is.EqualTo(8));
                    Assert.That(biome.LoadedChunks, Does.Not.Contain(origin));
                    Assert.That(BitOperations.PopCount(streaming.PriorityLoadedCells[origin]), Is.EqualTo(15));
                    Assert.That(BitOperations.PopCount(streaming.MaterializedCells[origin]), Is.InRange(23, 28));
                    Assert.That(biome.LoadedEntities[origin].Count, Is.EqualTo(23));
                });

                var requested = new Vector2i(1, 6);
                var anchored = map.GetAnchoredEntities(terrain, grid, requested);
                Assert.That(anchored.MoveNext(out _), Is.True,
                    "A physical return must materialize a previously untouched predicted wall before physics.");
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task ExpiredZCacheContinuesPartialUnloadInsteadOfRestoringIt()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(terrain, Server.ProtoMan.Index(UndergroundBiome), 42);
                em.GetComponent<MapGridComponent>(terrain).CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.UnloadEntitiesPerSlice = 2;
                streaming.UnloadEntitiesPerTick = 2;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.FromSeconds(10);
                streaming.MaxZLevelCachedChunks = 1;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                map.InitializeMap(terrain);

                var viewer = em.SpawnEntity(null, new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                var cacheEye = em.SpawnEntity(null, new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var origin = Vector2i.Zero;
                transform.SetWorldPosition(viewer, new Vector2(1004.5f, 1004.5f));
                biomes.Update(0);
                Assert.That(streaming.PartialUnloads.TryGetValue(origin, out var unloading), Is.True);
                Assert.That(unloading!.Phase, Is.EqualTo(ClassicBiomePartialUnloadPhase.Entities));
                var entitiesBeforeCache = biome.LoadedEntities[origin].Keys.ToHashSet();

                biomes.CacheClassicZLevel(cacheEye);
                Assert.Multiple(() =>
                {
                    Assert.That(streaming.CachedZChunks, Does.ContainKey(origin));
                    Assert.That(unloading.Phase, Is.EqualTo(ClassicBiomePartialUnloadPhase.Restoring));
                });

                streaming.CachedZChunks[origin] = TimeSpan.Zero;
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(streaming.CachedZChunks, Does.Not.ContainKey(origin));
                    Assert.That(streaming.PartialUnloads.TryGetValue(origin, out var continuing), Is.True);
                    Assert.That(continuing!.Phase, Is.EqualTo(ClassicBiomePartialUnloadPhase.Entities));
                    Assert.That(biome.LoadedEntities[origin].Keys, Is.SubsetOf(entitiesBeforeCache));
                    Assert.That(biome.LoadedEntities[origin].Count, Is.LessThan(entitiesBeforeCache.Count));
                });

                for (var i = 0; i < 100 && streaming.PartialUnloads.ContainsKey(origin); i++)
                    biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PartialUnloads, Does.Not.ContainKey(origin));
                    Assert.That(streaming.PendingUnloads, Does.Not.ContainKey(origin));
                    Assert.That(biome.LoadedChunks, Does.Not.Contain(origin));
                    Assert.That(biome.LoadedEntities, Does.Not.ContainKey(origin));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task MarkerAreaScanIsBoundedDeterministicAndCancelsStaleRequests()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.MarkerAreaCellsPerSlice = 3;
                streaming.MarkerNodesPerSlice = 1;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                streaming.UnloadDelay = TimeSpan.FromHours(1);
                var biome = em.GetComponent<BiomeComponent>(terrain);
                biome.MarkerLayers.Add("ClassicStreamingMarkerNodeTest");
                map.InitializeMap(terrain);

                var viewer = em.SpawnEntity(null, new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var firstScan = streaming.MarkerAreaScan;
                Assert.That(firstScan, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(firstScan!.NextCell, Is.EqualTo(3));
                    Assert.That(firstScan.RemainingTiles.Count, Is.EqualTo(3));
                    Assert.That(biome.LoadedMarkers.ContainsKey(firstScan.Layer), Is.False);
                });

                var layerProto = Server.ProtoMan.Index<BiomeMarkerLayerPrototype>(firstScan!.Layer);
                biomes.GetMarkerNodes(
                    terrain,
                    biome,
                    em.GetComponent<MapGridComponent>(terrain),
                    layerProto,
                    firstScan.Forced,
                    firstScan.Bounds,
                    firstScan.Count,
                    new Random(firstScan.MarkerSeed),
                    out var expected,
                    out _);

                biomes.Update(0);
                Assert.That(streaming.MarkerAreaScan!.NextCell, Is.EqualTo(6));

                // Removing the range request must discard the read-only snapshot instead of
                // publishing nodes for an area that is no longer observed.
                biome.MarkerLayers.Clear();
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(streaming.MarkerAreaScan, Is.Null);
                    Assert.That(biome.LoadedMarkers.ContainsKey(firstScan.Layer), Is.False);
                    Assert.That(biome.PendingMarkers, Is.Empty);
                });

                // Restart with a different slice width. The completed node set must match the
                // legacy atomic algorithm because random draws happen only after row-major scan.
                biome.MarkerLayers.Add(firstScan.Layer);
                streaming.MarkerAreaCellsPerSlice = 64;
                biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.MarkerAreaScan, Is.Not.Null);
                    Assert.That(streaming.MarkerAreaScan!.Phase,
                        Is.EqualTo(ClassicBiomeMarkerAreaScanPhase.Selecting));
                    Assert.That(biome.LoadedMarkers.ContainsKey(firstScan.Layer), Is.False);
                    Assert.That(biome.PendingMarkers, Is.Empty);
                });

                var selectionTicks = 0;
                while (streaming.MarkerAreaScan != null && selectionTicks++ < 128)
                    biomes.Update(0);

                var actual = biome.PendingMarkers.Values
                    .Where(layers => layers.ContainsKey(firstScan.Layer))
                    .SelectMany(layers => layers[firstScan.Layer])
                    .ToHashSet();
                foreach (var markerCells in streaming.MarkerCells.Values)
                foreach (var (node, marker) in markerCells)
                {
                    if (marker.Layer == firstScan.Layer)
                        actual.Add(node);
                }
                foreach (var modified in biome.ModifiedTiles.Values)
                foreach (var node in modified)
                {
                    if (expected.ContainsKey(node))
                        actual.Add(node);
                }
                Assert.Multiple(() =>
                {
                    Assert.That(selectionTicks, Is.LessThan(128),
                        "The bounded seeded selection did not converge.");
                    Assert.That(streaming.MarkerAreaScan, Is.Null);
                    Assert.That(biome.LoadedMarkers[firstScan.Layer], Does.Contain(firstScan.MarkerChunk));
                    Assert.That(actual, Is.EquivalentTo(expected.Keys));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task MarkerNodesOnLoadedBackgroundChunkRespectSharedEntityQuota()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var underground = map.CreateMap(runMapInit: false);
                var surface = map.CreateMap();
                biomes.EnsurePlanet(underground, Server.ProtoMan.Index(UndergroundBiome), 42);
                var grid = em.GetComponent<MapGridComponent>(underground);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(underground);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.MarkerNodesPerSlice = 2;
                streaming.UnloadDelay = TimeSpan.FromHours(1);
                streaming.WorkBudget = TimeSpan.FromSeconds(1);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [underground] = -1,
                });
                if (!map.IsInitialized(underground))
                    map.InitializeMap(underground);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(underground, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(underground);
                var origin = Vector2i.Zero;
                var markerNodes = biome.LoadedEntities[origin].Values.Take(5).ToList();
                biome.PendingMarkers[origin] = new Dictionary<string, List<Vector2i>>
                {
                    ["ClassicStreamingMarkerNodeTest"] = markerNodes,
                };

                // Keep the old chunk in the same-level PVS range but make it non-urgent; adjacent
                // Z landing visibility is intentionally only one exact cell and no longer keeps
                // an unrelated underground chunk active through an opaque surface.
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundEntitySpawnsPerTick = 1;
                transform.SetCoordinates(viewer, new EntityCoordinates(underground, new Vector2(12.5f, 4.5f)));
                for (var i = 0; i < 16 && biome.PendingMarkers.ContainsKey(origin); i++)
                {
                    var nodesBefore = biome.PendingMarkers[origin]["ClassicStreamingMarkerNodeTest"].Count;
                    var entitiesBefore = biome.LoadedEntities[origin].Count;
                    biomes.Update(0);
                    var nodesAfter = biome.PendingMarkers.TryGetValue(origin, out var pending)
                        ? pending["ClassicStreamingMarkerNodeTest"].Count
                        : 0;
                    var entitiesAfter = biome.LoadedEntities[origin].Count;
                    Assert.Multiple(() =>
                    {
                        Assert.That(nodesBefore - nodesAfter, Is.InRange(0, 1));
                        Assert.That(entitiesBefore - entitiesAfter, Is.InRange(0, 1));
                    });
                }

                Assert.Multiple(() =>
                {
                    Assert.That(biome.PendingMarkers.ContainsKey(origin), Is.False);
                    Assert.That(biome.LoadedEntities[origin].Count, Is.EqualTo(64));
                    Assert.That(biome.ModifiedTiles.ContainsKey(origin), Is.False);
                    Assert.That(streaming.MarkerCells[origin].Count, Is.EqualTo(5));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [TestCase("ClassicStreamingMarkerLifecycleTest", "ClassicStreamingMarkerLifecycleEntityTest")]
    [TestCase("ClassicStreamingCarpLifecycleTest", "MobCarpDungeon")]
    public async Task SustainedMarkerTravelKeepsStateBoundedAndDrains(
        string markerLayer,
        string markerPrototype)
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 64;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.MarkerAreaCellsPerSlice = 64;
                streaming.MarkerNodesPerSlice = 64;
                streaming.UnloadEntitiesPerSlice = 64;
                streaming.UnloadEntitiesPerTick = 64;
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                var biome = em.GetComponent<BiomeComponent>(terrain);
                biome.MarkerLayers.Add(markerLayer);
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);

                for (var stop = 0; stop < 24; stop++)
                {
                    transform.SetCoordinates(
                        viewer,
                        new EntityCoordinates(terrain, new Vector2(stop * 48f + 4.5f, 4.5f)));
                    for (var tick = 0; tick < 12; tick++)
                    {
                        biomes.Update(0);
                        var loadedAreas = biome.LoadedMarkers.Values.Sum(areas => areas.Count);
                        var pendingNodes = biome.PendingMarkers.Values
                            .Sum(layers => layers.Values.Sum(nodes => nodes.Count));
                        var markerCells = streaming.MarkerCells.Values.Sum(cells => cells.Count);
                        var markerEntities = CountMarkers();
                        Assert.Multiple(() =>
                        {
                            Assert.That(loadedAreas, Is.LessThanOrEqualTo(8));
                            Assert.That(pendingNodes, Is.LessThanOrEqualTo(8));
                            Assert.That(markerCells, Is.LessThanOrEqualTo(8));
                            Assert.That(markerEntities, Is.LessThanOrEqualTo(8));
                            Assert.That(biome.ModifiedTiles, Is.Empty);
                            Assert.That(streaming.PinnedMarkerAreas, Is.Empty);
                        });
                    }

                    Assert.Multiple(() =>
                    {
                        Assert.That(biome.LoadedMarkers.Values.Sum(areas => areas.Count), Is.EqualTo(4));
                        Assert.That(biome.PendingMarkers, Is.Empty);
                        Assert.That(streaming.MarkerCells.Values.Sum(cells => cells.Count), Is.EqualTo(4));
                        Assert.That(CountMarkers(), Is.EqualTo(4));
                    });
                }

                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
            });

            var drainTicks = (int) Math.Ceiling(3d / Server.Timing.TickPeriod.TotalSeconds);
            await Server.WaitRunTicks(drainTicks);

            await Server.WaitPost(() =>
            {
                var biome = em.GetComponent<BiomeComponent>(terrain);
                var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(terrain);
                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedMarkers.Values.Sum(areas => areas.Count), Is.Zero);
                    Assert.That(biome.PendingMarkers, Is.Empty);
                    Assert.That(streaming.MarkerCells, Is.Empty);
                    Assert.That(streaming.PinnedMarkerAreas, Is.Empty);
                    Assert.That(streaming.MarkerAreaScan, Is.Null);
                    Assert.That(CountMarkers(), Is.Zero);
                    Assert.That(biome.ModifiedTiles, Is.Empty);
                    Assert.That(biome.LoadedChunks, Is.Empty);
                    Assert.That(biome.LoadedEntities, Is.Empty);
                    Assert.That(streaming.PartialLoads, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                    Assert.That(em.GetComponent<MapGridComponent>(terrain).ChunkCount, Is.Zero);
                });
            });

            int CountMarkers()
            {
                var count = 0;
                var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var metadata, out var xform))
                {
                    if (xform.ParentUid == terrain &&
                        metadata.EntityPrototype?.ID == markerPrototype)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [Test]
    public async Task EditedMarkerSurvivesUnloadAndDoesNotRespawn()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var metadata = em.System<MetaDataSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 64;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.MarkerAreaCellsPerSlice = 64;
                streaming.MarkerNodesPerSlice = 64;
                streaming.UnloadEntitiesPerSlice = 64;
                streaming.UnloadEntitiesPerTick = 64;
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                var biome = em.GetComponent<BiomeComponent>(terrain);
                biome.MarkerLayers.Add("ClassicStreamingMarkerLifecycleTest");
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                for (var i = 0; i < 12; i++)
                    biomes.Update(0);

                var (chunk, cells) = streaming.MarkerCells.First();
                var (node, marker) = cells.First();
                var root = marker.Root;
                metadata.SetEntityName(root, "edited marker");

                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                for (var i = 0; i < 16; i++)
                    biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(em.EntityExists(root), Is.True);
                    Assert.That(biome.ModifiedTiles[chunk], Does.Contain(node));
                    Assert.That(streaming.MarkerCells.TryGetValue(chunk, out var remaining) &&
                        remaining.ContainsKey(node), Is.False);
                    Assert.That(streaming.PinnedMarkerAreas["ClassicStreamingMarkerLifecycleTest"],
                        Does.Contain(Vector2i.Zero));
                    Assert.That(biome.LoadedEntities, Is.Empty);
                });

                Server.PlayerMan.SetAttachedEntity(session, viewer);
                for (var i = 0; i < 12; i++)
                    biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(em.EntityExists(root), Is.True);
                    Assert.That(em.GetComponent<MetaDataComponent>(root).EntityName, Is.EqualTo("edited marker"));
                    Assert.That(streaming.MarkerCells.TryGetValue(chunk, out var remaining) &&
                        remaining.ContainsKey(node), Is.False);
                    Assert.That(biome.LoadedEntities.TryGetValue(chunk, out var loaded) && loaded.ContainsKey(root),
                        Is.False);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [TestCase("Carps", "MobCarpDungeon")]
    [TestCase("Xenos", "MobXeno")]
    public async Task AmbientMarkersDiscardNaturalMovementAndKeepPlayerChanges(
        string markerLayer,
        string markerPrototype)
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var metadata = em.System<MetaDataSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;
        EntityUid mob = default;
        EntityUid damaged = default;
        EntityUid edited = default;
        EntityUid touched = default;
        EntityUid retuned = default;
        var node = new Vector2i(4, 4);
        var damagedNode = new Vector2i(5, 4);
        var editedNode = new Vector2i(6, 4);
        var touchedNode = new Vector2i(7, 4);
        var retunedNode = new Vector2i(4, 6);

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                em.GetComponent<MapGridComponent>(terrain).CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 64;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.MarkerNodesPerSlice = 64;
                streaming.UnloadEntitiesPerSlice = 64;
                streaming.UnloadEntitiesPerTick = 64;
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                for (var i = 0; i < 32 && !biome.LoadedChunks.Contains(Vector2i.Zero); i++)
                    biomes.Update(0);
                Assert.That(biome.LoadedChunks, Does.Contain(Vector2i.Zero));
                biome.PendingMarkers[Vector2i.Zero] = new Dictionary<string, List<Vector2i>>
                {
                    [markerLayer] = [node, damagedNode, editedNode, touchedNode, retunedNode],
                };
                biomes.Update(0);

                mob = streaming.MarkerCells[Vector2i.Zero][node].Root;
                damaged = streaming.MarkerCells[Vector2i.Zero][damagedNode].Root;
                edited = streaming.MarkerCells[Vector2i.Zero][editedNode].Root;
                touched = streaming.MarkerCells[Vector2i.Zero][touchedNode].Root;
                retuned = streaming.MarkerCells[Vector2i.Zero][retunedNode].Root;
                Assert.Multiple(() =>
                {
                    Assert.That(em.EntityExists(mob), Is.True);
                    Assert.That(em.GetComponent<MetaDataComponent>(mob).EntityPrototype?.ID,
                        Is.EqualTo(markerPrototype));
                    Assert.That(biome.LoadedEntities[Vector2i.Zero], Does.ContainKey(mob));
                });
            });

            await Server.WaitRunTicks(600);
            await Server.WaitPost(() =>
            {
                transform.SetCoordinates(mob,
                    new EntityCoordinates(terrain, new Vector2(4.5f, 5.5f)));
                em.System<DamageableSystem>().TryChangeDamage(damaged,
                    new DamageSpecifier { DamageDict = { ["Blunt"] = 1 } }, ignoreResistances: true);
                metadata.SetEntityName(edited, "edited marker");
                em.EventBus.RaiseLocalEvent(touched, new PullStartedMessage(viewer, touched));
                em.GetComponent<TemperatureComponent>(retuned).SpecificHeat *= 2f;
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                var biome = em.GetComponent<BiomeComponent>(terrain);
                var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(terrain);
                for (var i = 0; i < 128 &&
                     (biome.LoadedChunks.Contains(Vector2i.Zero) ||
                      streaming.PartialLoads.ContainsKey(Vector2i.Zero) ||
                      streaming.PartialUnloads.ContainsKey(Vector2i.Zero) ||
                      streaming.PendingUnloads.ContainsKey(Vector2i.Zero)); i++)
                    biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks, Does.Not.Contain(Vector2i.Zero));
                    Assert.That(streaming.PartialUnloads, Does.Not.ContainKey(Vector2i.Zero));
                    Assert.That(em.EntityExists(mob), Is.False);
                    Assert.That(em.EntityExists(damaged), Is.True);
                    Assert.That(em.EntityExists(edited), Is.True);
                    Assert.That(em.EntityExists(touched), Is.True);
                    Assert.That(em.EntityExists(retuned), Is.True);
                    Assert.That(biome.ModifiedTiles.TryGetValue(Vector2i.Zero, out var modified) &&
                        modified.Contains(node), Is.False);
                    Assert.That(biome.ModifiedTiles[Vector2i.Zero], Does.Contain(damagedNode));
                    Assert.That(biome.ModifiedTiles[Vector2i.Zero], Does.Contain(editedNode));
                    Assert.That(biome.ModifiedTiles[Vector2i.Zero], Does.Contain(touchedNode));
                    Assert.That(biome.ModifiedTiles[Vector2i.Zero], Does.Contain(retunedNode));
                    Assert.That(streaming.MarkerCells.ContainsKey(Vector2i.Zero), Is.False);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }

    }

    [Test]
    public async Task BackgroundSlicesRotateBetweenDistantViewers()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var subscribers = em.System<ViewSubscriberSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        EntityUid auxiliary = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var underground = map.CreateMap(runMapInit: false);
                var surface = map.CreateMap();
                biomes.EnsurePlanet(underground, Server.ProtoMan.Index(UndergroundBiome), 42);
                var grid = em.GetComponent<MapGridComponent>(underground);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(underground);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 8;
                streaming.UnloadDelay = TimeSpan.FromHours(1);
                streaming.WorkBudget = TimeSpan.FromSeconds(1);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [underground] = -1,
                });
                if (!map.IsInitialized(underground))
                    map.InitializeMap(underground);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(underground, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                auxiliary = em.SpawnEntity(null,
                    new EntityCoordinates(underground, new Vector2(1004.5f, 4.5f)));
                subscribers.AddViewSubscriber(auxiliary, session);

                biomes.Update(0);
                biomes.Update(0);

                Assert.That(streaming.PartialLoads.Count, Is.EqualTo(2),
                    "Each viewer must receive a background slice before either consumes a second one.");
                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PartialLoads.Keys.Any(chunk => Math.Abs(chunk.X) <= 24), Is.True,
                        "The first viewer did not receive preload work.");
                    Assert.That(streaming.PartialLoads.Keys.Any(chunk => Math.Abs(chunk.X - 1000) <= 24), Is.True,
                        "The distant viewer was starved by coordinate tie-breaking.");
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (auxiliary != EntityUid.Invalid)
                    subscribers.RemoveViewSubscriber(auxiliary, session);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
            });
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task VisibleBiomeRangeWinsOverAdjacentPrefetchUnderExpiredBudget()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var subscribers = em.System<ViewSubscriberSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        EntityUid camera = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var upper = map.CreateMap(runMapInit: false);
                var lower = map.CreateMap(runMapInit: false);
                var tileOnly = Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest");
                biomes.EnsurePlanet(upper, tileOnly, 42);
                biomes.EnsurePlanet(lower, tileOnly, 24);
                var upperStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(upper);
                upperStreaming.BackgroundChunksPerTick = 0;
                upperStreaming.WorkBudget = TimeSpan.FromSeconds(1);
                var lowerStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(lower);
                lowerStreaming.BackgroundChunksPerTick = 1;
                lowerStreaming.BackgroundCellsPerSlice = 4;
                lowerStreaming.WorkBudget = TimeSpan.FromSeconds(1);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [upper] = 0,
                    [lower] = -1,
                }), Is.True);
                map.InitializeMap(upper);
                map.InitializeMap(lower);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(upper, new Vector2(4.5f, 4.5f)));
                camera = em.SpawnEntity(null, new EntityCoordinates(lower, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                subscribers.AddViewSubscriber(camera, session);

                biomes.Update(0);
                var origin = Vector2i.Zero;
                Assert.That(lowerStreaming.PartialLoads[origin], Is.EqualTo(4));
                Assert.That(lowerStreaming.AdjacentPrefetchChunks, Does.Not.Contain(origin));

                lowerStreaming.WorkBudget = TimeSpan.Zero;
                biomes.Update(0);

                Assert.That(lowerStreaming.PartialLoads[origin], Is.GreaterThan(4));
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (camera != EntityUid.Invalid)
                    subscribers.RemoveViewSubscriber(camera, session);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
            });
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task AdjacentUndergroundStreamingPreservesMiningDamageAndCustomComponents()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        var pristineUnloaded = false;
        var pristineRegenerated = false;
        var minedStayedEmpty = false;
        var damagePreserved = false;
        var componentPreserved = false;
        var tagPreserved = false;
        var gatherablePreserved = false;
        var loadedInitially = 0;
        var landingCellLoaded = false;
        var teleportedLandingCellLoaded = false;
        var unloadDiagnostics = string.Empty;

        try
        {
            await Server.WaitPost(() =>
            {
                var mapUid = map.CreateMap(runMapInit: false);
                var surface = map.CreateMap();
                biomes.EnsurePlanet(mapUid, Server.ProtoMan.Index(UndergroundBiome), 42);
                var grid = em.GetComponent<MapGridComponent>(mapUid);
                grid.CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(mapUid);
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.Zero;
                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [mapUid] = -1,
                });
                if (!map.IsInitialized(mapUid))
                    map.InitializeMap(mapUid);
                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(surface, new Vector2(-4.5f, -4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                em.GetComponent<ClassicZPhysicsComponent>(viewer).Velocity = -1f;

                // A real downward transition request materializes exactly one adjacent cell; it
                // must not promote the rest of the hidden chunk even with ample spare budget.
                biomes.Update(0);
                var biome = em.GetComponent<BiomeComponent>(mapUid);
                var origin = new Vector2i(-8, -8);
                landingCellLoaded = biome.LoadedChunks.Count == 0 &&
                    biome.LoadedEntities.TryGetValue(origin, out var adjacentLoaded) &&
                    adjacentLoaded.Count == 1;

                // Entering the level makes the chunk render-active; only then may the bounded
                // background lane finish all of its cells for mining/persistence checks below.
                streaming.BackgroundChunksPerTick = 2;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                transform.SetCoordinates(viewer,
                    new EntityCoordinates(mapUid, new Vector2(-4.5f, -4.5f)));
                em.GetComponent<ClassicZPhysicsComponent>(viewer).Velocity = 0f;
                biomes.Update(0);
                loadedInitially = biome.LoadedChunks.Count;
                var loaded = biome.LoadedEntities[origin];
                var rocks = loaded!.Take(6).ToArray();
                var pristine = rocks[0];
                var mined = rocks[1];
                var damaged = rocks[2];
                var customized = rocks[3];
                var tagged = rocks[4];
                var gatherableCustomized = rocks[5];

                em.DeleteEntity(mined.Key);
                em.System<DamageableSystem>().TryChangeDamage(damaged.Key,
                    new DamageSpecifier { DamageDict = { ["Blunt"] = 1 } }, ignoreResistances: true);
                em.EnsureComponent<PointLightComponent>(customized.Key);
                em.System<TagSystem>().AddTag(tagged.Key, new ProtoId<TagPrototype>("Pickaxe"));
                em.GetComponent<Content.Server.Gatherable.Components.GatherableComponent>(gatherableCustomized.Key)
                    .GatherOffset += 1f;

                // Leave the chunk and let its unload deadline pass. No background loading is
                // needed for this part, so the return trip exercises exactly the original chunk.
                // Z-eyes still point at the old location until their periodic update; the main
                // physical viewer must already use the current owner position.
                streaming.BackgroundChunksPerTick = 0;
                transform.SetWorldPosition(viewer, new Vector2(1004.5f, 1004.5f));
                biomes.Update(0);
                teleportedLandingCellLoaded = biome.LoadedEntities.TryGetValue(new Vector2i(1000, 1000), out var destination) &&
                    destination.Count == 64;
                var unloadStates = new List<string>();
                for (var i = 0; i < 80; i++)
                {
                    biomes.Update(0);
                    if (i < 12)
                    {
                        unloadStates.Add($"{i}: loaded=[{string.Join(';', biome.LoadedChunks.OrderBy(value => value.X).ThenBy(value => value.Y))}] " +
                            $"pending=[{string.Join(';', streaming.PendingUnloads.Keys.OrderBy(value => value.X).ThenBy(value => value.Y))}] " +
                            $"partial=[{string.Join(';', streaming.PartialUnloads.Select(pair => $"{pair.Key}:{pair.Value.Phase}/{pair.Value.RemovedEntityCells.Count}"))}]");
                    }

                    if (!biome.LoadedChunks.Contains(origin) &&
                        !streaming.PartialUnloads.ContainsKey(origin) &&
                        !biome.LoadedEntities.ContainsKey(origin))
                    {
                        break;
                    }
                }
                unloadDiagnostics = string.Join(" | ", unloadStates);
                pristineUnloaded = !em.EntityExists(pristine.Key);

                transform.SetWorldPosition(viewer, new Vector2(-4.5f, -4.5f));
                biomes.Update(0);
                var anchored = map.GetAnchoredEntities(mapUid, grid, pristine.Value);
                pristineRegenerated = anchored.MoveNext(out var regenerated) && regenerated != pristine.Key;
                anchored = map.GetAnchoredEntities(mapUid, grid, mined.Value);
                minedStayedEmpty = !anchored.MoveNext(out _);
                damagePreserved = em.EntityExists(damaged.Key) &&
                    em.GetComponent<Content.Shared.Damage.Components.DamageableComponent>(damaged.Key).TotalDamage > 0;
                componentPreserved = em.HasComponent<PointLightComponent>(customized.Key);
                tagPreserved = em.System<TagSystem>().HasTag(tagged.Key, new ProtoId<TagPrototype>("Pickaxe"));
                gatherablePreserved = em.EntityExists(gatherableCustomized.Key) &&
                    em.GetComponent<Content.Server.Gatherable.Components.GatherableComponent>(gatherableCustomized.Key)
                        .GatherOffset > 1f;
            });

            Assert.Multiple(() =>
            {
                Assert.That(loadedInitially, Is.InRange(1, 3), "Background terrain must not generate the entire view range in one tick.");
                Assert.That(landingCellLoaded, Is.True,
                    "A downward transition must prepare one exact cell without completing the hidden chunk.");
                Assert.That(teleportedLandingCellLoaded, Is.True,
                    "The current physical viewer chunk must load before a stale Z-eye catches up.");
                Assert.That(pristineUnloaded, Is.True,
                    $"Pristine underground rocks must not accumulate permanently as modified terrain. {unloadDiagnostics}");
                Assert.That(pristineRegenerated, Is.True, unloadDiagnostics);
                Assert.That(minedStayedEmpty, Is.True, "Returning to a chunk must not respawn mined walls.");
                Assert.That(damagePreserved, Is.True);
                Assert.That(componentPreserved, Is.True);
                Assert.That(tagPreserved, Is.True);
                Assert.That(gatherablePreserved, Is.True,
                    "Changing a Gatherable gameplay field must keep the procedural entity persistent.");
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task NormalGhostViewSubscriptionsCannotGenerateAdjacentBiome()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var viewSubscribers = em.System<ViewSubscriberSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        var livingEyes = -1;
        var ghostEyes = -1;
        var loadedChunks = -1;

        try
        {
            await Server.WaitPost(() =>
            {
                var underground = map.CreateMap(runMapInit: false);
                var surface = map.CreateMap();
                biomes.EnsurePlanet(underground, Server.ProtoMan.Index(UndergroundBiome), 42);
                em.EnsureComponent<ClassicBiomeStreamingComponent>(underground);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [underground] = -1,
                });
                if (!map.IsInitialized(underground))
                    map.InitializeMap(underground);

                var ghost = em.SpawnEntity(null, new EntityCoordinates(surface, Vector2.Zero));
                Server.PlayerMan.SetAttachedEntity(session, ghost);
                var viewer = em.GetComponent<ClassicZLevelViewerComponent>(ghost);
                livingEyes = viewer.Eyes.Count;
                em.EnsureComponent<GhostComponent>(ghost);
                ghostEyes = viewer.Eyes.Count;

                // Exercise the defense-in-depth path too: even an arbitrary non-ghost camera
                // subscribed by a normal ghost must not be allowed to generate terrain.
                var auxiliaryViewer = em.SpawnEntity("ClassicZLevelEye",
                    new EntityCoordinates(underground, Vector2.Zero));
                viewSubscribers.AddViewSubscriber(auxiliaryViewer, session);

                biomes.Update(0);
                loadedChunks = em.GetComponent<BiomeComponent>(underground).LoadedChunks.Count;
            });

            Assert.Multiple(() =>
            {
                Assert.That(livingEyes, Is.GreaterThan(0),
                    "The regression setup must begin with a live lower-level Z-eye.");
                Assert.That(ghostEyes, Is.Zero, "Normal ghosts must not receive cross-Z PVS eyes.");
                Assert.That(loadedChunks, Is.Zero,
                    "A normal ghost's auxiliary view subscriptions must not generate biome chunks.");
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task IdleViewerKeepsBoundedAdjacentSupportUntilLookingUp()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var actions = em.System<SharedActionsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        var hadIdleUpperEye = true;
        var lowerCriticalChunks = -1;
        var lowerCriticalEntities = -1;
        var upperLandingChunks = -1;
        var upperLandingEntities = -1;
        var upperPrefetchChunks = -1;
        var upperViewerCells = -1;
        var forcedCursorCells = -1;
        var keptLowerEye = false;
        var gainedUpperEye = false;

        try
        {
            await Server.WaitPost(() =>
            {
                var lower = map.CreateMap(runMapInit: false);
                var current = map.CreateMap();
                var upper = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(lower, Server.ProtoMan.Index(UndergroundBiome), 24);
                biomes.EnsurePlanet(upper, Server.ProtoMan.Index(UndergroundBiome), 42);
                em.GetComponent<BiomeComponent>(upper).ModifiedTiles[Vector2i.Zero] =
                    [new Vector2i(4, 4)];
                var lowerStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(lower);
                lowerStreaming.BackgroundChunksPerTick = 2;
                lowerStreaming.WorkBudget = TimeSpan.Zero;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(upper);
                streaming.BackgroundChunksPerTick = 2;
                // WorkBudget is server-wide for active Classic biomes: one zero budget must stop
                // background work everywhere while still permitting exact safety chunks.
                streaming.WorkBudget = TimeSpan.Zero;

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [lower] = -1,
                    [current] = 0,
                    [upper] = 1,
                });
                if (!map.IsInitialized(lower))
                    map.InitializeMap(lower);
                if (!map.IsInitialized(upper))
                    map.InitializeMap(upper);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(current, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                var lowerEye = viewerComp.Eyes.Single(eye => em.GetComponent<TransformComponent>(eye).MapUid == lower);
                hadIdleUpperEye = viewerComp.Eyes.Any(eye => em.GetComponent<TransformComponent>(eye).MapUid == upper);

                biomes.Update(0);
                var lowerBiome = em.GetComponent<BiomeComponent>(lower);
                lowerCriticalChunks = lowerBiome.LoadedChunks.Count;
                lowerCriticalEntities = lowerBiome.LoadedEntities.Values.Sum(chunk => chunk.Count);
                var upperBiome = em.GetComponent<BiomeComponent>(upper);
                upperLandingChunks = upperBiome.LoadedChunks.Count;
                upperLandingEntities = upperBiome.LoadedEntities.GetValueOrDefault(Vector2i.Zero)?.Count ?? 0;
                upperPrefetchChunks = streaming.AdjacentPrefetchChunks.Count;
                upperViewerCells = streaming.ViewerCells.Count;
                forcedCursorCells = lowerStreaming.PartialLoads.Values.Sum() + streaming.PartialLoads.Values.Sum();

                var action = actions.GetAction(viewerComp.ActionEntity);
                Assert.That(action, Is.Not.Null);
                actions.PerformAction(viewer, action!.Value);

                keptLowerEye = viewerComp.Eyes.Contains(lowerEye);
                gainedUpperEye = viewerComp.UpperEyes.Count == 1 &&
                    viewerComp.UpperEyes.All(eye => em.GetComponent<TransformComponent>(eye).MapUid == upper);
            });

            Assert.Multiple(() =>
            {
                Assert.That(hadIdleUpperEye, Is.False,
                    "The full upper PVS range must stay cold until the player looks up.");
                Assert.That(lowerCriticalChunks, Is.Zero,
                    "Foreground safety cells must not masquerade as a completed chunk.");
                Assert.That(lowerCriticalEntities, Is.InRange(1, 2),
                    "A render-only lower eye needs only the body's exact fall landing cell synchronously.");
                Assert.That(upperLandingChunks, Is.Zero,
                    "Bounded upper support must not masquerade as a completed render chunk.");
                Assert.That(upperLandingEntities, Is.Zero);
                Assert.That(upperPrefetchChunks, Is.EqualTo(4));
                Assert.That(upperViewerCells, Is.Zero,
                    "Upper support must remain background-only until the player looks up.");
                Assert.That(forcedCursorCells, Is.InRange(0, 1),
                    "A zero budget may advance at most one sequential background cell globally.");
                Assert.That(keptLowerEye, Is.True,
                    "Toggling LookUp must not recreate lower eyes or churn their PVS subscriptions.");
                Assert.That(gainedUpperEye, Is.True);
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task DeepLookUpStopsAtOpaqueRoofAndOpensTransparentPath()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var transform = em.System<SharedTransformSystem>();
        var actions = em.System<SharedActionsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var surface = map.CreateMap(runMapInit: false);
                var shallow = map.CreateMap(runMapInit: false);
                var deep = map.CreateMap(runMapInit: false);
                var deepest = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    deep,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var deepStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(deep);
                deepStreaming.BackgroundChunksPerTick = 64;
                deepStreaming.BackgroundCellsPerSlice = 64;
                deepStreaming.WorkBudget = TimeSpan.FromSeconds(1);
                deepStreaming.ZLevelCacheDuration = TimeSpan.FromSeconds(10);
                deepStreaming.MaxZLevelCachedChunks = 64;
                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [shallow] = -1,
                    [deep] = -2,
                    [deepest] = -3,
                }), Is.True);
                foreach (var level in new[] { surface, shallow, deep, deepest })
                    map.InitializeMap(level);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(deepest, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                var lowerEyes = viewerComp.Eyes.ToHashSet();
                Assert.That(viewerComp.UpperEyes, Is.Empty);

                var action = actions.GetAction(viewerComp.ActionEntity);
                Assert.That(action, Is.Not.Null);
                actions.PerformAction(viewer, action!.Value);

                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                    Assert.That(viewerComp.VisibleZLevelsAboveMap, Is.EqualTo(deepest));
                    Assert.That(viewerComp.VisibleZLevelsAboveTile, Is.EqualTo(new Vector2i(4, 4)));
                    Assert.That(zLevels.GetVisibleZLevelsAbove(viewer), Is.Zero);
                    Assert.That(viewerComp.Eyes, Is.EquivalentTo(lowerEyes));
                    Assert.That(em.GetComponent<BiomeComponent>(deep).LoadedChunks, Is.Empty);
                });

                em.GetComponent<BiomeComponent>(deep).ModifiedTiles[new Vector2i(32, 0)] =
                    [new Vector2i(36, 4)];
                transform.SetWorldPosition(viewer, new Vector2(36.5f, 4.5f));

                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(3));
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(3));
                    Assert.That(viewerComp.VisibleZLevelsAboveMap, Is.EqualTo(deepest));
                    Assert.That(viewerComp.VisibleZLevelsAboveTile, Is.EqualTo(new Vector2i(36, 4)));
                    Assert.That(zLevels.GetVisibleZLevelsAbove(viewer), Is.EqualTo(3));
                    Assert.That(viewerComp.UpperEyes
                            .Select(eye => em.GetComponent<TransformComponent>(eye).MapUid),
                        Is.EquivalentTo(new[] { deep, shallow, surface }));
                    Assert.That(viewerComp.Eyes, Is.SupersetOf(lowerEyes));
                });

                biomes.Update(0);
                Assert.That(deepStreaming.PartialLoads.ContainsKey(new Vector2i(32, 0)) ||
                    em.GetComponent<BiomeComponent>(deep).LoadedChunks.Contains(new Vector2i(32, 0)), Is.True);
                transform.SetWorldPosition(viewer, new Vector2(100.5f, 4.5f));
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                    Assert.That(viewerComp.VisibleZLevelsAboveMap, Is.EqualTo(deepest));
                    Assert.That(viewerComp.VisibleZLevelsAboveTile, Is.EqualTo(new Vector2i(100, 4)));
                    Assert.That(zLevels.GetVisibleZLevelsAbove(viewer), Is.Zero);
                    Assert.That(deepStreaming.CachedZChunks, Does.ContainKey(new Vector2i(32, 0)));
                    Assert.That(deepStreaming.CachedZChunks, Does.Not.ContainKey(new Vector2i(96, 0)));
                });

                actions.PerformAction(viewer, action.Value);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(viewerComp.Eyes, Is.EquivalentTo(lowerEyes));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task LookUpRechecksRotatedGridWithinSameWorldCell()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var actions = em.System<SharedActionsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var current = map.CreateMap(runMapInit: false);
                var upper = map.CreateMap(runMapInit: false);
                var upperGrid = map.CreateGridEntity(upper);
                var plating = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                map.SetTile(upperGrid, Vector2i.Zero, plating);
                transform.SetWorldPositionRotation(upperGrid, Vector2.Zero, Angle.FromDegrees(45));

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [current] = 0,
                    [upper] = 1,
                }), Is.True);
                map.InitializeMap(current);
                map.InitializeMap(upper);

                var blockedPosition = map.LocalToWorld(upperGrid, upperGrid.Comp, new Vector2(0.75f, 0.25f));
                var openPosition = new Vector2(0.9f, 0.1f);
                Assert.That(blockedPosition.Floored(), Is.EqualTo(openPosition.Floored()));
                var viewer = em.SpawnEntity("ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(current, blockedPosition));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                var action = actions.GetAction(viewerComp.ActionEntity);
                Assert.That(action, Is.Not.Null);
                actions.PerformAction(viewer, action!.Value);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(zLevels.GetVisibleZLevelsAbove(viewer), Is.Zero);
                });

                transform.SetWorldPosition(viewer, openPosition);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(1));
                    Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(1));
                    Assert.That(zLevels.GetVisibleZLevelsAbove(viewer), Is.EqualTo(1));
                });

                transform.SetWorldPosition(viewer, blockedPosition);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(zLevels.GetVisibleZLevelsAbove(viewer), Is.Zero);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task LookUpTracksStationaryViewerOnMovingParentGridEachTick()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var actions = em.System<SharedActionsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        EntityUid currentGrid = default;
        EntityUid viewer = default;
        EntityUid upperEye = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var current = map.CreateMap(runMapInit: false);
                var upper = map.CreateMap(runMapInit: false);
                var currentGridEnt = map.CreateGridEntity(current);
                currentGrid = currentGridEnt;
                var upperGrid = map.CreateGridEntity(upper);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [current] = 0,
                    [upper] = 1,
                }), Is.True);
                map.InitializeMap(current);
                map.InitializeMap(upper);
                var plating = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                map.SetTile(currentGridEnt, new Vector2i(2, 0), plating);
                map.SetTile(upperGrid, Vector2i.Zero, plating);
                transform.SetWorldPosition(currentGridEnt, new Vector2(-2f, 0f));

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(currentGridEnt, new Vector2(2.5f, 0.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                var action = actions.GetAction(viewerComp.ActionEntity);
                Assert.That(action, Is.Not.Null);
                actions.PerformAction(viewer, action!.Value);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(Vector2.Distance(
                        em.GetComponent<TransformComponent>(viewer).LocalPosition,
                        new Vector2(2.5f, 0.5f)), Is.LessThan(0.0001f));
                });

                transform.SetWorldPosition(currentGrid, Vector2.Zero);
            });

            await Server.WaitRunTicks(1);

            await Server.WaitPost(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(1));
                    Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(1));
                    Assert.That(Vector2.Distance(
                        em.GetComponent<TransformComponent>(viewer).LocalPosition,
                        new Vector2(2.5f, 0.5f)), Is.LessThan(0.0001f));
                    Assert.That(transform.GetWorldPosition(viewerComp.UpperEyes.Single()),
                        Is.EqualTo(transform.GetWorldPosition(viewer)));
                });

                upperEye = viewerComp.UpperEyes.Single();
                transform.SetWorldPosition(currentGrid, new Vector2(0.1f, 0.1f));
            });

            await Server.WaitRunTicks(1);

            await Server.WaitPost(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(1));
                    Assert.That(viewerComp.UpperEyes, Is.EquivalentTo(new[] { upperEye }));
                    Assert.That(Vector2.Distance(
                        em.GetComponent<TransformComponent>(viewer).LocalPosition,
                        new Vector2(2.5f, 0.5f)), Is.LessThan(0.0001f));
                    Assert.That(transform.GetWorldPosition(viewer).Floored(), Is.EqualTo(new Vector2i(2, 0)));
                    Assert.That(transform.GetWorldPosition(upperEye), Is.EqualTo(transform.GetWorldPosition(viewer)));
                });

                transform.SetWorldRotation(currentGrid, Angle.FromDegrees(1));
            });

            await Server.WaitRunTicks(1);

            await Server.WaitPost(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(1));
                    Assert.That(viewerComp.UpperEyes, Is.EquivalentTo(new[] { upperEye }));
                    Assert.That(Vector2.Distance(
                        em.GetComponent<TransformComponent>(viewer).LocalPosition,
                        new Vector2(2.5f, 0.5f)), Is.LessThan(0.0001f));
                    Assert.That(transform.GetWorldPosition(viewer).Floored(), Is.EqualTo(new Vector2i(2, 0)));
                    Assert.That(transform.GetWorldPosition(upperEye), Is.EqualTo(transform.GetWorldPosition(viewer)));
                });

                transform.SetWorldPositionRotation(currentGrid, new Vector2(-2f, 0f), Angle.Zero);
            });

            await Server.WaitRunTicks(1);

            await Server.WaitPost(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(Vector2.Distance(
                        em.GetComponent<TransformComponent>(viewer).LocalPosition,
                        new Vector2(2.5f, 0.5f)), Is.LessThan(0.0001f));
                });

                transform.SetWorldRotation(currentGrid, Angle.FromDegrees(180));
            });

            await Server.WaitRunTicks(1);

            await Server.WaitPost(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(1));
                    Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(1));
                    Assert.That(Vector2.Distance(
                        em.GetComponent<TransformComponent>(viewer).LocalPosition,
                        new Vector2(2.5f, 0.5f)), Is.LessThan(0.0001f));
                    Assert.That(transform.GetWorldPosition(viewerComp.UpperEyes.Single()),
                        Is.EqualTo(transform.GetWorldPosition(viewer)));
                });

                transform.SetWorldRotation(currentGrid, Angle.Zero);
            });

            await Server.WaitRunTicks(1);

            await Server.WaitPost(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.Multiple(() =>
                {
                    Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                    Assert.That(viewerComp.UpperEyes, Is.Empty);
                    Assert.That(Vector2.Distance(
                        em.GetComponent<TransformComponent>(viewer).LocalPosition,
                        new Vector2(2.5f, 0.5f)), Is.LessThan(0.0001f));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task RapidLookToggleReusesCachedUpperBiome()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var actions = em.System<SharedActionsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        EntityUid viewer = default;
        EntityUid upper = default;
        HashSet<EntityUid> cachedEntities = [];

        try
        {
            await Server.WaitPost(() =>
            {
                var current = map.CreateMap();
                upper = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(upper, Server.ProtoMan.Index(UndergroundBiome), 42);
                em.GetComponent<BiomeComponent>(upper).ModifiedTiles[Vector2i.Zero] =
                    [new Vector2i(4, 4)];
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(upper);
                streaming.BackgroundChunksPerTick = 1;
                streaming.BackgroundCellsPerSlice = 1;
                streaming.UnloadEntitiesPerSlice = 64;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.FromSeconds(10);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [current] = 0,
                    [upper] = 1,
                });
                if (!map.IsInitialized(upper))
                    map.InitializeMap(upper);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(current, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                var action = actions.GetAction(viewerComp.ActionEntity);
                Assert.That(action, Is.Not.Null);
                actions.PerformAction(viewer, action!.Value);

                Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(1));
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(upper);
                Assert.That(streaming.PartialLoads, Is.Not.Empty);
                cachedEntities = biome.LoadedEntities.Values.SelectMany(chunk => chunk.Keys).ToHashSet();
                Assert.That(cachedEntities, Is.Not.Empty);

                actions.PerformAction(viewer, action.Value);
                Assert.That(viewerComp.UpperEyes, Is.Empty);
            });

            await Server.WaitRunTicks(3);

            await Server.WaitPost(() =>
            {
                var biome = em.GetComponent<BiomeComponent>(upper);
                var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(upper);
                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedEntities.Values.SelectMany(chunk => chunk.Keys),
                        Is.SupersetOf(cachedEntities));
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                });

                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                var action = actions.GetAction(viewerComp.ActionEntity);
                Assert.That(action, Is.Not.Null);
                actions.PerformAction(viewer, action!.Value);
                Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(1));
                biomes.Update(0);

                Assert.That(
                    biome.LoadedEntities.Values.SelectMany(chunk => chunk.Keys),
                    Is.SupersetOf(cachedEntities),
                    "Rapidly restoring the upper eye must reuse resident biome entities.");
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task RapidLowerPrefetchReturnReusesCachedBiome()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var upper = map.CreateMap(runMapInit: false);
                var lower = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    upper,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    24);
                biomes.EnsurePlanet(lower, Server.ProtoMan.Index(UndergroundBiome), 42);
                var upperStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(upper);
                var lowerStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(lower);
                foreach (var streaming in new[] { upperStreaming, lowerStreaming })
                {
                    streaming.BackgroundChunksPerTick = 4;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.BackgroundEntitySpawnsPerTick = 64;
                    streaming.WorkBudget = TimeSpan.FromSeconds(1);
                    streaming.UnloadDelay = TimeSpan.Zero;
                }
                lowerStreaming.MaxZLevelCachedChunks = 64;
                lowerStreaming.AdjacentPrefetchCacheDuration = TimeSpan.FromSeconds(10);
                lowerStreaming.MaxAdjacentPrefetchCachedChunks = 4;

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [upper] = 0,
                    [lower] = -1,
                }), Is.True);
                map.InitializeMap(upper);
                map.InitializeMap(lower);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(upper, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var lowerBiome = em.GetComponent<BiomeComponent>(lower);
                var initialChunks = new HashSet<Vector2i>
                {
                    new(0, 0),
                    new(0, 8),
                    new(8, 0),
                    new(8, 8),
                };
                for (var i = 0; i < 16 && !lowerBiome.LoadedChunks.ToHashSet().SetEquals(initialChunks); i++)
                    biomes.Update(0);

                Assert.That(lowerBiome.LoadedChunks, Is.EquivalentTo(initialChunks));
                var cachedEntities = lowerBiome.LoadedEntities[Vector2i.Zero].Keys.ToHashSet();
                Assert.That(cachedEntities, Is.Not.Empty);

                transform.SetWorldPosition(viewer, new Vector2(12.5f, 4.5f));
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(lowerStreaming.CachedZChunks, Does.ContainKey(Vector2i.Zero));
                    Assert.That(lowerStreaming.CachedZChunks.Count, Is.LessThanOrEqualTo(4));
                    Assert.That(lowerStreaming.CachedAdjacentPrefetchChunks, Does.Contain(Vector2i.Zero));
                    Assert.That(lowerStreaming.CachedAdjacentPrefetchChunks.Count, Is.LessThanOrEqualTo(4));
                    Assert.That(lowerStreaming.PartialUnloads, Does.Not.ContainKey(Vector2i.Zero));
                });

                transform.SetWorldPosition(viewer, new Vector2(4.5f, 4.5f));
                biomes.Update(0);
                Assert.Multiple(() =>
                {
                    Assert.That(lowerBiome.LoadedEntities[Vector2i.Zero].Keys, Is.EquivalentTo(cachedEntities));
                    Assert.That(lowerStreaming.CachedZChunks, Does.Not.ContainKey(Vector2i.Zero));
                    Assert.That(lowerStreaming.CachedAdjacentPrefetchChunks, Does.Not.Contain(Vector2i.Zero));
                    Assert.That(lowerStreaming.PartialUnloads, Does.Not.ContainKey(Vector2i.Zero));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task DirectUnanchoredBiomeRootIsAnchored()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingUnanchoredRootTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.ViewerSafetyRadius = 0;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(0.5f, 0.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var root = biome.LoadedEntities[Vector2i.Zero]
                    .Single(pair => pair.Value == Vector2i.Zero).Key;
                Assert.Multiple(() =>
                {
                    Assert.That(em.GetComponent<MetaDataComponent>(root).EntityPrototype?.ID,
                        Is.EqualTo("ClassicStreamingUnanchoredRootTest"));
                    Assert.That(em.GetComponent<TransformComponent>(root).Anchored, Is.True);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedSpawnerOutputMutationPreservesSourceCell(bool deleteOutput)
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var metadata = em.System<MetaDataSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;
        EntityUid root = default;
        EntityUid output = default;
        var cell = Vector2i.Zero;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingPersistentSpawnerTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.ViewerSafetyRadius = 0;
                streaming.UnloadEntitiesPerSlice = 1;
                streaming.UnloadEntitiesPerTick = 1;
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(0.5f, 0.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var cellEntities = biome.LoadedEntities[Vector2i.Zero]
                    .Where(pair => pair.Value == cell)
                    .Select(pair => pair.Key)
                    .ToArray();
                root = cellEntities.Single(uid =>
                    em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ==
                    "ClassicStreamingPersistentSpawnerSourceTest");
                output = cellEntities.Single(uid =>
                    em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ==
                    "ClassicStreamingUnanchoredOutputTest");

                if (deleteOutput)
                    em.DeleteEntity(output);
                else
                    metadata.SetEntityName(output, "modified biome output");

                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                for (var i = 0; i < 16 &&
                     (biome.LoadedChunks.Count > 0 ||
                      streaming.PartialLoads.Count > 0 ||
                      streaming.PartialUnloads.Count > 0 ||
                      streaming.PendingUnloads.Count > 0); i++)
                {
                    biomes.Update(0);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(em.EntityExists(root), Is.True);
                    Assert.That(biome.ModifiedTiles.Values.SelectMany(chunk => chunk), Does.Contain(cell));
                    Assert.That(biome.LoadedEntities, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                });

                streaming.WorkBudget = TimeSpan.Zero;
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var roots = 0;
                var outputs = 0;
                var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var meta, out var xform))
                {
                    if (xform.ParentUid != terrain)
                        continue;
                    if (meta.EntityPrototype?.ID == "ClassicStreamingPersistentSpawnerSourceTest")
                        roots++;
                    else if (meta.EntityPrototype?.ID == "ClassicStreamingUnanchoredOutputTest")
                        outputs++;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(roots, Is.EqualTo(1));
                    Assert.That(outputs, Is.EqualTo(deleteOutput ? 0 : 1));
                    Assert.That(em.EntityExists(root), Is.True);
                    Assert.That(biome.ModifiedTiles.Values.SelectMany(chunk => chunk), Does.Contain(cell));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [Test]
    public async Task PristineSpawnerOutputUnloadsButDeletedOutputStaysModified()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;
        var pristineTile = new Vector2i(4, 4);
        var deletedTile = new Vector2i(12, 4);

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingSpawnerTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.ViewerSafetyRadius = 0;
                streaming.UnloadEntitiesPerSlice = 64;
                streaming.UnloadEntitiesPerTick = 64;
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);
            });

            await Server.WaitRunTicks(1);
            await Server.WaitPost(() =>
            {
                var biome = em.GetComponent<BiomeComponent>(terrain);
                var output = biome.LoadedEntities.Values
                    .SelectMany(chunk => chunk)
                    .Single(pair => pair.Value == pristineTile).Key;
                Assert.Multiple(() =>
                {
                    Assert.That(em.GetComponent<MetaDataComponent>(output).EntityPrototype?.ID,
                        Is.EqualTo("FloraTree"));
                    Assert.That(em.GetComponent<RandomSpriteComponent>(output).Selected, Is.Not.Empty);
                });

                var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                for (var i = 0; i < 16 &&
                     (biome.LoadedChunks.Count > 0 ||
                      streaming.PartialLoads.Count > 0 ||
                      streaming.PartialUnloads.Count > 0 ||
                      streaming.PendingUnloads.Count > 0); i++)
                {
                    biomes.Update(0);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks, Is.Empty);
                    Assert.That(biome.LoadedEntities, Is.Empty);
                    Assert.That(biome.ModifiedTiles, Is.Empty);
                    Assert.That(streaming.PartialLoads, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                    Assert.That(em.GetComponent<MapGridComponent>(terrain).ChunkCount, Is.Zero);
                });

                transform.SetCoordinates(viewer, new EntityCoordinates(terrain, new Vector2(12.5f, 4.5f)));
                streaming.WorkBudget = TimeSpan.Zero;
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);
            });

            await Server.WaitRunTicks(1);
            await Server.WaitPost(() =>
            {
                var biome = em.GetComponent<BiomeComponent>(terrain);
                var output = biome.LoadedEntities.Values
                    .SelectMany(chunk => chunk)
                    .Single(pair => pair.Value == deletedTile).Key;
                em.DeleteEntity(output);

                var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                for (var i = 0; i < 16 &&
                     (biome.LoadedChunks.Count > 0 ||
                      streaming.PartialLoads.Count > 0 ||
                      streaming.PartialUnloads.Count > 0 ||
                      streaming.PendingUnloads.Count > 0); i++)
                {
                    biomes.Update(0);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedEntities, Is.Empty);
                    Assert.That(biome.ModifiedTiles.Values.SelectMany(chunk => chunk), Does.Contain(deletedTile));
                    Assert.That(streaming.PartialLoads, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [Test]
    public async Task PersistentSpawnerRestoresUnanchoredOutputWithoutDirtyingCell()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;
        var origin = Vector2i.Zero;
        var cell = Vector2i.Zero;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingPersistentSpawnerTest"),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = 64;
                streaming.ViewerSafetyRadius = 0;
                streaming.UnloadEntitiesPerSlice = 1;
                streaming.UnloadEntitiesPerTick = 1;
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(0.5f, 0.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var cellEntities = biome.LoadedEntities[origin]
                    .Where(pair => pair.Value == cell)
                    .Select(pair => pair.Key)
                    .ToArray();
                Assert.That(cellEntities, Has.Length.EqualTo(2));
                var output = cellEntities.Single(uid =>
                    em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ==
                    "ClassicStreamingUnanchoredOutputTest");
                Assert.That(em.GetComponent<TransformComponent>(output).Anchored, Is.False);

                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                transform.SetWorldPosition(viewer, new Vector2(1000.5f, 1000.5f));
                biomes.Update(0);

                Assert.Multiple(() =>
                {
                    Assert.That(streaming.PartialUnloads.TryGetValue(origin, out var unloading), Is.True);
                    Assert.That(unloading!.RemovedEntityCells, Does.Contain(cell));
                    Assert.That(biome.ModifiedTiles, Is.Empty);
                });

                streaming.WorkBudget = TimeSpan.Zero;
                transform.SetWorldPosition(viewer, new Vector2(0.5f, 0.5f));
                biomes.Update(0);

                cellEntities = biome.LoadedEntities[origin]
                    .Where(pair => pair.Value == cell)
                    .Select(pair => pair.Key)
                    .ToArray();
                output = cellEntities.Single(uid =>
                    em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ==
                    "ClassicStreamingUnanchoredOutputTest");
                Assert.Multiple(() =>
                {
                    Assert.That(cellEntities, Has.Length.EqualTo(2));
                    Assert.That(em.GetComponent<TransformComponent>(output).Anchored, Is.False);
                    Assert.That(streaming.PartialUnloads, Does.Not.ContainKey(origin));
                    Assert.That(biome.ModifiedTiles, Is.Empty);
                });

                streaming.UnloadEntitiesPerSlice = 64;
                streaming.UnloadEntitiesPerTick = 64;
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                for (var i = 0; i < 16 &&
                     (biome.LoadedChunks.Count > 0 ||
                      streaming.PartialLoads.Count > 0 ||
                      streaming.PartialUnloads.Count > 0 ||
                      streaming.PendingUnloads.Count > 0); i++)
                {
                    biomes.Update(0);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks, Is.Empty);
                    Assert.That(biome.LoadedEntities, Is.Empty);
                    Assert.That(biome.ModifiedTiles, Is.Empty);
                    Assert.That(streaming.PartialLoads, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [Test]
    public async Task TimedAnchoredEffectDoesNotPermanentlyDirtyBiomeCell()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;
        EntityUid effect = default;
        var cell = Vector2i.Zero;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    42);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.ViewerSafetyRadius = 0;
                streaming.UnloadEntitiesPerSlice = 64;
                streaming.UnloadEntitiesPerTick = 64;
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(0.5f, 0.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);
                effect = em.SpawnEntity(
                    "ClassicStreamingTimedAnchoredEffectTest",
                    new EntityCoordinates(terrain, new Vector2(0.5f, 0.5f)));
                Assert.That(em.GetComponent<TransformComponent>(effect).Anchored, Is.True);

                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                for (var i = 0; i < 16 &&
                     (streaming.PartialLoads.Count > 0 ||
                      streaming.PartialUnloads.Count > 0 ||
                      streaming.PendingUnloads.Count > 0); i++)
                {
                    biomes.Update(0);
                }

                var biome = em.GetComponent<BiomeComponent>(terrain);
                Assert.Multiple(() =>
                {
                    Assert.That(em.EntityExists(effect), Is.True);
                    Assert.That(em.GetComponent<TransformComponent>(effect).Anchored, Is.True);
                    Assert.That(map.GetTileRef(terrain, grid, cell).Tile.IsEmpty, Is.True);
                    Assert.That(biome.ModifiedTiles, Is.Empty);
                    Assert.That(streaming.PartialLoads, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [Test]
    public async Task DeepUndergroundViewerRetainsBoundedUpperColumn()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var surface = map.CreateMap(runMapInit: false);
                var upper = map.CreateMap(runMapInit: false);
                var lower = map.CreateMap(runMapInit: false);
                var deepest = map.CreateMap(runMapInit: false);
                var maps = new[] { surface, upper, lower, deepest };
                for (var i = 0; i < maps.Length; i++)
                    biomes.EnsurePlanet(maps[i], Server.ProtoMan.Index(UndergroundBiome), 24 + i);
                var streamings = maps
                    .Select(uid => em.EnsureComponent<ClassicBiomeStreamingComponent>(uid))
                    .ToArray();
                foreach (var streaming in streamings)
                {
                    streaming.BackgroundChunksPerTick = 64;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.BackgroundEntitySpawnsPerTick = 64;
                    streaming.UnloadEntitiesPerSlice = 64;
                    streaming.UnloadEntitiesPerTick = 1024;
                    streaming.UnloadCellsPerSlice = 64;
                    streaming.UnloadDelay = TimeSpan.Zero;
                    streaming.WorkBudget = TimeSpan.FromSeconds(1);
                    streaming.ZLevelCacheDuration = TimeSpan.Zero;
                    streaming.AdjacentPrefetchCacheDuration = TimeSpan.Zero;
                }

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [upper] = -1,
                    [lower] = -2,
                    [deepest] = -3,
                }), Is.True);
                foreach (var uid in maps)
                    map.InitializeMap(uid);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(deepest, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var retainedChunks = new HashSet<Vector2i>
                {
                    new(0, 0),
                    new(0, 8),
                    new(8, 0),
                    new(8, 8),
                };
                var supported = maps.Take(3)
                    .Select(uid => (
                        Biome: em.GetComponent<BiomeComponent>(uid),
                        Streaming: em.GetComponent<ClassicBiomeStreamingComponent>(uid)))
                    .ToArray();
                for (var i = 0; i < 64 && supported.Any(entry => !retainedChunks.IsSubsetOf(entry.Biome.LoadedChunks)); i++)
                    biomes.Update(0);

                var retainedEntities = supported.Select(entry => retainedChunks.ToDictionary(
                        chunk => chunk,
                        chunk => entry.Biome.LoadedEntities.GetValueOrDefault(chunk)?.Keys.ToHashSet() ?? []))
                    .ToArray();
                Assert.That(retainedEntities.SelectMany(chunks => chunks.Values).SelectMany(entities => entities),
                    Is.Not.Empty);
                for (var i = 0; i < 64; i++)
                    biomes.Update(0);

                for (var level = 0; level < supported.Length; level++)
                {
                    var entry = supported[level];
                    Assert.Multiple(() =>
                    {
                        Assert.That(entry.Streaming.AdjacentPrefetchChunks, Is.EquivalentTo(retainedChunks),
                            $"level={level}");
                        Assert.That(entry.Streaming.ViewerCells, Is.Empty, $"level={level}");
                        Assert.That(retainedChunks, Is.SubsetOf(entry.Biome.LoadedChunks), $"level={level}");
                        Assert.That(entry.Streaming.PartialUnloads.Keys.Intersect(retainedChunks), Is.Empty,
                            $"level={level}");
                        Assert.That(entry.Streaming.PendingUnloads.Keys.Intersect(retainedChunks), Is.Empty,
                            $"level={level}");
                    });
                    foreach (var (chunk, entities) in retainedEntities[level])
                    {
                        Assert.That(entry.Biome.LoadedEntities.GetValueOrDefault(chunk)?.Keys.ToHashSet() ?? [],
                            Is.SupersetOf(entities), $"level={level}, chunk={chunk}");
                    }
                }
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task SustainedDeepTravelKeepsUpperColumnBoundedAndDrains()
    {
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetMaxUpdateRange, 16f);
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetPvsPriorityRange, 16f);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        EntityUid viewer = default;
        EntityUid[] supportedMaps = [];

        try
        {
            await Server.WaitPost(() =>
            {
                var surface = map.CreateMap(runMapInit: false);
                var upper = map.CreateMap(runMapInit: false);
                var lower = map.CreateMap(runMapInit: false);
                var deepest = map.CreateMap(runMapInit: false);
                supportedMaps = [surface, upper, lower];
                foreach (var uid in supportedMaps)
                {
                    biomes.EnsurePlanet(uid, Server.ProtoMan.Index(UndergroundBiome), 42);
                    em.GetComponent<MapGridComponent>(uid).CanSplit = false;
                }
                biomes.EnsurePlanet(
                    deepest,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    24);
                em.GetComponent<MapGridComponent>(deepest).CanSplit = false;

                foreach (var uid in supportedMaps.Append(deepest))
                {
                    var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(uid);
                    streaming.BackgroundChunksPerTick = 4;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.BackgroundEntitySpawnsPerTick = 4;
                    streaming.UnloadEntitiesPerSlice = 4;
                    streaming.UnloadEntitiesPerTick = 16;
                    streaming.UnloadCellsPerSlice = 16;
                    streaming.UnloadDelay = TimeSpan.Zero;
                    streaming.MaxUnloadBacklogBeforeThrottling = 2;
                    streaming.WorkBudget = TimeSpan.FromMilliseconds(1);
                    streaming.AdjacentLevelPrefetchRadius = 4;
                    streaming.ZLevelCacheDuration = TimeSpan.FromSeconds(10);
                    streaming.MaxZLevelCachedChunks = 64;
                    streaming.AdjacentPrefetchCacheDuration = TimeSpan.FromSeconds(1);
                    streaming.MaxAdjacentPrefetchCachedChunks = 8;
                }

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [surface] = 0,
                    [upper] = -1,
                    [lower] = -2,
                    [deepest] = -3,
                }), Is.True);
                foreach (var uid in supportedMaps.Append(deepest))
                    map.InitializeMap(uid);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(deepest, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);

                for (var i = 0; i < 48; i++)
                {
                    transform.SetWorldPosition(viewer, new Vector2(i * 48f + 4.5f, 4.5f));
                    biomes.Update(0);
                    foreach (var uid in supportedMaps)
                    {
                        var biome = em.GetComponent<BiomeComponent>(uid);
                        var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(uid);
                        var resident = biome.LoadedChunks
                            .Concat(biome.LoadedEntities.Keys)
                            .Concat(biome.LoadedDecals.Keys)
                            .Concat(streaming.PartialLoads.Keys)
                            .Concat(streaming.PartialUnloads.Keys)
                            .Concat(streaming.PendingUnloads.Keys)
                            .Concat(streaming.CachedZChunks.Keys)
                            .Concat(streaming.PriorityLoadedCells.Keys)
                            .Concat(streaming.MaterializedCells.Keys)
                            .Concat(streaming.AdjacentPrefetchChunks)
                            .ToHashSet();

                        Assert.Multiple(() =>
                        {
                            Assert.That(streaming.AdjacentPrefetchChunks, Has.Count.EqualTo(4),
                                $"step={i}, map={uid}");
                            Assert.That(streaming.ViewerCells, Is.Empty, $"step={i}, map={uid}");
                            Assert.That(streaming.CachedAdjacentPrefetchChunks.Count, Is.LessThanOrEqualTo(8),
                                $"step={i}, map={uid}");
                            Assert.That(streaming.CachedZChunks.Count, Is.LessThanOrEqualTo(8),
                                $"step={i}, map={uid}");
                            Assert.That(resident.Count, Is.LessThanOrEqualTo(24), $"step={i}, map={uid}");
                        });
                    }
                }

                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                em.DeleteEntity(viewer);
                viewer = EntityUid.Invalid;
                biomes.Update(0);
            });

            var drainTicks = (int) Math.Ceiling(3d / Server.Timing.TickPeriod.TotalSeconds);
            await Server.WaitRunTicks(drainTicks);

            await Server.WaitPost(() =>
            {
                foreach (var uid in supportedMaps)
                {
                    var biome = em.GetComponent<BiomeComponent>(uid);
                    var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(uid);
                    Assert.Multiple(() =>
                    {
                        Assert.That(biome.LoadedChunks, Is.Empty, $"map={uid}");
                        Assert.That(biome.LoadedEntities, Is.Empty, $"map={uid}");
                        Assert.That(biome.LoadedDecals, Is.Empty, $"map={uid}");
                        Assert.That(streaming.PartialLoads, Is.Empty, $"map={uid}");
                        Assert.That(streaming.PartialUnloads, Is.Empty, $"map={uid}");
                        Assert.That(streaming.PendingUnloads, Is.Empty, $"map={uid}");
                        Assert.That(streaming.CachedZChunks, Is.Empty, $"map={uid}");
                        Assert.That(streaming.CachedAdjacentPrefetchChunks, Is.Empty, $"map={uid}");
                        Assert.That(streaming.AdjacentPrefetchChunks, Is.Empty, $"map={uid}");
                        Assert.That(streaming.PreviousAdjacentPrefetchChunks, Is.Empty, $"map={uid}");
                        Assert.That(em.GetComponent<MapGridComponent>(uid).ChunkCount, Is.Zero, $"map={uid}");
                    });
                }
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
            });
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task SustainedOpaqueSurfaceTravelKeepsLowerPrefetchBoundedAndDrains()
    {
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetMaxUpdateRange, 16f);
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetPvsPriorityRange, 16f);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid network = default;
        EntityUid lower = default;
        EntityUid viewer = default;
        var maxResident = 0;
        var maxCached = 0;

        try
        {
            await Server.WaitPost(() =>
            {
                var upper = map.CreateMap(runMapInit: false);
                lower = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    upper,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    24);
                biomes.EnsurePlanet(lower, Server.ProtoMan.Index(UndergroundBiome), 42);
                em.GetComponent<MapGridComponent>(upper).CanSplit = false;
                em.GetComponent<MapGridComponent>(lower).CanSplit = false;
                var upperStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(upper);
                var lowerStreaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(lower);
                foreach (var streaming in new[] { upperStreaming, lowerStreaming })
                {
                    streaming.BackgroundChunksPerTick = 4;
                    streaming.BackgroundCellsPerSlice = 64;
                    streaming.BackgroundEntitySpawnsPerTick = 4;
                    streaming.UnloadEntitiesPerSlice = 4;
                    streaming.UnloadEntitiesPerTick = 16;
                    streaming.UnloadCellsPerSlice = 16;
                    streaming.UnloadDelay = TimeSpan.Zero;
                    streaming.MaxUnloadBacklogBeforeThrottling = 2;
                    streaming.WorkBudget = TimeSpan.FromMilliseconds(1);
                }
                lowerStreaming.AdjacentLevelPrefetchRadius = 4;
                lowerStreaming.ZLevelCacheDuration = TimeSpan.FromSeconds(10);
                lowerStreaming.MaxZLevelCachedChunks = 64;
                lowerStreaming.AdjacentPrefetchCacheDuration = TimeSpan.FromSeconds(1);
                lowerStreaming.MaxAdjacentPrefetchCachedChunks = 8;

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [upper] = 0,
                    [lower] = -1,
                }), Is.True);
                map.InitializeMap(upper);
                map.InitializeMap(lower);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(upper, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var lowerBiome = em.GetComponent<BiomeComponent>(lower);

                for (var i = 0; i < 64; i++)
                {
                    transform.SetWorldPosition(viewer, new Vector2(i * 48f + 4.5f, 4.5f));
                    biomes.Update(0);
                    var resident = lowerBiome.LoadedChunks
                        .Concat(lowerBiome.LoadedEntities.Keys)
                        .Concat(lowerBiome.LoadedDecals.Keys)
                        .Concat(lowerStreaming.PartialLoads.Keys)
                        .Concat(lowerStreaming.PartialUnloads.Keys)
                        .Concat(lowerStreaming.PendingUnloads.Keys)
                        .Concat(lowerStreaming.CachedZChunks.Keys)
                        .Concat(lowerStreaming.PriorityLoadedCells.Keys)
                        .Concat(lowerStreaming.MaterializedCells.Keys)
                        .Concat(lowerStreaming.AdjacentPrefetchChunks)
                        .ToHashSet();
                    maxResident = Math.Max(maxResident, resident.Count);
                    maxCached = Math.Max(maxCached, lowerStreaming.CachedAdjacentPrefetchChunks.Count);

                    Assert.Multiple(() =>
                    {
                        Assert.That(lowerStreaming.AdjacentPrefetchChunks, Has.Count.EqualTo(4), $"step={i}");
                        Assert.That(lowerStreaming.ViewerCells, Is.Empty, $"step={i}");
                        Assert.That(lowerStreaming.CachedAdjacentPrefetchChunks.Count, Is.LessThanOrEqualTo(8), $"step={i}");
                        Assert.That(lowerStreaming.CachedZChunks.Count, Is.LessThanOrEqualTo(8), $"step={i}");
                        Assert.That(resident.Count, Is.LessThanOrEqualTo(24), $"step={i}");
                        Assert.That(lowerBiome.ModifiedTiles, Is.Empty, $"step={i}");
                    });
                }

                Assert.Multiple(() =>
                {
                    Assert.That(maxResident, Is.InRange(1, 24));
                    Assert.That(maxCached, Is.InRange(1, 8));
                });

                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                em.DeleteEntity(viewer);
                viewer = EntityUid.Invalid;
                biomes.Update(0);
            });

            var drainTicks = (int) Math.Ceiling(3d / Server.Timing.TickPeriod.TotalSeconds);
            await Server.WaitRunTicks(drainTicks);

            await Server.WaitPost(() =>
            {
                var lowerBiome = em.GetComponent<BiomeComponent>(lower);
                var lowerStreaming = em.GetComponent<ClassicBiomeStreamingComponent>(lower);
                Assert.Multiple(() =>
                {
                    Assert.That(lowerBiome.LoadedChunks, Is.Empty);
                    Assert.That(lowerBiome.LoadedEntities, Is.Empty);
                    Assert.That(lowerBiome.LoadedDecals, Is.Empty);
                    Assert.That(lowerBiome.ModifiedTiles, Is.Empty);
                    Assert.That(lowerStreaming.PartialLoads, Is.Empty);
                    Assert.That(lowerStreaming.PartialUnloads, Is.Empty);
                    Assert.That(lowerStreaming.PendingUnloads, Is.Empty);
                    Assert.That(lowerStreaming.PriorityLoadedCells, Is.Empty);
                    Assert.That(lowerStreaming.MaterializedCells, Is.Empty);
                    Assert.That(lowerStreaming.CachedZChunks, Is.Empty);
                    Assert.That(lowerStreaming.CachedAdjacentPrefetchChunks, Is.Empty);
                    Assert.That(lowerStreaming.AdjacentPrefetchChunks, Is.Empty);
                    Assert.That(lowerStreaming.PreviousAdjacentPrefetchChunks, Is.Empty);
                    Assert.That(em.GetComponent<MapGridComponent>(lower).ChunkCount, Is.Zero);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
            });
            if (network != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task SustainedDistantMovementKeepsAndDrainsBoundedBiomeWorkingSet()
    {
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetMaxUpdateRange, 16f);
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetPvsPriorityRange, 16f);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        var backlogLimit = 2;
        var backgroundChunksPerTick = 4;
        var backgroundEntitySpawnsPerTick = 4;
        var viewerSafetyRadius = 1;
        var maxZLevelCachedChunks = 2;
        var residentLimit = backlogLimit + backgroundChunksPerTick + maxZLevelCachedChunks;
        var entitiesPerResident = (viewerSafetyRadius * 2 + 1) * (viewerSafetyRadius * 2 + 1) +
                                  backgroundEntitySpawnsPerTick * 2;
        var maxResident = 0;
        var maxLoadedEntities = 0;
        var maxPendingUnloads = 0;
        var maxPartialLoads = 0;
        var maxPartialUnloads = 0;
        var maxCachedZChunks = 0;
        var backgroundSpawnObserved = false;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(terrain, Server.ProtoMan.Index(UndergroundBiome), 42);
                em.GetComponent<MapGridComponent>(terrain).CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = backgroundChunksPerTick;
                streaming.BackgroundCellsPerSlice = 64;
                streaming.BackgroundEntitySpawnsPerTick = backgroundEntitySpawnsPerTick;
                streaming.ViewerSafetyRadius = viewerSafetyRadius;
                streaming.UnloadEntitiesPerSlice = 4;
                streaming.UnloadEntitiesPerTick = 16;
                streaming.UnloadCellsPerSlice = 16;
                streaming.UnloadDelay = TimeSpan.FromSeconds(1);
                streaming.MaxUnloadBacklogBeforeThrottling = backlogLimit;
                streaming.ZLevelCacheDuration = TimeSpan.FromSeconds(1);
                streaming.MaxZLevelCachedChunks = maxZLevelCachedChunks;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                var viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var biome = em.GetComponent<BiomeComponent>(terrain);

                void SampleWorkingSet()
                {
                    var resident = biome.LoadedChunks
                        .Concat(biome.LoadedEntities.Keys)
                        .Concat(streaming.PartialLoads.Keys)
                        .Concat(streaming.PartialUnloads.Keys)
                        .Concat(streaming.PendingUnloads.Keys)
                        .Concat(streaming.CachedZChunks.Keys)
                        .ToHashSet();
                    maxResident = Math.Max(maxResident, resident.Count);
                    maxLoadedEntities = Math.Max(
                        maxLoadedEntities,
                        biome.LoadedEntities.Values.Sum(chunk => chunk.Count));
                    maxPendingUnloads = Math.Max(maxPendingUnloads, streaming.PendingUnloads.Count);
                    maxPartialLoads = Math.Max(maxPartialLoads, streaming.PartialLoads.Count);
                    maxPartialUnloads = Math.Max(maxPartialUnloads, streaming.PartialUnloads.Count);
                    maxCachedZChunks = Math.Max(maxCachedZChunks, streaming.CachedZChunks.Count);
                }

                for (var i = 0; i < 48; i++)
                {
                    var position = new Vector2(i * 48f + 4.5f, 4.5f);
                    biomes.CacheClassicZLevel(viewer);
                    transform.SetWorldPosition(viewer, position);
                    biomes.Update(0);
                    SampleWorkingSet();
                    var entitiesBeforeBackground = biome.LoadedEntities.Values
                        .SelectMany(chunk => chunk.Keys)
                        .ToHashSet();
                    biomes.Update(0);
                    SampleWorkingSet();
                    var entitiesAfterBackground = biome.LoadedEntities.Values
                        .SelectMany(chunk => chunk.Keys)
                        .ToHashSet();
                    var spawned = entitiesAfterBackground.Except(entitiesBeforeBackground).Count();
                    backgroundSpawnObserved |= spawned > 0;
                    Assert.That(
                        spawned,
                        Is.InRange(0, backgroundEntitySpawnsPerTick));
                }

                Assert.Multiple(() =>
                {
                    Assert.That(backgroundSpawnObserved, Is.True);
                    Assert.That(maxLoadedEntities, Is.GreaterThan(0));
                    Assert.That(maxResident, Is.LessThanOrEqualTo(residentLimit));
                    Assert.That(maxLoadedEntities, Is.LessThanOrEqualTo(residentLimit * entitiesPerResident));
                    Assert.That(maxPendingUnloads, Is.LessThanOrEqualTo(residentLimit));
                    Assert.That(maxPartialLoads, Is.LessThanOrEqualTo(residentLimit));
                    Assert.That(maxPartialUnloads, Is.LessThanOrEqualTo(residentLimit));
                    Assert.That(maxCachedZChunks, Is.InRange(1, maxZLevelCachedChunks));
                });

                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
            });

            var drainTicks = (int) Math.Ceiling(3d / Server.Timing.TickPeriod.TotalSeconds);
            await Server.WaitRunTicks(drainTicks);

            await Server.WaitPost(() =>
            {
                var biome = em.GetComponent<BiomeComponent>(terrain);
                var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(terrain);
                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks, Is.Empty);
                    Assert.That(biome.LoadedEntities, Is.Empty);
                    Assert.That(biome.LoadedDecals, Is.Empty);
                    Assert.That(streaming.PartialLoads, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                    Assert.That(streaming.PriorityLoadedCells, Is.Empty);
                    Assert.That(streaming.MaterializedCells, Is.Empty);
                    Assert.That(streaming.CachedZChunks, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }

    [Test]
    public async Task RapidTravelWithSlowUnloadDoesNotAccumulatePartialChunks()
    {
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetMaxUpdateRange, 16f);
        await OverrideCVar(Side.Server, Robust.Shared.CVars.NetPvsPriorityRange, 16f);
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var transform = em.System<SharedTransformSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        EntityUid terrain = default;
        EntityUid viewer = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index<BiomeTemplatePrototype>("ClassicStreamingTileOnlyTest"),
                    24);
                em.GetComponent<MapGridComponent>(terrain).CanSplit = false;
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundCellsPerSlice = 1;
                streaming.ViewerSafetyRadius = 1;
                streaming.UnloadEntitiesPerSlice = 1;
                streaming.UnloadEntitiesPerTick = 1;
                streaming.UnloadCellsPerSlice = 4;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.MaxUnloadBacklogBeforeThrottling = 2;
                streaming.ZLevelCacheDuration = TimeSpan.Zero;
                streaming.MaxZLevelCachedChunks = 0;
                streaming.WorkBudget = TimeSpan.Zero;
                map.InitializeMap(terrain);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(terrain, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var biome = em.GetComponent<BiomeComponent>(terrain);
                var firstHalfResident = 0;
                var secondHalfResident = 0;
                var firstHalfPending = 0;
                var secondHalfPending = 0;

                for (var i = 0; i < 96; i++)
                {
                    transform.SetWorldPosition(viewer, new Vector2(i * 48f + 4.5f, 4.5f));
                    biomes.Update(0);
                    if (i != 47 && i != 95)
                        continue;

                    var resident = biome.LoadedChunks
                        .Concat(streaming.PartialLoads.Keys)
                        .Concat(streaming.PartialUnloads.Keys)
                        .Concat(streaming.PendingUnloads.Keys)
                        .ToHashSet();
                    if (i == 47)
                    {
                        firstHalfResident = resident.Count;
                        firstHalfPending = streaming.PendingUnloads.Count;
                    }
                    else
                    {
                        secondHalfResident = resident.Count;
                        secondHalfPending = streaming.PendingUnloads.Count;
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(firstHalfResident, Is.GreaterThan(0));
                    Assert.That(secondHalfResident, Is.LessThanOrEqualTo(firstHalfResident + 8),
                        $"resident chunks grew from {firstHalfResident} to {secondHalfResident}");
                    Assert.That(secondHalfPending, Is.LessThanOrEqualTo(firstHalfPending + 8),
                        $"pending unloads grew from {firstHalfPending} to {secondHalfPending}");
                });

                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                streaming.UnloadCellsPerSlice = 64;
                streaming.UnloadEntitiesPerTick = 64;
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                em.DeleteEntity(viewer);
                viewer = EntityUid.Invalid;
                for (var i = 0; i < 128 &&
                     (streaming.PartialLoads.Count > 0 ||
                      streaming.PartialUnloads.Count > 0 ||
                      streaming.PendingUnloads.Count > 0); i++)
                {
                    biomes.Update(0);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedChunks, Is.Empty);
                    Assert.That(streaming.PartialLoads, Is.Empty);
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                    Assert.That(streaming.PendingUnloads, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(session, originalViewer);
                if (viewer != EntityUid.Invalid && em.EntityExists(viewer))
                    em.DeleteEntity(viewer);
                if (terrain != EntityUid.Invalid && em.EntityExists(terrain))
                    em.DeleteEntity(terrain);
            });
        }
    }

    [Test]
    public async Task MovingBetweenZLevelsCachesPreviousBiome()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        Entity<ClassicZMapNetworkComponent> network = default;
        EntityUid viewer = default;
        EntityUid source = default;
        HashSet<EntityUid> cachedEntities = [];

        try
        {
            await Server.WaitPost(() =>
            {
                var target = map.CreateMap();
                source = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(source, Server.ProtoMan.Index(UndergroundBiome), 42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(source);
                streaming.BackgroundChunksPerTick = 0;
                streaming.BackgroundCellsPerSlice = 1;
                streaming.UnloadEntitiesPerSlice = 64;
                streaming.WorkBudget = TimeSpan.Zero;
                streaming.UnloadDelay = TimeSpan.Zero;
                streaming.ZLevelCacheDuration = TimeSpan.FromSeconds(10);

                network = zLevels.CreateMapNetwork();
                zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [target] = 0,
                    [source] = 1,
                });
                if (!map.IsInitialized(source))
                    map.InitializeMap(source);

                viewer = em.SpawnEntity(
                    "ClassicStreamingPhysicalViewerTest",
                    new EntityCoordinates(source, new Vector2(4.5f, 4.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                biomes.Update(0);

                var biome = em.GetComponent<BiomeComponent>(source);
                Assert.That(streaming.PartialLoads, Is.Not.Empty);
                cachedEntities = biome.LoadedEntities.Values.SelectMany(chunk => chunk.Keys).ToHashSet();
                Assert.That(cachedEntities, Is.Not.Empty);

                Assert.That(zLevels.TryMoveDown(viewer), Is.True);
                Assert.That(streaming.CachedZChunks, Is.Not.Empty);
            });

            await Server.WaitRunTicks(3);

            await Server.WaitPost(() =>
            {
                var biome = em.GetComponent<BiomeComponent>(source);
                var streaming = em.GetComponent<ClassicBiomeStreamingComponent>(source);
                Assert.Multiple(() =>
                {
                    Assert.That(biome.LoadedEntities.Values.SelectMany(chunk => chunk.Keys),
                        Is.SupersetOf(cachedEntities));
                    Assert.That(streaming.PartialUnloads, Is.Empty);
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (network.Owner != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(network));
                await Server.WaitRunTicks(2);
            }
        }
    }
}
