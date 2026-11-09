/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Classic.ZLevels.Core.Components;
using JetBrains.Annotations;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Classic.ZLevels.Core.EntitySystems;

public abstract partial class ClassicSharedZLevelsSystem
{
    private readonly HashSet<EntityUid> _activeBodies = new();
    private readonly List<EntityUid> _activeBodySnapshot = new();

    public IReadOnlyCollection<EntityUid> ActiveBodies => _activeBodies;

    private void InitializeActivation()
    {
        SubscribeLocalEvent<ClassicZPhysicsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClassicZPhysicsComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<ClassicZPhysicsComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<ClassicZPhysicsComponent, PhysicsBodyTypeChangedEvent>(OnPhysicsBodyTypeChanged);

        SubscribeLocalEvent<ClassicZPhysicsComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnMapInit(Entity<ClassicZPhysicsComponent> entity, ref MapInitEvent args)
    {
        RefreshBody(entity);

        var mapUid = Transform(entity).MapUid;

        if (!_zMapQuery.TryComp(mapUid, out var zLevel))
            return;

        if (entity.Comp.CurrentZLevel == zLevel.Depth)
            return;

        entity.Comp.CurrentZLevel = zLevel.Depth;
        DirtyField(entity, entity.Comp, nameof(ClassicZPhysicsComponent.CurrentZLevel));
    }

    private void OnShutdown(Entity<ClassicZPhysicsComponent> entity, ref ComponentShutdown args)
    {
        SleepBody((entity, entity));
    }

    private void OnAnchorStateChanged(Entity<ClassicZPhysicsComponent> entity, ref AnchorStateChangedEvent args)
    {
        RefreshBody(entity);
    }

    private void OnPhysicsBodyTypeChanged(Entity<ClassicZPhysicsComponent> entity, ref PhysicsBodyTypeChangedEvent args)
    {
        RefreshBody(entity);
    }

    private void OnParentChanged(Entity<ClassicZPhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        OnZPositionChanged(entity);
        RefreshBody(entity);

        if (ZPhysicsQuery.TryComp(args.OldParent, out var oldParentPhysics))
        {
            SetZPosition((entity, entity), oldParentPhysics.LocalPosition);
            return;
        }

        if (ZPhysicsQuery.HasComp(Transform(entity).ParentUid))
        {
            SetZPosition((entity, entity), 0);
        }
    }

    [PublicAPI]
    public void WakeBody(Entity<ClassicZPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        if (!_activeBodies.Add(entity))
            return;

        entity.Comp.Sleeping = false;
        entity.Comp.SleepTimer = 0f;
    }

    [PublicAPI]
    public void SleepBody(Entity<ClassicZPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        entity.Comp.Sleeping = true;
        entity.Comp.SleepTimer = 0f;

        _activeBodies.Remove(entity);
    }

    [PublicAPI]
    public void RefreshBody(Entity<ClassicZPhysicsComponent> entity)
    {
        if (TerminatingOrDeleted(entity))
        {
            SleepBody((entity, entity));
            return;
        }

        var transform = Transform(entity);
        var parent = transform.ParentUid;

        var onMap = parent == transform.GridUid || parent == transform.MapUid;

        if (!onMap
            || transform.Anchored
            || _physicsQuery.TryComp(entity, out var physics)
            && physics.BodyType == BodyType.Static)
        {
            SleepBody((entity, entity));
            return;
        }

        WakeBody((entity, entity));
    }
}
