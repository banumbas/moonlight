using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Classic.ZLevels.Core;
using Content.Shared._Classic.ZLevels.Core.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

[TestFixture]
public sealed class ZGridConnectorTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task OnlyOccupancyChangesAboveAConnectorRecalculateTopology()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var connectors = em.System<ClassicZGridConnectorSystem>();
        var tileDefs = Server.ResolveDependency<ITileDefinitionManager>();
        EntityUid mapNetwork = default;
        Entity<MapGridComponent> upperGrid = default;
        Entity<MapGridComponent> lowerGrid = default;
        ulong beforeUnrelatedBatch = 0;
        ulong afterRemoval = 0;
        var plating = new Tile(tileDefs["Plating"].TileId);
        var stone = new Tile(tileDefs["ClassicStone"].TileId);
        var connectorIndex = new Vector2i(2, 1);

        try
        {
            await Server.WaitPost(() =>
            {
                var upperMap = map.CreateMap();
                var lowerMap = map.CreateMap();
                upperGrid = map.CreateGridEntity(upperMap);
                lowerGrid = map.CreateGridEntity(lowerMap);
                upperGrid.Comp.CanSplit = false;
                lowerGrid.Comp.CanSplit = false;
                transform.SetWorldPositionRotation(upperGrid, new Vector2(10, 5), Angle.FromDegrees(90));
                transform.SetWorldPositionRotation(lowerGrid, new Vector2(10, 5), Angle.FromDegrees(90));
                map.SetTile(upperGrid, connectorIndex, plating);
                map.SetTile(lowerGrid, connectorIndex, plating);

                var network = zLevels.CreateMapNetwork();
                mapNetwork = network;
                Assert.That(zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [upperMap] = 0,
                    [lowerMap] = -1,
                }), Is.True);

                var connector = em.SpawnEntity(null, map.GridTileToLocal(lowerGrid, lowerGrid.Comp, connectorIndex));
                em.EnsureComponent<ClassicZGridConnectorComponent>(connector);
                Assert.That(transform.AnchorEntity(connector), Is.True);
            });

            await Server.WaitRunTicks(3);
            await Server.WaitAssertion(() =>
            {
                Assert.That(em.HasComponent<ClassicZGridComponent>(upperGrid), Is.True);
                Assert.That(em.HasComponent<ClassicZGridComponent>(lowerGrid), Is.True);
                Assert.That(em.GetComponent<ClassicZGridComponent>(upperGrid).Network,
                    Is.EqualTo(em.GetComponent<ClassicZGridComponent>(lowerGrid).Network));
                beforeUnrelatedBatch = connectors.RecalculationCount;
            });

            await Server.WaitPost(() =>
            {
                // Models a streamed procedural chunk: all changes alter occupancy, but none can
                // affect the connector on this translated and rotated grid.
                var distantChunk = new List<(Vector2i, Tile)>();
                for (var x = 32; x < 40; x++)
                for (var y = 32; y < 40; y++)
                    distantChunk.Add((new Vector2i(x, y), stone));
                map.SetTiles(upperGrid, upperGrid.Comp, distantChunk);
            });
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
                Assert.That(connectors.RecalculationCount, Is.EqualTo(beforeUnrelatedBatch)));

            await Server.WaitPost(() => map.SetTile(upperGrid, connectorIndex, stone));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
                Assert.That(connectors.RecalculationCount, Is.EqualTo(beforeUnrelatedBatch),
                    "Replacing a non-empty tile cannot change connector topology."));

            await Server.WaitPost(() => map.SetTile(upperGrid, connectorIndex, Tile.Empty));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(connectors.RecalculationCount, Is.GreaterThan(beforeUnrelatedBatch));
                Assert.That(em.HasComponent<ClassicZGridComponent>(upperGrid), Is.False);
                Assert.That(em.HasComponent<ClassicZGridComponent>(lowerGrid), Is.False);
                afterRemoval = connectors.RecalculationCount;
            });

            await Server.WaitPost(() => map.SetTile(upperGrid, connectorIndex, plating));
            await Server.WaitRunTicks(2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(connectors.RecalculationCount, Is.GreaterThan(afterRemoval));
                Assert.That(em.HasComponent<ClassicZGridComponent>(upperGrid), Is.True);
                Assert.That(em.HasComponent<ClassicZGridComponent>(lowerGrid), Is.True);
            });
        }
        finally
        {
            if (mapNetwork != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(mapNetwork));
                await Server.WaitRunTicks(2);
            }
        }
    }
}
