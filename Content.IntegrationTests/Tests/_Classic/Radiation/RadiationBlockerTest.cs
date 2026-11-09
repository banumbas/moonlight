using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Radiation.Components;
using Content.Server.Radiation.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Classic.Radiation;

[TestFixture]
[TestOf(typeof(RadiationSystem))]
public sealed class RadiationBlockerTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: RadiationBlockerIdempotenceTest
  components:
  - type: Transform
    anchored: true
  - type: RadiationBlocker
    resistance: 2
";

    [Test]
    public async Task RepeatedAnchorRegistrationIsIdempotentAndStateChangesStillRefresh()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var radiation = entManager.System<RadiationSystem>();

        await server.WaitAssertion(() =>
        {
            mapSystem.CreateMap(out var mapId);
            var grid = mapSystem.CreateGridEntity(mapId);
            var tile = new Vector2i(2, -3);
            mapSystem.SetTile(grid, tile, new Tile(1));

            var coordinates = new EntityCoordinates(grid.Owner, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
            var blockerUid = entManager.SpawnEntity("RadiationBlockerIdempotenceTest", coordinates);
            var blocker = entManager.GetComponent<RadiationBlockerComponent>(blockerUid);
            var xform = entManager.GetComponent<TransformComponent>(blockerUid);

            Assert.Multiple(() =>
            {
                Assert.That(blocker.CurrentPosition, Is.EqualTo((grid.Owner, tile)));
                Assert.That(blocker.RegisteredResistance, Is.EqualTo(2f));
            });

            var initialCache = entManager.GetComponent<RadiationGridResistanceComponent>(grid.Owner);
            Assert.That(initialCache.ResistancePerTile[tile], Is.EqualTo(2f));

            // Transform startup raises this after ComponentInit has already registered an anchored
            // blocker. A repeated event must not remove and recreate the one-entry grid cache.
            RaiseAnchorChanged(entManager, blockerUid, xform);
            var unchangedCache = entManager.GetComponent<RadiationGridResistanceComponent>(grid.Owner);
            Assert.Multiple(() =>
            {
                Assert.That(unchangedCache, Is.SameAs(initialCache));
                Assert.That(unchangedCache.ResistancePerTile[tile], Is.EqualTo(2f));
            });

            // The public state transition must still remove and restore the cached contribution.
            radiation.SetEnabled(blockerUid, false, blocker);
            Assert.Multiple(() =>
            {
                Assert.That(blocker.CurrentPosition, Is.Null);
                Assert.That(blocker.RegisteredResistance, Is.Zero);
                Assert.That(entManager.HasComponent<RadiationGridResistanceComponent>(grid.Owner), Is.False);
            });

            radiation.SetEnabled(blockerUid, true, blocker);
            Assert.That(
                entManager.GetComponent<RadiationGridResistanceComponent>(grid.Owner).ResistancePerTile[tile],
                Is.EqualTo(2f));

            // Real unanchor/re-anchor must still unregister and register the blocker normally.
            transformSystem.Unanchor(blockerUid, xform);
            Assert.Multiple(() =>
            {
                Assert.That(blocker.CurrentPosition, Is.Null);
                Assert.That(entManager.HasComponent<RadiationGridResistanceComponent>(grid.Owner), Is.False);
            });

            Assert.That(transformSystem.AnchorEntity((blockerUid, xform), grid, tile), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(blocker.CurrentPosition, Is.EqualTo((grid.Owner, tile)));
                Assert.That(blocker.RegisteredResistance, Is.EqualTo(2f));
                Assert.That(
                    entManager.GetComponent<RadiationGridResistanceComponent>(grid.Owner).ResistancePerTile[tile],
                    Is.EqualTo(2f));
            });

            mapSystem.DeleteMap(mapId);
        });
    }

    private static void RaiseAnchorChanged(
        IEntityManager entManager,
        EntityUid uid,
        TransformComponent xform)
    {
        var ev = new AnchorStateChangedEvent(uid, xform);
        entManager.EventBus.RaiseLocalEvent(uid, ref ev);
    }
}
