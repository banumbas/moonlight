using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Classic.ZLevels.Core;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

[TestFixture]
public sealed class ZLevelLookUpRefreshTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    [Test]
    public async Task StationaryViewerRechecksMovingUpperGridAndChangedCeiling()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var transform = em.System<SharedTransformSystem>();
        var zLevels = em.System<ClassicZLevelsSystem>();
        var actions = em.System<SharedActionsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;
        var refreshTicks = (int) Math.Ceiling(1.1d / Server.Timing.TickPeriod.TotalSeconds);
        EntityUid network = default;
        EntityUid viewer = default;
        Entity<MapGridComponent> upperGrid = default;
        Tile plating = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var current = map.CreateMap(runMapInit: false);
                var upper = map.CreateMap(runMapInit: false);
                upperGrid = map.CreateGridEntity(upper);
                upperGrid.Comp.CanSplit = false;
                plating = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                map.SetTile(upperGrid, Vector2i.Zero, plating);
                map.SetTile(upperGrid, new Vector2i(0, 1), plating);

                var zNetwork = zLevels.CreateMapNetwork();
                network = zNetwork;
                Assert.That(zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
                {
                    [current] = 0,
                    [upper] = 1,
                }), Is.True);
                map.InitializeMap(current);
                map.InitializeMap(upper);

                viewer = em.SpawnEntity(null, new EntityCoordinates(current, new Vector2(0.5f, 0.5f)));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                var action = actions.GetAction(viewerComp.ActionEntity);
                Assert.That(action, Is.Not.Null);
                actions.PerformAction(viewer, action!.Value);
                Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                Assert.That(viewerComp.UpperEyes, Is.Empty);

                transform.SetWorldPosition(upperGrid, new Vector2(2f, 0f));
            });

            await Server.WaitRunTicks(refreshTicks);
            await Server.WaitAssertion(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(1));
                Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(1));
                Assert.That(transform.GetWorldPosition(viewer), Is.EqualTo(new Vector2(0.5f, 0.5f)));
            });

            await Server.WaitPost(() => transform.SetWorldPosition(upperGrid, Vector2.Zero));
            await Server.WaitRunTicks(refreshTicks);
            await Server.WaitAssertion(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                Assert.That(viewerComp.UpperEyes, Is.Empty);
            });

            await Server.WaitPost(() => map.SetTile(upperGrid, Vector2i.Zero, Tile.Empty));
            await Server.WaitRunTicks(refreshTicks);
            await Server.WaitAssertion(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.That(viewerComp.VisibleZLevelsAbove, Is.EqualTo(1));
                Assert.That(viewerComp.UpperEyes, Has.Count.EqualTo(1));
            });

            await Server.WaitPost(() => map.SetTile(upperGrid, Vector2i.Zero, plating));
            await Server.WaitRunTicks(refreshTicks);
            await Server.WaitAssertion(() =>
            {
                var viewerComp = em.GetComponent<ClassicZLevelViewerComponent>(viewer);
                Assert.That(viewerComp.VisibleZLevelsAbove, Is.Zero);
                Assert.That(viewerComp.UpperEyes, Is.Empty);
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
}
