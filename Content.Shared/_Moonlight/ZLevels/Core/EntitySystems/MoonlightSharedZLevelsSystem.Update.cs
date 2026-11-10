/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared.Chasm;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Moonlight.ZLevels.Core.EntitySystems;

public abstract partial class MoonlightSharedZLevelsSystem
{
    [Dependency] private EntityQuery<MetaDataComponent> _zBodyMetaQuery = default!;

    public int UpdateCalls { get; private set; }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateCalls = 0;
        FlushMovementBodyRefreshes();

        if (_net.IsClient && !_clientSimulation)
            return;

        _accumulatedTime += TimeSpan.FromSeconds(frameTime);

        var steps = 0;
        while (_accumulatedTime >= _fixedTimestep && steps < MaxStepsPerFrame)
        {
            UpdateZPhysics((float) _fixedTimestep.TotalSeconds);
            _accumulatedTime -= _fixedTimestep;

            steps++;
        }
    }

    private void UpdateZPhysics(float frameTime)
    {
        UpdateDirtyMovement();

        // Landing, damage and map transitions may wake/sleep other bodies during this loop.
        // A reused snapshot keeps iteration stable while membership changes remain O(1).
        _activeBodySnapshot.Clear();
        _activeBodySnapshot.AddRange(_activeBodies);
        for (var i = _activeBodySnapshot.Count - 1; i >= 0; i--)
        {
            var uid = _activeBodySnapshot[i];
            if (!_activeBodies.Contains(uid))
                continue;

            if (!ZPhysicsQuery.TryComp(uid, out var zPhysicsComponent) ||
                !_transformQuery.TryComp(uid, out var xform) ||
                !_physicsQuery.TryComp(uid, out var physics))
            {
                _activeBodies.Remove(uid);
                continue;
            }

            if (!_zMapQuery.HasComp(xform.MapUid))
            {
                _activeBodies.Remove(uid);
                continue;
            }

            if (_zBodyMetaQuery.GetComponent(uid).EntityPaused)
                continue;

            ProcessZPhysics((uid, zPhysicsComponent, physics), frameTime);
        }
    }

    private void ProcessZPhysics(Entity<MoonlightZPhysicsComponent, PhysicsComponent> entity, float frameTime)
    {
        var zPhysicsComponent = entity.Comp1;

        if (zPhysicsComponent.Disabled)
            return;

        UpdateCalls++;

        var oldVelocity = zPhysicsComponent.Velocity;
        var oldHeight = zPhysicsComponent.LocalPosition;

        if (zPhysicsComponent.VelocityGravity)
            zPhysicsComponent.Velocity -= ZGravityForce * zPhysicsComponent.GravityMultiplier * frameTime;

        if (zPhysicsComponent.VelocityRaiseEvent)
        {
            var velocityEvent = new MoonlightGetZVelocityEvent((entity, zPhysicsComponent), frameTime);
            RaiseLocalEvent(entity, ref velocityEvent);

            zPhysicsComponent.Velocity += velocityEvent.VelocityDelta * frameTime;
        }

        zPhysicsComponent.LocalPosition += zPhysicsComponent.Velocity * frameTime;
        var distanceToGround = zPhysicsComponent.LocalPosition - zPhysicsComponent.CachedGroundHeight;

        if (zPhysicsComponent.AutoStep && distanceToGround < 0)
            zPhysicsComponent.LocalPosition -= distanceToGround;

        if (zPhysicsComponent.CachedStickyGround)
            zPhysicsComponent.LocalPosition -= distanceToGround;

        if (zPhysicsComponent is { Velocity: < 0, Fallable: true })
        {
            if (distanceToGround <= 0.05f)
            {
                RaiseImpact(entity, zPhysicsComponent.Velocity);

                if (float.Abs(zPhysicsComponent.Velocity) < zPhysicsComponent.SleepThreshold)
                {
                    zPhysicsComponent.Velocity = 0;
                    zPhysicsComponent.LocalPosition = zPhysicsComponent.CachedGroundHeight;
                }
                else
                {
                    zPhysicsComponent.Velocity = -zPhysicsComponent.Velocity * zPhysicsComponent.Bounciness;
                }

                // Resolve floor penetration before attempting a transition to the map below.
                zPhysicsComponent.LocalPosition = MathF.Max(zPhysicsComponent.LocalPosition, zPhysicsComponent.CachedGroundHeight);
            }
        }

        if (zPhysicsComponent.LocalPosition < 0)
        {
            if (TryMoveDownOrChasm(entity, out var blocked))
            {
                zPhysicsComponent.LocalPosition += 1;
                if (zPhysicsComponent is { CachedStickyGround: false, Fallable: true })
                {
                    var fallEv = new MoonlightZLevelFallMapEvent();
                    RaiseLocalEvent(entity, ref fallEv);
                }
            }
            else if (blocked || !HasComp<ChasmFallingComponent>(entity))
            {
                // A cancelled chasm attempt keeps the body at the network boundary.
                zPhysicsComponent.LocalPosition = 0f;
                var impactVelocity = zPhysicsComponent.Velocity;
                if (blocked)
                    RaiseImpact(entity, impactVelocity);
                zPhysicsComponent.Velocity = blocked ? -impactVelocity * zPhysicsComponent.Bounciness : 0f;
            }
        }

        if (zPhysicsComponent.LocalPosition >= 1)
        {
            if (HasTileAbove(entity))
            {
                RaiseImpact(entity, zPhysicsComponent.Velocity);
                zPhysicsComponent.LocalPosition = 1;
                zPhysicsComponent.Velocity = -zPhysicsComponent.Velocity * zPhysicsComponent.Bounciness;
                RaiseMapMoveBlocked(entity, 1);
            }
            else
            {
                if (TryMove(entity, 1, out var blocked))
                {
                    zPhysicsComponent.LocalPosition -= 1;
                }
                else if (blocked)
                {
                    var impactVelocity = zPhysicsComponent.Velocity;
                    RaiseImpact(entity, impactVelocity);
                    zPhysicsComponent.LocalPosition = 1f;
                    zPhysicsComponent.Velocity = -impactVelocity * zPhysicsComponent.Bounciness;
                }
                else
                {
                    // There is no map above the network: do not integrate beyond its upper edge.
                    zPhysicsComponent.LocalPosition = 1f;
                    zPhysicsComponent.Velocity = 0f;
                }
            }
        }

        if (float.Abs(zPhysicsComponent.Velocity) > ZVelocityLimit)
            zPhysicsComponent.Velocity = float.Sign(zPhysicsComponent.Velocity) * ZVelocityLimit;

        if (float.Abs(oldVelocity - zPhysicsComponent.Velocity) > 0.001f)
            DirtyField(entity, zPhysicsComponent, nameof(MoonlightZPhysicsComponent.Velocity));

        if (oldHeight != zPhysicsComponent.LocalPosition)
        {
            OnZPositionChanged((entity.Owner, zPhysicsComponent));

            if (float.Abs(oldHeight - zPhysicsComponent.LocalPosition) > 0.001f)
                DirtyField(entity, zPhysicsComponent, nameof(MoonlightZPhysicsComponent.LocalPosition));
        }

        if (zPhysicsComponent.VelocityGravity)
        {
            var targetStatus = distanceToGround > AirborneHeightThreshold ? BodyStatus.InAir : BodyStatus.OnGround;
            if (entity.Comp2.BodyStatus != targetStatus)
            {
                _physicsSystem.SetBodyStatus(entity, entity.Comp2, targetStatus);
                var statusEv = new MoonlightZBodyStatusChangedEvent(targetStatus);
                RaiseLocalEvent(entity, ref statusEv);
            }
        }

        SleepUpdate((entity, entity.Comp1), frameTime);
    }

    private void RaiseImpact(EntityUid uid, float velocity)
    {
        if (float.Abs(velocity) < ImpactVelocityLimit)
            return;

        var hit = new MoonlightZLevelHitEvent(float.Abs(velocity));
        RaiseLocalEvent(uid, ref hit);
        var land = new LandEvent(null, true);
        RaiseLocalEvent(uid, ref land);
    }

    private void SleepUpdate(Entity<MoonlightZPhysicsComponent> entity, float frameTime)
    {
        var distanceToGround = entity.Comp.LocalPosition - entity.Comp.CachedGroundHeight;
        var almostStopped = float.Abs(entity.Comp.Velocity) < entity.Comp.SleepThreshold && float.Abs(distanceToGround) <= 0.01f;

        if (!almostStopped)
        {
            entity.Comp.SleepTimer = 0f;
            return;
        }

        entity.Comp.SleepTimer += frameTime;
        if (entity.Comp.SleepTimer < entity.Comp.TimeToSleep)
            return;

        SleepBody((entity, entity));
    }
}

/// <summary>
/// Raised directed on an entity when its BodyStatus changes due to Z-physics height sync.
/// </summary>
[ByRefEvent]
public readonly record struct MoonlightZBodyStatusChangedEvent(BodyStatus NewStatus);
