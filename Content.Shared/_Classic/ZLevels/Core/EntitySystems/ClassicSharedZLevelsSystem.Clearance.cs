using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Classic.ZLevels.Core.EntitySystems;

public abstract partial class ClassicSharedZLevelsSystem
{
    /// <summary>
    /// Tests the entity's complete hard fixtures at the same world pose on another map.
    /// Unlike the ground cache, this includes obstructions underneath a long body's nose and tail.
    /// </summary>
    /// <param name="includeFloors">Also reject supporting tiles when ascending through the destination's floor.</param>
    [PublicAPI]
    public bool CanOccupyMap(EntityUid uid, EntityUid destination, bool includeFloors = false)
    {
        if (!_mapQuery.TryComp(destination, out var map))
            return false;
        if (!_fixturesQuery.TryComp(uid, out var fixtures))
            return true;

        var pose = new Transform(_transform.GetWorldPosition(uid), (float) _transform.GetWorldRotation(uid).Theta);
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            var query = new FixtureQueryArgs(new QueryFilter
            {
                MaskBits = fixture.CollisionMask,
                LayerBits = fixture.CollisionLayer,
                Flags = QueryFlags.Static | QueryFlags.Dynamic,
            }, IgnoreShapeSkin: true);

            for (var child = 0; child < fixture.Shape.ChildCount; child++)
            {
                // Grid floors are not returned as fixtures by EntityLookupSystem.
                if (includeFloors && IntersectsFloor(uid, map.MapId, fixture.Shape.ComputeAABB(pose, child), pose))
                    return false;

                var excluded = uid;
                if (!_lookup.ForEachFixtureIntersecting(map.MapId, fixture.Shape, child, pose,
                        ref excluded, new ClearanceCallback(), query))
                    return false;
            }
        }

        return true;
    }

    private bool IntersectsFloor(EntityUid uid, MapId destination, Box2 bounds, Transform pose)
    {
        var blocked = false;
        _map.FindGridsIntersecting(destination, bounds, (gridUid, grid) =>
        {
            // Explicit poses let the physics query compare bodies on different maps. This
            // also handles map grids, rotated grids and every child of a compound hull.
            if (!_physicsSystem.TryGetNearest(uid, gridUid, out _, out _, out var distance,
                    pose, _physicsSystem.GetPhysicsTransform(gridUid)) || distance > 0f)
                return true;

            blocked = true;
            return false;
        }, approx: true);
        return blocked;
    }

    private readonly struct ClearanceCallback : IFixtureQueryCallback<EntityUid>
    {
        public bool Invoke(ref EntityUid excluded, in FixtureProxy fixture)
        {
            return fixture.Entity == excluded || !fixture.Fixture.Hard;
        }
    }
}
