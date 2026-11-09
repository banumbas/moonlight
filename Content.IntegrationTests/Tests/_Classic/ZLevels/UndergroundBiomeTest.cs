using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Classic.Station;
using Content.Server._Classic.ZLevels.Core;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Roof;
using Content.Shared.CCVar;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Station.Components;
using Content.Shared._Classic.Station.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

[TestFixture]
public sealed class UndergroundBiomeTest : GameTest
{
    private static readonly string MapPrototypeId = "ClassicClassic";

    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ClassicUndergroundSetupContractsTest()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var tileDefinitions = server.ResolveDependency<ITileDefinitionManager>();
        var ticker = entManager.System<GameTicker>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var roofSystem = entManager.System<SharedRoofSystem>();
        var zLevels = entManager.System<ClassicZLevelsSystem>();
        var stationSystem = entManager.System<StationSystem>();

        var networkUid = EntityUid.Invalid;
        var stationUid = EntityUid.Invalid;
        var surfaceMapUid = EntityUid.Invalid;
        Entity<MapGridComponent> surfaceGrid = default;

        try
        {
            await server.WaitPost(() =>
            {
                var options = DeserializationOptions.Default with { InitializeMaps = true };
                ticker.LoadGameMap(protoManager.Index<GameMapPrototype>(MapPrototypeId), out var surfaceMapId, options);

                surfaceGrid = mapSystem.GetAllGrids(surfaceMapId)
                    .MaxBy(grid => grid.Comp.ChunkCount);
                if (surfaceGrid.Owner == EntityUid.Invalid)
                    return;

                surfaceMapUid = entManager.GetComponent<TransformComponent>(surfaceGrid.Owner).MapUid ?? EntityUid.Invalid;
                if (surfaceMapUid == EntityUid.Invalid)
                    return;

                networkUid = entManager.GetComponent<ClassicZMapComponent>(surfaceMapUid).NetworkUid;
                stationUid = entManager.GetComponent<StationMemberComponent>(surfaceGrid.Owner).Station;
            });
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                Assert.That(surfaceGrid.Owner, Is.Not.EqualTo(EntityUid.Invalid), "Classic surface has no terrain grid.");
                Assert.That(surfaceMapUid, Is.Not.EqualTo(EntityUid.Invalid), "Classic surface grid has no parent map.");
                Assert.That(networkUid, Is.Not.EqualTo(EntityUid.Invalid), "Classic surface map has no Z-network.");

                var surfaceZMap = entManager.GetComponent<ClassicZMapComponent>(surfaceMapUid);
                var network = entManager.GetComponent<ClassicZMapNetworkComponent>(networkUid);

                Assert.Multiple(() =>
                {
                    Assert.That(surfaceZMap.Depth, Is.Zero);
                    Assert.That(network.ZLevels.Keys, Is.EquivalentTo(new[] { -3, -2, -1, 0, 1, 2 }));
                    Assert.That(network.SortedMin, Is.EqualTo(-3));
                    Assert.That(network.SortedMax, Is.EqualTo(2));
                    Assert.That(network.SortedZLevels, Has.Count.EqualTo(6));
                });

                var surfaceBiome = entManager.GetComponent<BiomeComponent>(surfaceGrid.Owner);
                var surfaceLight = entManager.GetComponent<MapLightComponent>(surfaceMapUid);
                var surfaceCycle = entManager.GetComponent<LightCycleComponent>(surfaceMapUid);
                var stationData = entManager.GetComponent<StationDataComponent>(stationUid);
                Assert.Multiple(() =>
                {
                    Assert.That(surfaceBiome.Template?.Id, Is.EqualTo("GrasslandsClassic"));
                    Assert.That(surfaceCycle.OriginalColor, Is.EqualTo(Color.FromHex("#E0A0C0")),
                        "Station setup must preserve the authored planetary light palette.");
                });

                var expectedLevels = new Dictionary<int,
                    (string Template, bool LoadEntities, bool LoadDecals, string Parallax, Color RoofColor)>
                {
                    [-1] = ("ClassicUndergroundDirtStone", true, false, "Dirt", Color.FromHex("#050505")),
                    [-2] = ("ClassicUndergroundCaves", true, false, "Dirt", Color.FromHex("#050505")),
                    [-3] = ("ClassicUndergroundMagma", true, false, "ClassicMagma", Color.FromHex("#48160c")),
                };

                foreach (var (depth, expected) in expectedLevels)
                {
                    Assert.That(network.ZLevels.TryGetValue(depth, out var undergroundMapUid), Is.True);
                    Assert.That(undergroundMapUid, Is.Not.Null, $"No map exists at depth {depth}.");

                    var mapUid = undergroundMapUid!.Value;
                    var zMap = entManager.GetComponent<ClassicZMapComponent>(mapUid);
                    var undergroundGrid = GetTerrainGrid(mapUid, entManager, mapSystem);
                    var marker = entManager.GetComponent<ClassicUndergroundBiomeComponent>(undergroundGrid.Owner);
                    var biome = entManager.GetComponent<BiomeComponent>(undergroundGrid.Owner);
                    var mapLight = entManager.GetComponent<MapLightComponent>(mapUid);
                    var lightCycle = entManager.GetComponent<LightCycleComponent>(mapUid);
                    var shadowCycle = entManager.GetComponent<SunShadowCycleComponent>(undergroundGrid.Owner);
                    var roof = entManager.GetComponent<RoofComponent>(undergroundGrid.Owner);

                    Assert.Multiple(() =>
                    {
                        Assert.That(zMap.Depth, Is.EqualTo(depth));
                        Assert.That(zMap.MapAbove, Is.EqualTo(network.ZLevels[depth + 1]));
                        Assert.That(zMap.MapBelow, Is.EqualTo(depth == -3 ? null : network.ZLevels[depth - 1]));
                        Assert.That(marker.Depth, Is.EqualTo(depth));
                        Assert.That(marker.LoadEntities, Is.EqualTo(expected.LoadEntities));
                        Assert.That(marker.LoadDecals, Is.EqualTo(expected.LoadDecals));
                        Assert.That(biome.Template?.Id, Is.EqualTo(expected.Template));
                        Assert.That(biome.Seed, Is.EqualTo(surfaceBiome.Seed), "All station biomes must share one seed.");
                        Assert.That(entManager.GetComponent<ParallaxComponent>(mapUid).Parallax, Is.EqualTo(expected.Parallax));
                        Assert.That(entManager.HasComponent<StationAuxiliaryGridComponent>(undergroundGrid.Owner), Is.True);
                        Assert.That(entManager.GetComponent<StationMemberComponent>(undergroundGrid.Owner).Station,
                            Is.EqualTo(stationUid));
                        Assert.That(stationData.Grids, Does.Not.Contain(undergroundGrid.Owner));
                        Assert.That(stationData.AuxiliaryGrids, Does.Contain(undergroundGrid.Owner));
                        Assert.That(entManager.HasComponent<SunShadowComponent>(undergroundGrid.Owner), Is.True);
                        Assert.That(mapLight.AmbientLightColor, Is.EqualTo(surfaceLight.AmbientLightColor));
                        Assert.That(roof.Color, Is.EqualTo(expected.RoofColor));
                        AssertLightCyclesEqual(surfaceCycle, lightCycle);
                        Assert.That(shadowCycle.Duration, Is.EqualTo(surfaceCycle.Duration));
                        Assert.That(shadowCycle.Offset, Is.EqualTo(surfaceCycle.Offset));
                    });

                    AssertAlignedTileCenter(Vector2i.Zero, surfaceGrid, undergroundGrid, mapSystem, depth);
                    AssertAlignedTileCenter(new Vector2i(7, 7), surfaceGrid, undergroundGrid, mapSystem, depth);
                }

                foreach (var mapUid in network.SortedZLevels)
                {
                    if (!mapUid.IsValid())
                        continue;

                    var mapLight = entManager.GetComponent<MapLightComponent>(mapUid);
                    var lightCycle = entManager.GetComponent<LightCycleComponent>(mapUid);
                    var mapShadowCycle = entManager.GetComponent<SunShadowCycleComponent>(mapUid);
                    Assert.Multiple(() =>
                    {
                        Assert.That(mapLight.AmbientLightColor, Is.EqualTo(surfaceLight.AmbientLightColor));
                        AssertLightCyclesEqual(surfaceCycle, lightCycle);
                        Assert.That(mapShadowCycle.Duration, Is.EqualTo(surfaceCycle.Duration));
                        Assert.That(mapShadowCycle.Offset, Is.EqualTo(surfaceCycle.Offset));
                    });

                    if (!entManager.TryGetComponent<MapComponent>(mapUid, out var map))
                        continue;

                    foreach (var grid in mapSystem.GetAllGrids(map.MapId))
                    {
                        if (!entManager.HasComponent<ClassicZLevelRoofComponent>(grid.Owner))
                            continue;

                        var gridShadowCycle = entManager.GetComponent<SunShadowCycleComponent>(grid.Owner);
                        Assert.Multiple(() =>
                        {
                            Assert.That(entManager.HasComponent<SunShadowComponent>(grid.Owner), Is.True);
                            Assert.That(gridShadowCycle.Duration, Is.EqualTo(surfaceCycle.Duration));
                            Assert.That(gridShadowCycle.Offset, Is.EqualTo(surfaceCycle.Offset));
                        });
                    }
                }

                Assert.That(stationSystem.GetLargestGrid(stationUid), Is.EqualTo(surfaceGrid.Owner),
                    "Explored underground terrain must never replace the station's primary grid.");
            });

            Entity<MapGridComponent> lateMagmaGrid = default;
            await server.WaitPost(() =>
            {
                var network = entManager.GetComponent<ClassicZMapNetworkComponent>(networkUid);
                var magmaMapUid = network.ZLevels[-3]!.Value;
                lateMagmaGrid = mapSystem.CreateGridEntity(magmaMapUid);
                entManager.EnsureComponent<ClassicZLevelRoofComponent>(lateMagmaGrid);
            });
            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                var surfaceCycle = entManager.GetComponent<LightCycleComponent>(surfaceMapUid);
                var shadowCycle = entManager.GetComponent<SunShadowCycleComponent>(lateMagmaGrid.Owner);
                Assert.Multiple(() =>
                {
                    Assert.That(entManager.GetComponent<RoofComponent>(lateMagmaGrid.Owner).Color,
                        Is.EqualTo(Color.FromHex("#48160c")),
                        "A late terrain grid must inherit its map level's underground ambient.");
                    Assert.That(entManager.HasComponent<SunShadowComponent>(lateMagmaGrid.Owner), Is.True);
                    Assert.That(shadowCycle.Duration, Is.EqualTo(surfaceCycle.Duration));
                    Assert.That(shadowCycle.Offset, Is.EqualTo(surfaceCycle.Offset));
                });
            });

            RoofOpening? opening = null;
            Entity<MapGridComponent> shallowUnderground = default;
            await server.WaitPost(() =>
            {
                var network = entManager.GetComponent<ClassicZMapNetworkComponent>(networkUid);
                var undergroundMapUid = network.ZLevels[-1]!.Value;
                shallowUnderground = GetTerrainGrid(undergroundMapUid, entManager, mapSystem);
                opening = FindRoofOpening(
                    surfaceGrid,
                    shallowUnderground,
                    network,
                    zLevels,
                    mapSystem,
                    entManager,
                    tileDefinitions);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(opening, Is.Not.Null,
                    "Classic surface has no opaque tile in a column left open by every higher Z-level.");
            });

            var roofOpening = opening!.Value;
            var stone = new Tile(tileDefinitions["ClassicStone"].TileId);

            await server.WaitPost(() =>
                mapSystem.SetTile(shallowUnderground, roofOpening.UndergroundIndex, Tile.Empty));
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var hasTile = mapSystem.TryGetTileRef(
                    shallowUnderground.Owner,
                    shallowUnderground.Comp,
                    roofOpening.UndergroundIndex,
                    out var tileRef);
                Assert.That(!hasTile || tileRef.Tile.IsEmpty, Is.True,
                    "The streaming contract requires an empty lower cell before adding terrain.");
            });

            await server.WaitPost(() =>
                mapSystem.SetTile(shallowUnderground, roofOpening.UndergroundIndex, stone));
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var roof = entManager.GetComponent<RoofComponent>(shallowUnderground.Owner);
                Assert.That(
                    roofSystem.GetColor((shallowUnderground.Owner, shallowUnderground.Comp, roof),
                        roofOpening.UndergroundIndex,
                        false),
                    Is.EqualTo(Color.FromHex("#050505")),
                    "A lower tile streamed after an opaque surface tile must be roofed immediately.");
            });

            await server.WaitPost(() => mapSystem.SetTile(surfaceGrid, roofOpening.SurfaceIndex, Tile.Empty));
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var roof = entManager.GetComponent<RoofComponent>(shallowUnderground.Owner);
                Assert.That(
                    roofSystem.GetColor((shallowUnderground.Owner, shallowUnderground.Comp, roof),
                        roofOpening.UndergroundIndex,
                        false),
                    Is.Null,
                    "Opening the surface must expose the lower tile to planetary light.");
            });

            await server.WaitPost(() =>
                mapSystem.SetTile(surfaceGrid, roofOpening.SurfaceIndex, roofOpening.CoveringTile));
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var roof = entManager.GetComponent<RoofComponent>(shallowUnderground.Owner);
                Assert.That(
                    roofSystem.GetColor((shallowUnderground.Owner, shallowUnderground.Comp, roof),
                        roofOpening.UndergroundIndex,
                        false),
                    Is.EqualTo(Color.FromHex("#050505")),
                    "Closing the surface opening must restore the lower roof mask.");
            });
        }
        finally
        {
            if (networkUid != EntityUid.Invalid)
            {
                await server.WaitPost(() => zLevels.DeleteMapNetwork(networkUid));
                await server.WaitRunTicks(2);
            }

            if (stationUid != EntityUid.Invalid)
            {
                await server.WaitPost(() =>
                {
                    if (entManager.EntityExists(stationUid))
                        entManager.DeleteEntity(stationUid);
                });
                await server.WaitRunTicks(1);
            }
        }
    }

    private static RoofOpening? FindRoofOpening(
        Entity<MapGridComponent> surface,
        Entity<MapGridComponent> underground,
        ClassicZMapNetworkComponent network,
        ClassicZLevelsSystem zLevels,
        SharedMapSystem mapSystem,
        IEntityManager entManager,
        ITileDefinitionManager tileDefinitions)
    {
        foreach (var tileRef in mapSystem.GetAllTiles(surface.Owner, surface.Comp))
        {
            if (tileRef.Tile.IsEmpty ||
                tileDefinitions[tileRef.Tile.TypeId] is not ContentTileDefinition { Transparent: false })
            {
                continue;
            }

            var worldTile = zLevels.GridTileToWorldTile(surface.Owner, surface.Comp, tileRef.GridIndices);
            if (HasOtherOpaqueTileAtOrAboveSurface(
                    surface,
                    tileRef.GridIndices,
                    worldTile,
                    network,
                    mapSystem,
                    entManager,
                    tileDefinitions))
            {
                continue;
            }

            var undergroundMapId = entManager.GetComponent<TransformComponent>(underground.Owner).MapID;
            var worldPosition = new Vector2(worldTile.X + 0.5f, worldTile.Y + 0.5f);
            var undergroundIndex = mapSystem.TileIndicesFor(
                underground.Owner,
                underground.Comp,
                new MapCoordinates(worldPosition, undergroundMapId));
            return new RoofOpening(tileRef.GridIndices, undergroundIndex, tileRef.Tile);
        }

        return null;
    }

    private static bool HasOtherOpaqueTileAtOrAboveSurface(
        Entity<MapGridComponent> surface,
        Vector2i surfaceIndex,
        Vector2i worldTile,
        ClassicZMapNetworkComponent network,
        SharedMapSystem mapSystem,
        IEntityManager entManager,
        ITileDefinitionManager tileDefinitions)
    {
        var worldPosition = new Vector2(worldTile.X + 0.5f, worldTile.Y + 0.5f);

        foreach (var (depth, mapUid) in network.ZLevels)
        {
            if (depth < 0 || mapUid is not { } validMapUid ||
                !entManager.TryGetComponent<MapComponent>(validMapUid, out var map))
            {
                continue;
            }

            foreach (var grid in mapSystem.GetAllGrids(map.MapId))
            {
                var localIndex = mapSystem.TileIndicesFor(
                    grid.Owner,
                    grid.Comp,
                    new MapCoordinates(worldPosition, map.MapId));

                if (depth == 0 && grid.Owner == surface.Owner && localIndex == surfaceIndex)
                    continue;

                if (!mapSystem.TryGetTileRef(grid.Owner, grid.Comp, localIndex, out var tileRef) ||
                    tileRef.Tile.IsEmpty ||
                    tileDefinitions[tileRef.Tile.TypeId] is not ContentTileDefinition { Transparent: false })
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static void AssertLightCyclesEqual(LightCycleComponent expected, LightCycleComponent actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.OriginalColor, Is.EqualTo(expected.OriginalColor));
            Assert.That(actual.Duration, Is.EqualTo(expected.Duration));
            Assert.That(actual.Offset, Is.EqualTo(expected.Offset));
            Assert.That(actual.Enabled, Is.EqualTo(expected.Enabled));
            Assert.That(actual.InitialOffset, Is.False);
            Assert.That(actual.MinLightLevel, Is.EqualTo(expected.MinLightLevel));
            Assert.That(actual.MaxLightLevel, Is.EqualTo(expected.MaxLightLevel));
            Assert.That(actual.ClipLight, Is.EqualTo(expected.ClipLight));
            Assert.That(actual.ClipLevel, Is.EqualTo(expected.ClipLevel));
            Assert.That(actual.MinLevel, Is.EqualTo(expected.MinLevel));
            Assert.That(actual.MaxLevel, Is.EqualTo(expected.MaxLevel));
        });
    }

    private static Entity<MapGridComponent> GetTerrainGrid(
        EntityUid mapUid,
        IEntityManager entManager,
        SharedMapSystem mapSystem)
    {
        if (entManager.TryGetComponent<MapGridComponent>(mapUid, out var combinedGrid))
            return (mapUid, combinedGrid);

        var map = entManager.GetComponent<MapComponent>(mapUid);
        return mapSystem.GetAllGrids(map.MapId).MaxBy(grid => grid.Comp.ChunkCount);
    }

    private static void AssertAlignedTileCenter(
        Vector2i indices,
        Entity<MapGridComponent> surface,
        Entity<MapGridComponent> underground,
        SharedMapSystem mapSystem,
        int depth)
    {
        var surfacePosition = mapSystem.GridTileToWorldPos(surface.Owner, surface.Comp, indices);
        var undergroundPosition = mapSystem.GridTileToWorldPos(underground.Owner, underground.Comp, indices);

        Assert.Multiple(() =>
        {
            Assert.That(undergroundPosition.X, Is.EqualTo(surfacePosition.X).Within(0.0001f),
                $"X tile-center alignment differs at depth {depth} for {indices}.");
            Assert.That(undergroundPosition.Y, Is.EqualTo(surfacePosition.Y).Within(0.0001f),
                $"Y tile-center alignment differs at depth {depth} for {indices}.");
        });
    }

    private readonly record struct RoofOpening(
        Vector2i SurfaceIndex,
        Vector2i UndergroundIndex,
        Tile CoveringTile);
}
