/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Collections.Generic;
using System.Numerics;
using Content.Client._Moonlight.ZLevels.Core.Overlays;
using Content.Shared._Moonlight.Sprite;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Content.Shared.Camera;
using Content.Shared.StatusEffectNew.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Client._Moonlight.ZLevels.Core;

/// <summary>
/// Only process Eye offset and drawdepth on clientside
/// </summary>
public sealed partial class MoonlightClientZLevelsSystem : MoonlightSharedZLevelsSystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IEyeManager _eye = default!;

    internal readonly MoonlightZLevelOpeningCache OpeningCache = new(
        maxCachedChunksPerGrid: 1024,
        maxCachedChunksTotal: 4096);

    /// <summary>
    /// Entities with a non-zero visual Z contribution found by the pre-animation pass.
    /// The post-animation pass consumes this list instead of running the same global
    /// three-component query again.
    /// </summary>
    internal readonly List<Entity<MoonlightZPhysicsComponent, SpriteComponent, TransformComponent>> VisualEntities = new();
    internal readonly HashSet<EntityUid> VisualEntitySet = new();
    internal readonly HashSet<EntityUid> PendingVisualEntities = new();

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new MoonlightZLevelBlurOverlay());

        SubscribeLocalEvent<MoonlightZPhysicsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MoonlightZPhysicsComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
        SubscribeLocalEvent<MoonlightZPhysicsComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<EyeComponent, GetEyeOffsetEvent>(OnEyeOffset);
        SubscribeLocalEvent<GridRemovalEvent>(OnOpeningGridRemoved);
    }

    protected override void OnTileChanged(Entity<MapGridComponent> ent, ref TileChangedEvent args)
    {
        OpeningCache.InvalidateTiles(ent, args.Changes);
        base.OnTileChanged(ent, ref args);
    }

    private void OnOpeningGridRemoved(GridRemovalEvent args)
    {
        OpeningCache.RemoveGrid(args.EntityUid);
    }

    private void OnEyeOffset(Entity<EyeComponent> ent, ref GetEyeOffsetEvent args)
    {
        var target = ent.Comp.Target ?? ent.Owner;
        if (!ZPhysicsQuery.TryComp(target, out var physics))
            return;

        Angle rotation = _eye.CurrentEye.Rotation * -1;
        var localPosition = GetVisualLocalPosition(target, physics, Transform(target), ZPhysicsQuery);
        var offset = rotation.RotateVec(new Vector2(0, localPosition * ZLevelOffset));
        args.Offset += offset;
    }

    /// <summary>
    /// Entities riding/parented onto another Z-physics body (e.g. a rider buckled to a flying vehicle)
    /// never step their own LocalPosition, so they must visually follow the parent's height instead.
    /// </summary>
    internal static float GetVisualLocalPosition(EntityUid uid, MoonlightZPhysicsComponent zPhys, TransformComponent xform, EntityQuery<MoonlightZPhysicsComponent> zPhysicsQuery)
    {
        if (xform.ParentUid != xform.MapUid && zPhysicsQuery.TryComp(xform.ParentUid, out var parentZPhys))
            return parentZPhys.LocalPosition;

        return zPhys.LocalPosition;
    }

    private void OnStartup(Entity<MoonlightZPhysicsComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        PendingVisualEntities.Add(ent);

        if (sprite.SnapCardinals)
            return;

        ent.Comp.DrawDepthDefault = TryComp<MoonlightPerspectiveDepthComponent>(ent, out var perspective)
            ? perspective.BaseDrawDepth
            : sprite.DrawDepth;
        ent.Comp.SpriteOffsetDefault = sprite.Offset;
    }

    private void OnAfterHandleState(Entity<MoonlightZPhysicsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        PendingVisualEntities.Add(ent);
        // A server-started controller must also wake a body that has already settled locally.
        if (ent.Comp.VelocityRaiseEvent)
            RefreshBody(ent);
    }

    private void OnUnpaused(Entity<MoonlightZPhysicsComponent> ent, ref EntityUnpausedEvent args)
    {
        // Startup or state updates may have queued this body while its visuals were paused.
        PendingVisualEntities.Add(ent);
    }

    protected override void OnZPositionChanged(Entity<MoonlightZPhysicsComponent> ent)
    {
        PendingVisualEntities.Add(ent);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<MoonlightZLevelBlurOverlay>();
        VisualEntities.Clear();
        VisualEntitySet.Clear();
        PendingVisualEntities.Clear();
        OpeningCache.Clear();
    }
}

/// <summary>
/// Pre-animation pass for Z-level visuals.
/// Runs its <see cref="FrameUpdate"/> BEFORE <see cref="AnimationPlayerSystem"/> every render frame,
/// resetting <see cref="SpriteComponent.Offset"/> to the entity's clean base (no Z).
/// This prevents Z from accumulating across frames when no animation writes to the offset
/// (e.g. entities that only animate scale, like SlimeIceBig).
/// </summary>
internal sealed partial class MoonlightClientZLevelsPreAnimSystem : EntitySystem
{
    [Dependency] private MoonlightClientZLevelsSystem _zLevels = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<MoonlightZPhysicsComponent> _zPhysQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private EntityQuery<MetaDataComponent> _metaQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        var visualEntities = _zLevels.VisualEntities;
        var visualEntitySet = _zLevels.VisualEntitySet;

        // Phase 1 (per render frame): strip any Z left from last frame so the animation player
        // always starts from a Z-free base, and Phase 2 can add exactly one Z contribution.
        for (var i = visualEntities.Count - 1; i >= 0; i--)
        {
            var entity = visualEntities[i];
            if (entity.Comp1.Deleted || entity.Comp2.Deleted || entity.Comp3.Deleted ||
                !_metaQuery.TryComp(entity.Owner, out var metadata))
            {
                visualEntitySet.Remove(entity.Owner);
                visualEntities.RemoveAt(i);
                continue;
            }

            // Keep the last composed frame intact, including any rotor attitude contribution.
            if (metadata.EntityPaused)
                continue;

            var localPosition = PrepareVisual(entity.Owner, entity.Comp1, entity.Comp2, entity.Comp3);
            if (localPosition == 0f)
            {
                visualEntitySet.Remove(entity.Owner);
                visualEntities.RemoveAt(i);
                continue;
            }

            TrackChildren(entity.Comp3);
        }

        // Component startup/state/parent changes catch newly spawned or network-updated bodies.
        foreach (var uid in _zLevels.PendingVisualEntities)
        {
            if (TryTrackVisual(uid, out var xform))
                TrackChildren(xform);
        }
        _zLevels.PendingVisualEntities.Clear();

        // Set parent-synced status effect offsets to the parent's current Z value each frame.
        var syncQuery = EntityQueryEnumerator<StatusEffectComponent, SpriteComponent, TransformComponent>();
        while (syncQuery.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            var parent = xform.ParentUid;
            if (_mapGridQuery.HasComp(parent))
                continue;
            if (!_zPhysQuery.TryComp(parent, out var parentZPhys))
                continue;
            var zOffset = new Vector2(0, parentZPhys.LocalPosition * MoonlightSharedZLevelsSystem.ZLevelOffset);
            if (sprite.Offset != zOffset)
                _sprite.SetOffset((uid, sprite), zOffset);
        }
    }

    private bool TryTrackVisual(EntityUid uid, out TransformComponent xform)
    {
        xform = null!;
        if (!_metaQuery.TryComp(uid, out var metadata) || metadata.EntityPaused)
            return false;

        if (_zLevels.VisualEntitySet.Contains(uid))
        {
            if (!_xformQuery.TryComp(uid, out var trackedXform))
                return false;

            xform = trackedXform;
            return true;
        }

        if (!_zPhysQuery.TryComp(uid, out var zPhys) ||
            !_spriteQuery.TryComp(uid, out var sprite) ||
            !_xformQuery.TryComp(uid, out var foundXform))
        {
            return false;
        }

        xform = foundXform;

        if (PrepareVisual(uid, zPhys, sprite, xform) == 0f)
            return false;

        _zLevels.VisualEntitySet.Add(uid);
        _zLevels.VisualEntities.Add((uid, zPhys, sprite, xform));
        return true;
    }

    private void TrackChildren(TransformComponent xform)
    {
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
            TryTrackVisual(child, out _);
    }

    private float PrepareVisual(
        EntityUid uid,
        MoonlightZPhysicsComponent zPhys,
        SpriteComponent sprite,
        TransformComponent xform)
    {
        var localPosition = MoonlightClientZLevelsSystem.GetVisualLocalPosition(uid, zPhys, xform, _zPhysQuery);

        if (sprite.Offset != zPhys.SpriteOffsetDefault)
            _sprite.SetOffset((uid, sprite), zPhys.SpriteOffsetDefault);

        var drawDepth = localPosition > 0
            ? (int) Shared.DrawDepth.DrawDepth.OverMobs
            : zPhys.DrawDepthDefault;
        if (sprite.DrawDepth != drawDepth)
            _sprite.SetDrawDepth((uid, sprite), drawDepth);

        return localPosition;
    }
}

/// <summary>
/// Post-animation pass for Z-level visuals.
/// Runs its <see cref="FrameUpdate"/> AFTER <see cref="AnimationPlayerSystem"/> so that
/// whatever offset the animation player wrote to <see cref="SpriteComponent.Offset"/> this frame
/// (loop animation, one-shot swing, idle bob, etc.) is preserved, and the Z-height contribution
/// is simply added on top.  No animation code needs to know about Z levels.
/// </summary>
internal sealed partial class MoonlightClientZLevelsPostAnimSystem : EntitySystem
{
    [Dependency] private MoonlightClientZLevelsSystem _zLevels = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private EntityQuery<MoonlightZPhysicsComponent> _zPhysQuery = default!;
    [Dependency] private EntityQuery<MetaDataComponent> _metaQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        // Phase 2: add the Z-height contribution on top of the animation-player's output.
        // At this point sprite.Offset == animationValue (or SpriteOffsetDefault if no anim ran).
        // The offset is counter-rotated by the entity's world angle so it always points world-up,
        // preventing it from orbiting the pivot when the entity has angular velocity (e.g. shurikens).
        var visualEntities = _zLevels.VisualEntities;
        for (var i = 0; i < visualEntities.Count; i++)
        {
            var entity = visualEntities[i];
            var zPhys = entity.Comp1;
            var sprite = entity.Comp2;
            var xform = entity.Comp3;

            // An animation or another system may have removed the entity between the
            // two passes. The cached component references are otherwise safe to reuse.
            if (zPhys.Deleted || sprite.Deleted || xform.Deleted ||
                !_metaQuery.TryComp(entity.Owner, out var metadata) || metadata.EntityPaused)
                continue;

            var localPosition = MoonlightClientZLevelsSystem.GetVisualLocalPosition(entity.Owner, zPhys, xform, _zPhysQuery);

            // At ground level there is no Z contribution. In particular, do not call
            // GetWorldRotation or rebuild the sprite matrix for the common case.
            if (localPosition == 0f)
                continue;

            var rawZ = new Vector2(0, localPosition * MoonlightSharedZLevelsSystem.ZLevelOffset);
            Vector2 zOffset;
            if (sprite.NoRotation)
                zOffset = rawZ;
            else
                zOffset = new Angle(-_xform.GetWorldRotation(xform)).RotateVec(rawZ);
            _sprite.SetOffset((entity.Owner, sprite), sprite.Offset + zOffset);
        }
    }
}
