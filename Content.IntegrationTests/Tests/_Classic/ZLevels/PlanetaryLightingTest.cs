using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Classic.ZLevels.Core;
using Content.Server.Light.EntitySystems;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Roof;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

[TestFixture]
public sealed class PlanetaryLightingTest : GameTest
{
    private static readonly SunShadowCycleDirection[] ReferenceShadowDirections =
    [
        new(0f, new Vector2(0f, 4f), 0.1f),
        new(0.4f, new Vector2(-2f, -1f), 0.7f),
        new(0.8f, new Vector2(3f, 0f), 0.3f),
    ];

    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task NonStationNetworkSharesPlanetaryLightAndPropagatesRoofOpenings()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var roofSystem = em.System<SharedRoofSystem>();
        var lightCycleSystem = em.System<LightCycleSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var tileDefinitions = Server.ResolveDependency<ITileDefinitionManager>();

        var referenceColor = Color.FromHex("#A4C8E1");
        var referenceDuration = TimeSpan.FromMinutes(47);
        var referenceOffset = TimeSpan.FromMinutes(13);
        var rejectedLowerOffset = TimeSpan.FromMinutes(31);
        var propagatedReferenceOffset = TimeSpan.FromMinutes(19);
        var coveredIndex = new Vector2i(2, 3);
        var opaqueTile = new Tile(tileDefinitions["ClassicStone"].TileId);

        EntityUid networkUid = default;
        EntityUid upperMap = default;
        EntityUid middleMap = default;
        EntityUid lowerMap = default;
        Entity<MapGridComponent> upperGrid = default;
        Entity<MapGridComponent> middleGrid = default;
        Entity<MapGridComponent> lowerGrid = default;
        Entity<MapGridComponent> lateLowerGrid = default;
        var mapsAdded = false;

        try
        {
            await Server.WaitPost(() =>
            {
                upperMap = map.CreateMap();
                middleMap = map.CreateMap();
                lowerMap = map.CreateMap(runMapInit: false);
                upperGrid = map.CreateGridEntity(upperMap);
                middleGrid = map.CreateGridEntity(middleMap);
                lowerGrid = map.CreateGridEntity(lowerMap);
                upperGrid.Comp.CanSplit = false;
                middleGrid.Comp.CanSplit = false;
                lowerGrid.Comp.CanSplit = false;

                map.SetTile(upperGrid, coveredIndex, opaqueTile);
                map.SetTile(upperGrid, new Vector2i(9, 9), opaqueTile);
                map.SetTile(lowerGrid, new Vector2i(8, 8), opaqueTile);
                em.EnsureComponent<ClassicZLevelRoofComponent>(upperGrid);

                var upperLight = em.EnsureComponent<MapLightComponent>(upperMap);
                upperLight.AmbientLightColor = referenceColor;
                var upperCycle = em.EnsureComponent<LightCycleComponent>(upperMap);
                ConfigureReferenceCycle(upperCycle, referenceColor, referenceDuration, referenceOffset);
                var upperShadowCycle = em.EnsureComponent<SunShadowCycleComponent>(upperMap);
                upperShadowCycle.Directions.Clear();
                upperShadowCycle.Directions.AddRange(ReferenceShadowDirections);

                var lowerLight = em.EnsureComponent<MapLightComponent>(lowerMap);
                lowerLight.AmbientLightColor = Color.FromHex("#241019");
                var lowerCycle = em.EnsureComponent<LightCycleComponent>(lowerMap);
                ConfigureDifferentCycle(lowerCycle);
                var lowerShadowCycle = em.EnsureComponent<SunShadowCycleComponent>(lowerMap);
                lowerShadowCycle.Offset = rejectedLowerOffset;
                lowerShadowCycle.Directions.Clear();
                lowerShadowCycle.Directions.Add(new SunShadowCycleDirection(
                    0f,
                    new Vector2(9f, 9f),
                    0.99f));

                // Adding the uninitialized lower map runs its normal MapInit path. The random
                // offset produced there must be rejected in favor of the reference map's phase.
                var network = zLevels.CreateMapNetwork();
                networkUid = network;
                mapsAdded = zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [lowerMap] = -2,
                    [middleMap] = -1,
                    // The only pre-marked planetary grid is deliberately added last. Existing
                    // grids from earlier entries must still enroll when the batch completes.
                    [upperMap] = 0,
                });
            });

            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(mapsAdded, Is.True);
                AssertPlanetaryMap(em, upperMap, referenceColor, referenceDuration, referenceOffset);
                AssertPlanetaryMap(em, middleMap, referenceColor, referenceDuration, referenceOffset);
                AssertPlanetaryMap(em, lowerMap, referenceColor, referenceDuration, referenceOffset);
                AssertPlanetaryGrid(em, upperGrid, referenceDuration, referenceOffset);
                AssertPlanetaryGrid(em, middleGrid, referenceDuration, referenceOffset);
                AssertPlanetaryGrid(em, lowerGrid, referenceDuration, referenceOffset);
            });

            await Server.WaitPost(() =>
            {
                var lowerCycle = em.GetComponent<LightCycleComponent>(lowerMap);
                lightCycleSystem.SetOffset((lowerMap, lowerCycle), rejectedLowerOffset);
            });
            await Server.WaitAssertion(() =>
            {
                AssertOffsets(em, upperMap, referenceOffset);
                AssertOffsets(em, lowerMap, referenceOffset);
                AssertGridShadowOffset(em, upperGrid, referenceOffset);
                AssertGridShadowOffset(em, lowerGrid, referenceOffset);
            });

            await Server.WaitPost(() =>
            {
                var upperCycle = em.GetComponent<LightCycleComponent>(upperMap);
                lightCycleSystem.SetOffset((upperMap, upperCycle), propagatedReferenceOffset);
            });
            await Server.WaitAssertion(() =>
            {
                AssertOffsets(em, upperMap, propagatedReferenceOffset);
                AssertOffsets(em, lowerMap, propagatedReferenceOffset);
                AssertGridShadowOffset(em, upperGrid, propagatedReferenceOffset);
                AssertGridShadowOffset(em, lowerGrid, propagatedReferenceOffset);
            });

            await Server.WaitPost(() =>
            {
                lateLowerGrid = map.CreateGridEntity(lowerMap);
                lateLowerGrid.Comp.CanSplit = false;
                map.SetTile(lateLowerGrid, coveredIndex, opaqueTile);
            });
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(em.HasComponent<ClassicZLevelRoofComponent>(lateLowerGrid), Is.False,
                        "A late unrelated grid must not automatically become planetary terrain.");
                    Assert.That(em.HasComponent<RoofComponent>(lateLowerGrid), Is.False);
                    Assert.That(em.HasComponent<SunShadowComponent>(lateLowerGrid), Is.False);
                });
            });

            await Server.WaitPost(() => em.EnsureComponent<ClassicZLevelRoofComponent>(lateLowerGrid));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                AssertPlanetaryMap(em, lowerMap, referenceColor, referenceDuration, propagatedReferenceOffset);
                AssertPlanetaryGrid(em, lateLowerGrid, referenceDuration, propagatedReferenceOffset);

                var roof = em.GetComponent<RoofComponent>(lateLowerGrid);
                Assert.That(roofSystem.IsRooved((lateLowerGrid, lateLowerGrid.Comp, roof), coveredIndex), Is.True,
                    "A grid enrolled after network creation must immediately see existing cover above it.");
            });

            await Server.WaitPost(() => map.SetTile(upperGrid, coveredIndex, Tile.Empty));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                var roof = em.GetComponent<RoofComponent>(lateLowerGrid);
                Assert.That(roofSystem.IsRooved((lateLowerGrid, lateLowerGrid.Comp, roof), coveredIndex), Is.False,
                    "Removing the opaque upper tile must let planetary light reach the lower grid.");
            });

            await Server.WaitPost(() => map.SetTile(middleGrid, coveredIndex, opaqueTile));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                var roof = em.GetComponent<RoofComponent>(lateLowerGrid);
                Assert.That(roofSystem.IsRooved((lateLowerGrid, lateLowerGrid.Comp, roof), coveredIndex), Is.True,
                    "An opaque intermediate level must block planetary light below it.");
            });

            await Server.WaitPost(() => map.SetTile(middleGrid, coveredIndex, Tile.Empty));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                var roof = em.GetComponent<RoofComponent>(lateLowerGrid);
                Assert.That(roofSystem.IsRooved((lateLowerGrid, lateLowerGrid.Comp, roof), coveredIndex), Is.False,
                    "Openings through every higher level must expose the bottom level to planetary light.");
            });

            await Server.WaitPost(() => map.SetTile(upperGrid, coveredIndex, opaqueTile));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                var roof = em.GetComponent<RoofComponent>(lateLowerGrid);
                Assert.That(roofSystem.IsRooved((lateLowerGrid, lateLowerGrid.Comp, roof), coveredIndex), Is.True);
            });

            await Server.WaitPost(() => em.DeleteEntity(upperGrid));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                var roof = em.GetComponent<RoofComponent>(lateLowerGrid);
                Assert.That(roofSystem.IsRooved((lateLowerGrid, lateLowerGrid.Comp, roof), coveredIndex), Is.False,
                    "Deleting opaque terrain must immediately remove the roof it generated below.");
            });
        }
        finally
        {
            if (networkUid != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(networkUid));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task DetachingCoveredMapClearsGeneratedRoofState()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var roofSystem = em.System<SharedRoofSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var tileDefinitions = Server.ResolveDependency<ITileDefinitionManager>();

        var coveredIndex = new Vector2i(5, 7);
        var opaqueTile = new Tile(tileDefinitions["ClassicStone"].TileId);
        var networkUid = EntityUid.Invalid;
        var detachedMap = EntityUid.Invalid;
        Entity<MapGridComponent> detachedGrid = default;
        var mapsAdded = false;
        var mapWasDetached = false;

        try
        {
            await Server.WaitPost(() =>
            {
                var upperMap = map.CreateMap();
                detachedMap = map.CreateMap();
                var upperGrid = map.CreateGridEntity(upperMap);
                detachedGrid = map.CreateGridEntity(detachedMap);
                upperGrid.Comp.CanSplit = false;
                detachedGrid.Comp.CanSplit = false;

                map.SetTile(upperGrid, coveredIndex, opaqueTile);
                map.SetTile(detachedGrid, coveredIndex, opaqueTile);
                em.EnsureComponent<ClassicZLevelRoofComponent>(upperGrid);

                var network = zLevels.CreateMapNetwork();
                networkUid = network;
                mapsAdded = zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [upperMap] = 0,
                    [detachedMap] = -1,
                });
            });

            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(mapsAdded, Is.True);
                var roof = em.GetComponent<RoofComponent>(detachedGrid);
                Assert.That(roofSystem.IsRooved((detachedGrid, detachedGrid.Comp, roof), coveredIndex), Is.True,
                    "The upper terrain must initially cover the lower map.");
            });

            await Server.WaitPost(() =>
            {
                var network = em.GetComponent<ClassicZMapNetworkComponent>(networkUid);
                zLevels.RemoveMapFromNetwork((networkUid, network), detachedMap);
                mapWasDetached = true;
            });
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                var roof = em.GetComponent<RoofComponent>(detachedGrid);
                Assert.Multiple(() =>
                {
                    Assert.That(zLevels.TryGetMapNetwork(detachedMap, out _), Is.False);
                    Assert.That(roofSystem.IsRooved((detachedGrid, detachedGrid.Comp, roof), coveredIndex), Is.False,
                        "A detached map must not retain roof bits generated by its former upper level.");
                });
            });
        }
        finally
        {
            if (networkUid != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(networkUid));
                await Server.WaitRunTicks(2);
            }

            if (mapWasDetached && detachedMap != EntityUid.Invalid)
            {
                await Server.WaitPost(() =>
                {
                    if (em.TryGetComponent<MapComponent>(detachedMap, out var detachedMapComponent))
                        map.DeleteMap(detachedMapComponent.MapId);
                });
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task NetworkWithoutLightingUsesPlanetaryDefault()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var expectedColor = Color.FromHex("#D8B059");

        var networkUid = EntityUid.Invalid;
        var mapsAdded = false;

        try
        {
            await Server.WaitPost(() =>
            {
                var upperMap = map.CreateMap();
                var lowerMap = map.CreateMap();
                var upperGrid = map.CreateGridEntity(upperMap);
                map.CreateGridEntity(lowerMap);
                em.EnsureComponent<ClassicZLevelRoofComponent>(upperGrid);

                var network = zLevels.CreateMapNetwork();
                networkUid = network;
                mapsAdded = zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [upperMap] = 0,
                    [lowerMap] = -1,
                });
            });

            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(mapsAdded, Is.True);
                var network = em.GetComponent<ClassicZMapNetworkComponent>(networkUid);
                foreach (var mapUid in network.SortedZLevels)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(em.GetComponent<MapLightComponent>(mapUid).AmbientLightColor,
                            Is.EqualTo(expectedColor));
                        Assert.That(em.GetComponent<LightCycleComponent>(mapUid).OriginalColor,
                            Is.EqualTo(expectedColor));
                    });
                }
            });
        }
        finally
        {
            if (networkUid != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(networkUid));
                await Server.WaitRunTicks(2);
            }
        }
    }

    [Test]
    public async Task RemovingReferenceLevelRebuildsRoofsAndKeepsLightingSynchronized()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var roofSystem = em.System<SharedRoofSystem>();
        var lightCycleSystem = em.System<LightCycleSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var tileDefinitions = Server.ResolveDependency<ITileDefinitionManager>();

        var referenceColor = Color.FromHex("#B4D7E7");
        var referenceDuration = TimeSpan.FromMinutes(53);
        var referenceOffset = TimeSpan.FromMinutes(11);
        var newReferenceOffset = TimeSpan.FromMinutes(23);
        var coveredIndex = new Vector2i(4, 6);
        var opaqueTile = new Tile(tileDefinitions["ClassicStone"].TileId);

        var networkUid = EntityUid.Invalid;
        var removedReferenceMap = EntityUid.Invalid;
        var referenceWasRemoved = false;
        var mapsAdded = false;
        EntityUid nextReferenceMap = default;
        EntityUid bottomMap = default;
        Entity<MapGridComponent> bottomGrid = default;

        try
        {
            await Server.WaitPost(() =>
            {
                removedReferenceMap = map.CreateMap();
                nextReferenceMap = map.CreateMap();
                bottomMap = map.CreateMap();

                var referenceGrid = map.CreateGridEntity(removedReferenceMap);
                map.CreateGridEntity(nextReferenceMap);
                bottomGrid = map.CreateGridEntity(bottomMap);
                referenceGrid.Comp.CanSplit = false;
                bottomGrid.Comp.CanSplit = false;

                map.SetTile(referenceGrid, coveredIndex, opaqueTile);
                map.SetTile(bottomGrid, coveredIndex, opaqueTile);
                em.EnsureComponent<ClassicZLevelRoofComponent>(referenceGrid);

                var referenceLight = em.EnsureComponent<MapLightComponent>(removedReferenceMap);
                referenceLight.AmbientLightColor = referenceColor;
                var referenceCycle = em.EnsureComponent<LightCycleComponent>(removedReferenceMap);
                ConfigureReferenceCycle(referenceCycle, referenceColor, referenceDuration, referenceOffset);
                var referenceShadow = em.EnsureComponent<SunShadowCycleComponent>(removedReferenceMap);
                referenceShadow.Directions.Clear();
                referenceShadow.Directions.AddRange(ReferenceShadowDirections);

                var network = zLevels.CreateMapNetwork();
                networkUid = network;
                mapsAdded = zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [removedReferenceMap] = 0,
                    [nextReferenceMap] = -1,
                    [bottomMap] = -2,
                });
            });

            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(mapsAdded, Is.True);
                var roof = em.GetComponent<RoofComponent>(bottomGrid);
                Assert.That(roofSystem.IsRooved((bottomGrid, bottomGrid.Comp, roof), coveredIndex), Is.True,
                    "The original reference level must cover terrain below it.");
                AssertPlanetaryMap(em, nextReferenceMap, referenceColor, referenceDuration, referenceOffset);
                AssertPlanetaryMap(em, bottomMap, referenceColor, referenceDuration, referenceOffset);
            });

            await Server.WaitPost(() =>
            {
                var network = em.GetComponent<ClassicZMapNetworkComponent>(networkUid);
                zLevels.RemoveMapFromNetwork((networkUid, network), removedReferenceMap);
                referenceWasRemoved = true;
            });

            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                var network = em.GetComponent<ClassicZMapNetworkComponent>(networkUid);
                var roof = em.GetComponent<RoofComponent>(bottomGrid);

                Assert.Multiple(() =>
                {
                    Assert.That(network.ZLevelByEntity.ContainsKey(removedReferenceMap), Is.False);
                    Assert.That(zLevels.TryGetMapNetwork(removedReferenceMap, out _), Is.False,
                        "A detached map must not retain a stale Z-network association.");
                    Assert.That(network.SortedZLevels[^1], Is.EqualTo(nextReferenceMap),
                        "The highest remaining level must become the planetary reference.");
                    Assert.That(roofSystem.IsRooved((bottomGrid, bottomGrid.Comp, roof), coveredIndex), Is.False,
                        "Removing an opaque upper level must clear stale roof bits below it.");
                });

                var removedMap = em.GetComponent<ClassicZMapComponent>(removedReferenceMap);
                Assert.Multiple(() =>
                {
                    Assert.That(removedMap.NetworkUid, Is.EqualTo(EntityUid.Invalid));
                    Assert.That(removedMap.MapAbove, Is.Null);
                    Assert.That(removedMap.MapBelow, Is.Null);
                });

                AssertPlanetaryMap(em, nextReferenceMap, referenceColor, referenceDuration, referenceOffset);
                AssertPlanetaryMap(em, bottomMap, referenceColor, referenceDuration, referenceOffset);
            });

            await Server.WaitPost(() =>
            {
                var cycle = em.GetComponent<LightCycleComponent>(nextReferenceMap);
                lightCycleSystem.SetOffset((nextReferenceMap, cycle), newReferenceOffset);
            });
            await Server.WaitAssertion(() =>
            {
                AssertOffsets(em, nextReferenceMap, newReferenceOffset);
                AssertOffsets(em, bottomMap, newReferenceOffset);
                AssertGridShadowOffset(em, bottomGrid, newReferenceOffset);
            });
        }
        finally
        {
            if (networkUid != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(networkUid));
                await Server.WaitRunTicks(2);
            }

            if (referenceWasRemoved && removedReferenceMap != EntityUid.Invalid)
            {
                await Server.WaitPost(() =>
                {
                    if (em.TryGetComponent<MapComponent>(removedReferenceMap, out var removedMap))
                        map.DeleteMap(removedMap.MapId);
                });
                await Server.WaitRunTicks(2);
            }
        }
    }

    private static void ConfigureReferenceCycle(
        LightCycleComponent cycle,
        Color color,
        TimeSpan duration,
        TimeSpan offset)
    {
        cycle.OriginalColor = color;
        cycle.Duration = duration;
        cycle.Offset = offset;
        cycle.Enabled = false;
        cycle.InitialOffset = true;
        cycle.MinLightLevel = 0.25f;
        cycle.MaxLightLevel = 2.25f;
        cycle.ClipLight = 1.15f;
        cycle.ClipLevel = new Color(0.8f, 0.9f, 1.1f);
        cycle.MinLevel = new Color(0.2f, 0.3f, 0.4f);
        cycle.MaxLevel = new Color(1.6f, 1.7f, 1.8f);
    }

    private static void ConfigureDifferentCycle(LightCycleComponent cycle)
    {
        cycle.OriginalColor = Color.FromHex("#331122");
        cycle.Duration = TimeSpan.FromMinutes(5);
        cycle.Offset = TimeSpan.FromMinutes(2);
        cycle.Enabled = true;
        cycle.InitialOffset = true;
        cycle.MinLightLevel = 0.01f;
        cycle.MaxLightLevel = 4f;
        cycle.ClipLight = 2f;
        cycle.ClipLevel = new Color(1.2f, 1.3f, 1.4f);
        cycle.MinLevel = new Color(0.01f, 0.02f, 0.03f);
        cycle.MaxLevel = new Color(3f, 4f, 5f);
    }

    private static void AssertPlanetaryMap(
        IEntityManager em,
        EntityUid mapUid,
        Color color,
        TimeSpan duration,
        TimeSpan offset)
    {
        var mapLight = em.GetComponent<MapLightComponent>(mapUid);
        var cycle = em.GetComponent<LightCycleComponent>(mapUid);
        var shadowCycle = em.GetComponent<SunShadowCycleComponent>(mapUid);

        Assert.Multiple(() =>
        {
            Assert.That(mapLight.AmbientLightColor, Is.EqualTo(color));
            Assert.That(em.HasComponent<SunShadowComponent>(mapUid), Is.True);
            Assert.That(cycle.OriginalColor, Is.EqualTo(color));
            Assert.That(cycle.Duration, Is.EqualTo(duration));
            Assert.That(cycle.Offset, Is.EqualTo(offset));
            Assert.That(cycle.Enabled, Is.False);
            Assert.That(cycle.InitialOffset, Is.False);
            Assert.That(cycle.MinLightLevel, Is.EqualTo(0.25f));
            Assert.That(cycle.MaxLightLevel, Is.EqualTo(2.25f));
            Assert.That(cycle.ClipLight, Is.EqualTo(1.15f));
            Assert.That(cycle.ClipLevel, Is.EqualTo(new Color(0.8f, 0.9f, 1.1f)));
            Assert.That(cycle.MinLevel, Is.EqualTo(new Color(0.2f, 0.3f, 0.4f)));
            Assert.That(cycle.MaxLevel, Is.EqualTo(new Color(1.6f, 1.7f, 1.8f)));
            Assert.That(shadowCycle.Duration, Is.EqualTo(duration));
            Assert.That(shadowCycle.Offset, Is.EqualTo(offset));
            Assert.That(shadowCycle.Directions, Is.EqualTo(ReferenceShadowDirections));
        });
    }

    private static void AssertPlanetaryGrid(
        IEntityManager em,
        Entity<MapGridComponent> grid,
        TimeSpan duration,
        TimeSpan offset)
    {
        var shadowCycle = em.GetComponent<SunShadowCycleComponent>(grid);

        Assert.Multiple(() =>
        {
            Assert.That(em.HasComponent<ClassicZLevelRoofComponent>(grid), Is.True);
            Assert.That(em.HasComponent<RoofComponent>(grid), Is.True);
            Assert.That(em.HasComponent<SunShadowComponent>(grid), Is.True);
            Assert.That(shadowCycle.Duration, Is.EqualTo(duration));
            Assert.That(shadowCycle.Offset, Is.EqualTo(offset));
            Assert.That(shadowCycle.Directions, Is.EqualTo(ReferenceShadowDirections));
        });
    }

    private static void AssertOffsets(IEntityManager em, EntityUid mapUid, TimeSpan expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(em.GetComponent<LightCycleComponent>(mapUid).Offset, Is.EqualTo(expected));
            Assert.That(em.GetComponent<SunShadowCycleComponent>(mapUid).Offset, Is.EqualTo(expected));
        });
    }

    private static void AssertGridShadowOffset(
        IEntityManager em,
        Entity<MapGridComponent> grid,
        TimeSpan expected)
    {
        Assert.That(em.GetComponent<SunShadowCycleComponent>(grid).Offset, Is.EqualTo(expected));
    }
}
