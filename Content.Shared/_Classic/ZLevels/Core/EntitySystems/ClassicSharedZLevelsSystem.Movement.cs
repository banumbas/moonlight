/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared.Chasm;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Inventory;
using Content.Shared.Physics;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.ZLevels.Core.EntitySystems;

public abstract partial class ClassicSharedZLevelsSystem
{
    private TimeSpan _accumulatedTime = TimeSpan.Zero;
    private readonly List<EntityUid> _dirtyMovementBodies = new();
    private readonly HashSet<EntityUid> _dirtyMovementBodySet = new();
    private readonly HashSet<EntityUid> _movementBodyLookup = new();
    private Dictionary<(MapId Map, Vector2i Chunk), Box2> _pendingMovementRefreshes = new();
    private Dictionary<(MapId Map, Vector2i Chunk), Box2> _processingMovementRefreshes = new();

    private const int MovementRefreshChunkSize = 8;
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    [Dependency] private TagSystem _tag = default!;

    private void InitializeMovement()
    {
        SubscribeLocalEvent<ClassicZPhysicsComponent, ClassicZLevelMapMoveEvent>(OnZLevelMapMove);
        SubscribeLocalEvent<ClassicZPhysicsComponent, MoveEvent>(OnMoveEvent);
        SubscribeLocalEvent<MapGridComponent, TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<FixturesComponent, AnchorStateChangedEvent>(OnBlockingAnchorChanged);
        SubscribeLocalEvent<CollisionChangeEvent>(OnBlockingCollisionChanged);
        SubscribeLocalEvent<CollisionLayerChangeEvent>(OnBlockingCollisionLayerChanged);
        SubscribeLocalEvent<DoorStateChangedEvent>(OnBlockingDoorStateChanged);
    }

    /// <summary>
    /// Returns the last cached distance to the floor.
    /// </summary>
    /// <param name="target">The entity, the distance to the floor which we calculate</param>
    /// <returns></returns>
    [PublicAPI]
    public float DistanceToGround(Entity<ClassicZPhysicsComponent?> target)
    {
        if (!Resolve(target, ref target.Comp, false))
            return 0;

        return target.Comp.LocalPosition - target.Comp.CachedGroundHeight;
    }

    protected virtual void OnTileChanged(Entity<MapGridComponent> ent, ref TileChangedEvent args)
    {
        if (_net.IsClient && !_clientSimulation)
            return;

        var xform = Transform(ent);
        if (xform.MapUid is not { } mapUid ||
            !_zMapQuery.TryComp(mapUid, out var zMap) ||
            args.Changes.Length == 0)
        {
            return;
        }

        MapId? aboveMapId = null;
        if (TryMapUp((mapUid, zMap), out var mapAbove) && _mapQuery.TryComp(mapAbove.Owner, out var mapAboveComp))
            aboveMapId = mapAboveComp.MapId;

        var half = ent.Comp.TileSizeHalfVector;
        foreach (var change in args.Changes)
        {
            // Ground height depends on the presence of a tile, not its material or variant.
            // Wall/door collision changes are handled separately below.
            if (change.OldTile.IsEmpty == change.NewTile.IsEmpty)
                continue;

            var center = _map.GridTileToWorld(ent.Owner, ent.Comp, change.GridIndices).Position;
            var tileBounds = new Box2(center - half, center + half);
            QueueMovementRefresh(xform.MapID, center, tileBounds);

            // This level is also the landing surface for bodies one Z-level above.
            if (aboveMapId is { } above)
                QueueMovementRefresh(above, center, tileBounds);
        }
    }

    private void OnBlockingAnchorChanged(Entity<FixturesComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (_net.IsClient && !_clientSimulation)
            return;

        if (IsBlockingLandingLayer(ent.Owner, ent.Comp))
            QueueMovementBodiesAbove(args.Transform);
    }

    private void OnBlockingCollisionChanged(ref CollisionChangeEvent args)
    {
        if (_net.IsClient && !_clientSimulation)
            return;

        if (!_fixturesQuery.TryComp(args.BodyUid, out var fixtures) ||
            !IsBlockingLandingLayer(args.BodyUid, fixtures))
        {
            return;
        }

        var xform = Transform(args.BodyUid);
        if (xform.Anchored)
            QueueMovementBodiesAbove(xform);
    }

    private void OnBlockingCollisionLayerChanged(ref CollisionLayerChangeEvent args)
    {
        if (_net.IsClient && !_clientSimulation)
            return;

        // The old layer is not part of this event. Refreshing any anchored layer change handles
        // both adding and removing a full-height blocking layer.
        var xform = Transform(args.Body.Owner);
        if (xform.Anchored)
            QueueMovementBodiesAbove(xform);
    }

    private void OnBlockingDoorStateChanged(DoorStateChangedEvent args)
    {
        if (_net.IsClient && !_clientSimulation)
            return;

        // Doors normally toggle fixture hardness without changing their collision layer. Their
        // explicit state event fills that gap so the cached landing surface follows open/closed.
        if (!args.Door.IsValid())
            return;

        var xform = Transform(args.Door);
        if (xform.Anchored)
            QueueMovementBodiesAbove(xform);
    }

    private void QueueMovementBodiesAbove(TransformComponent blockerXform)
    {
        if (_net.IsClient && !_clientSimulation)
            return;

        if (blockerXform.GridUid is not { } gridUid ||
            !_gridQuery.TryComp(gridUid, out var grid) ||
            blockerXform.MapUid is not { } mapUid ||
            !_zMapQuery.TryComp(mapUid, out var zMap) ||
            !TryMapUp((mapUid, zMap), out var mapAbove) ||
            !_mapQuery.TryComp(mapAbove.Owner, out var mapAboveComp))
        {
            return;
        }

        var tile = _map.TileIndicesFor(gridUid, grid, blockerXform.Coordinates);
        var center = _map.GridTileToWorld(gridUid, grid, tile).Position;
        var half = grid.TileSizeHalfVector;
        var bounds = new Box2(center - half, center + half);
        QueueMovementRefresh(mapAboveComp.MapId, center, bounds);
    }

    private void QueueMovementRefresh(MapId mapId, Vector2 center, Box2 bounds)
    {
        // Keep sparse edits local, and coalesce tile/fixture events from streamed chunks before
        // querying the broadphase. One large union would also wake bodies between distant edits.
        var chunk = new Vector2i(
            (int) MathF.Floor(center.X / MovementRefreshChunkSize),
            (int) MathF.Floor(center.Y / MovementRefreshChunkSize));
        var key = (mapId, chunk);

        if (_pendingMovementRefreshes.TryGetValue(key, out var existing))
            _pendingMovementRefreshes[key] = existing.Union(bounds);
        else
            _pendingMovementRefreshes[key] = bounds;
    }

    private void FlushMovementBodyRefreshes()
    {
        if (_net.IsClient && !_clientSimulation)
        {
            _pendingMovementRefreshes.Clear();
            _processingMovementRefreshes.Clear();
            _dirtyMovementBodies.Clear();
            _dirtyMovementBodySet.Clear();
            return;
        }

        if (_pendingMovementRefreshes.Count == 0)
            return;

        (_pendingMovementRefreshes, _processingMovementRefreshes) =
            (_processingMovementRefreshes, _pendingMovementRefreshes);

        foreach (var ((mapId, _), bounds) in _processingMovementRefreshes)
        {
            // Anchor/collision events can be queued while a Z-network is being torn down.
            // Never resolve a broadphase for a map that no longer exists on the next tick.
            if (_map.MapExists(mapId))
                RefreshMovementBodies(mapId, bounds);
        }

        _processingMovementRefreshes.Clear();
    }

    private void RefreshMovementBodies(MapId mapId, Box2 bounds)
    {
        _movementBodyLookup.Clear();
        _lookup.GetEntitiesIntersecting(mapId, bounds, _movementBodyLookup, LookupFlags.Uncontained);

        foreach (var uid in _movementBodyLookup)
        {
            if (!ZPhysicsQuery.HasComp(uid))
                continue;

            QueueDirtyMovement(uid);
        }

        _movementBodyLookup.Clear();
    }

    private bool IsBlockingLandingCell(EntityUid uid, FixturesComponent fixtures)
    {
        // Only full-tile walls and doors are ceilings for a falling body. Other anchored machines
        // may use HighImpassable fixtures without occupying the whole vertical cell.
        if (!_tag.HasTag(uid, WallTag) && !HasComp<DoorComponent>(uid))
            return false;

        return _physicsQuery.TryComp(uid, out var body) &&
               body.CanCollide &&
               IsBlockingLandingLayer(uid, fixtures);
    }

    private bool IsBlockingLandingLayer(EntityUid uid, FixturesComponent fixtures)
    {
        var (layer, _) = _physicsSystem.GetHardCollision(uid, fixtures);
        return (layer & (int) CollisionGroup.HighImpassable) != 0;
    }

    private void RequestCacheMovement(Entity<ClassicZPhysicsComponent> entity, bool force = true)
    {
        var tile = _transform.GetGridOrMapTilePosition(entity);

        if (tile == entity.Comp.CachedTile && !force)
            return;

        entity.Comp.CachedTile = tile;
        entity.Comp.CachedGroundHeight = ComputeGroundHeightInternal((entity, entity), out var sticky);
        entity.Comp.CachedStickyGround = sticky;
    }

    public void RefreshGroundAfterTeleport(Entity<ClassicZPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        if (_zMapQuery.TryComp(Transform(entity).MapUid, out var zLevel) &&
            entity.Comp.CurrentZLevel != zLevel.Depth)
        {
            entity.Comp.CurrentZLevel = zLevel.Depth;
            DirtyField(entity, entity.Comp, nameof(ClassicZPhysicsComponent.CurrentZLevel));
        }

        RequestCacheMovement((entity.Owner, entity.Comp));
        RefreshBody((entity.Owner, entity.Comp));
    }

    private void OnMoveEvent(Entity<ClassicZPhysicsComponent> entity, ref MoveEvent args)
    {
        if (_net.IsClient && !_clientSimulation)
            return;

        QueueDirtyMovement(entity);
    }

    private void QueueDirtyMovement(EntityUid uid)
    {
        if (_dirtyMovementBodySet.Add(uid))
            _dirtyMovementBodies.Add(uid);
    }

    private void OnZLevelMapMove(Entity<ClassicZPhysicsComponent> ent, ref ClassicZLevelMapMoveEvent args)
    {
        ent.Comp.CurrentZLevel = args.CurrentZLevel;
        DirtyField(ent, ent.Comp, nameof(ClassicZPhysicsComponent.CurrentZLevel));
        RequestCacheMovement(ent);
    }

    /// <summary>
    /// Computes the "ground height" relative to the entity's current Z-level.
    /// Returns values where 0 means ground on the same level, -1 means ground one level below,
    /// and intermediate values are possible for high ground entities (stairs).
    /// </summary>
    private float ComputeGroundHeightInternal(Entity<ClassicZPhysicsComponent?> target, out bool stickyGround, int maxFloors = 1)
    {
        stickyGround = false;

        if (!Resolve(target, ref target.Comp, false))
            return 0;

        var xform = Transform(target);
        if (!_zMapQuery.TryComp(xform.MapUid, out var zMapComp))
            return 0;

        var worldPos = _transform.GetWorldPosition(target);

        //Select current map by default
        Entity<ClassicZMapComponent> checkingMap = (xform.MapUid.Value, zMapComp);

        for (var floor = 0; floor <= maxFloors; floor++)
        {
            if (floor != 0) //Select map below
            {
                if (!TryMapOffset((checkingMap.Owner, checkingMap.Comp), -floor, out var tempCheckingMap))
                    continue;

                checkingMap = tempCheckingMap;
            }

            //Find whichever grid (structure or planet) provides the floor here.
            if (!_map.TryFindGridAt(checkingMap, worldPos, out var gridUid, out var grid))
                continue;

            var gridTile = _map.WorldToTile(gridUid, grid, worldPos);

            // A full-height collider on the level below behaves as a solid top surface. Crossing
            // the map boundary would otherwise reparent a falling entity inside a wall or door.
            if (floor > 0)
            {
                var blockers = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, gridTile);
                while (blockers.MoveNext(out var blocker))
                {
                    if (_fixturesQuery.TryComp(blocker.Value, out var fixtures) &&
                        IsBlockingLandingCell(blocker.Value, fixtures))
                    {
                        return 1f - floor;
                    }
                }
            }

            //Check all types of ZHeight entities
            var query = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, gridTile);
            while (query.MoveNext(out var uid))
            {
                if (!_zHighGroundQuery.TryComp(uid, out var heightComp))
                    continue;

                var dir = Transform(uid.Value).LocalRotation.GetCardinalDir();

                var gridLocal = _map.WorldToLocal(gridUid, grid, worldPos);
                var local = new Vector2((gridLocal.X % 1 + 1) % 1, (gridLocal.Y % 1 + 1) % 1);

                var t = dir switch
                {
                    Direction.East => heightComp.Corner ? (local.X + 1f - local.Y) / 2f : local.X,
                    Direction.West => heightComp.Corner ? (1f - local.X + local.Y) / 2f : 1f - local.X,
                    Direction.North => heightComp.Corner ? (local.X + local.Y) / 2f : local.Y,
                    Direction.South => heightComp.Corner ? (1f - local.X + 1f - local.Y) / 2f : 1f - local.Y,
                    _ => 0.5f,
                };

                t = float.Clamp(t, 0f, 1f);

                var curve = heightComp.HeightCurve;
                if (curve.Count == 0)
                    continue;

                if (curve.Count == 1)
                {
                    var groundY = curve[0];
                    // groundHeight is negative downwards: -floor + groundY
                    return -floor + groundY;
                }

                var step = 1f / (curve.Count - 1);
                var index = (int)(t / step);
                var frac = (t - index * step) / step;

                var y0 = curve[Math.Clamp(index, 0, curve.Count - 1)];
                var y1 = curve[Math.Clamp(index + 1, 0, curve.Count - 1)];

                var groundYInterp = MathHelper.Lerp(y0, y1, frac);

                if (target.Comp.Velocity < 0 && target.Comp.Velocity > -2f && heightComp.Stick)
                    stickyGround = true;

                return -floor + groundYInterp;
            }

            //No ZEntities found, check floor tiles
            if (_map.TryGetTileRef(gridUid, grid, gridTile, out var tileRef) &&
                !tileRef.Tile.IsEmpty)
                return -floor; // tile ground has groundY == 0 -> -floor
        }

        return -maxFloors;
    }

    /// <summary>
    /// Checks whether there is a ceiling above the specified entity (tiles on the layer above).
    /// If there are no Z-levels above, false will be returned.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(EntityUid ent, Entity<ClassicZMapComponent?>? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;

        if (currentMapUid is null)
            return false;

        if (!TryMapUp(currentMapUid.Value, out var mapAboveUid))
            return false;

        var worldPos = _transform.GetWorldPosition(ent);
        if (!_map.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        if (_map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef) &&
            !tileRef.Tile.IsEmpty)
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether there is a ceiling above the specified entity (tiles on the layer above).
    /// If there are no Z-levels above, false will be returned.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(Vector2i indices, Entity<ClassicZMapComponent?> map)
    {
        if (!Resolve(map, ref map.Comp, false))
        {
            var mapUid = Transform(map.Owner).MapUid;
            if (mapUid is null || !_zMapQuery.TryComp(mapUid.Value, out var mapComp))
                return false;

            map = (mapUid.Value, mapComp);
        }

        if (!_gridQuery.TryComp(map.Owner, out var currentGrid))
            return false;

        if (!TryMapUp(map, out var mapAboveUid))
            return false;

        var worldTile = GridTileToWorldTile(map.Owner, currentGrid, indices);
        var worldPos = new Vector2(worldTile.X + 0.5f, worldTile.Y + 0.5f);
        if (!_map.TryFindGridAt(mapAboveUid, worldPos, out var mapAboveGridUid, out var mapAboveGrid))
            return false;

        if (_map.TryGetTileRef(mapAboveGridUid, mapAboveGrid, worldPos, out var tileRef) &&
            !tileRef.Tile.IsEmpty)
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether any grid on the map above has a non-empty tile at the given world position.
    /// World-position overload; see also <see cref="HasTileAbove(EntityUid, Entity{ClassicZMapComponent?}?)"/>
    /// and <see cref="HasTileAbove(Vector2i, Entity{ClassicZMapComponent?})"/>.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(Vector2 worldPos, Entity<ClassicZMapComponent?> currentMap)
    {
        if (!TryMapUp(currentMap, out var mapAboveUid))
            return false;

        if (!_map.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        return _map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    [PublicAPI]
    public void SetZPosition(Entity<ClassicZPhysicsComponent?> ent, float newPosition)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.LocalPosition = newPosition;
        DirtyField(ent, ent.Comp, nameof(ClassicZPhysicsComponent.LocalPosition));
        OnZPositionChanged((ent, ent.Comp));
        WakeBody(ent);
    }

    /// <summary>
    /// Allows the client implementation to maintain its visual Z set without scanning every
    /// entity carrying <see cref="ClassicZPhysicsComponent"/> each render frame.
    /// </summary>
    protected virtual void OnZPositionChanged(Entity<ClassicZPhysicsComponent> ent)
    {
    }

    [PublicAPI]
    public void UpdateGravityState(Entity<ClassicZPhysicsComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var ev = new ClassicCheckGravityEvent();
        RaiseLocalEvent(ent.Owner, ev);

        SetZGravity(ent, ev.Gravity);
    }

    private void SetZGravity(Entity<ClassicZPhysicsComponent?> ent, float newGravityMultiplier)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.GravityMultiplier = newGravityMultiplier;
        DirtyField(ent, ent.Comp, nameof(ClassicZPhysicsComponent.GravityMultiplier));
        WakeBody(ent);
    }

    /// <summary>
    /// Sets the vertical velocity for the entity. Positive values make the entity fly upward. Negative values make it fly downward.
    /// </summary>
    [PublicAPI]
    public void SetZVelocity(Entity<ClassicZPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.Velocity = newVelocity;
        DirtyField(ent, ent.Comp, nameof(ClassicZPhysicsComponent.Velocity));
        WakeBody(ent);
    }

    /// <summary>
    /// Add the vertical velocity for the entity. Positive values make the entity fly upward. Negative values make it fly downward.
    /// </summary>
    [PublicAPI]
    public void AddZVelocity(Entity<ClassicZPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        ent.Comp.Velocity += newVelocity;
        DirtyField(ent, ent.Comp, nameof(ClassicZPhysicsComponent.Velocity));
        WakeBody(ent);
    }

    [PublicAPI]
    public bool TryMove(EntityUid ent, int offset, Entity<ClassicZMapComponent?>? map = null)
    {
        return TryMove(ent, offset, out _, map);
    }

    private bool TryMove(EntityUid ent, int offset, out bool blocked, Entity<ClassicZMapComponent?>? map = null)
    {
        blocked = false;
        map ??= Transform(ent).MapUid;

        if (map is null)
            return false;

        if (!TryMapOffset(map.Value, offset, out var targetMap))
            return false;

        if (!_mapQuery.TryComp(targetMap, out var targetMapComp))
            return false;

        var attempt = new ClassicZLevelMapMoveAttemptEvent(targetMap, offset);
        RaiseLocalEvent(ent, ref attempt);
        if (attempt.Cancelled)
        {
            blocked = attempt.Blocked;
            if (blocked)
                RaiseMapMoveBlocked(ent, offset);
            return false;
        }

        var worldRot = _transform.GetWorldRotation(ent);

        var beforeEv = new ClassicZLevelBeforeMapMoveEvent(offset, targetMap.Comp.Depth);
        RaiseLocalEvent(ent, ref beforeEv);

        _transform.SetMapCoordinates(ent, new MapCoordinates(_transform.GetWorldPosition(ent), targetMapComp.MapId));
        _transform.SetWorldRotation(ent, worldRot);

        var ev = new ClassicZLevelMapMoveEvent(offset, targetMap.Comp.Depth);
        RaiseLocalEvent(ent, ref ev);

        return true;
    }

    private void RaiseMapMoveBlocked(EntityUid uid, int offset)
    {
        var ev = new ClassicZLevelMapMoveBlockedEvent(offset);
        RaiseLocalEvent(uid, ref ev);
    }

    [PublicAPI]
    public bool TryMoveUp(EntityUid ent) => TryMove(ent, 1);

    [PublicAPI]
    public bool TryMoveDown(EntityUid ent)
    {
        return TryMove(ent, -1);
    }

    [PublicAPI]
    public bool TryMoveDownOrChasm(EntityUid ent)
    {
        return TryMoveDownOrChasm(ent, out _);
    }

    private bool TryMoveDownOrChasm(EntityUid ent, out bool blocked)
    {
        blocked = false;
        // A rejected transition is an obstruction, not the edge of the map network.
        if (Transform(ent).MapUid is { } map && TryMapDown(map, out _))
            return TryMove(ent, -1, out blocked);

        //welp, that default Chasm behavior. Not really good, but ok for now.
        if (HasComp<ChasmFallingComponent>(ent))
            return false; //Already falling

        var attempt = new ClassicZLevelChasmAttempt(ent);
        RaiseLocalEvent(ent, attempt);

        if (attempt.Cancelled)
            return false;

        var audio = new SoundPathSpecifier("/Audio/Effects/falling.ogg");
        _audio.PlayPredicted(audio, Transform(ent).Coordinates, ent);
        var falling = AddComp<ChasmFallingComponent>(ent);
        falling.NextDeletionTime = _timing.CurTime + falling.DeletionTime;
        _blocker.UpdateCanMove(ent);

        return false;
    }

    private void UpdateDirtyMovement()
    {
        for (var i = _dirtyMovementBodies.Count - 1; i >= 0; i--)
        {
            var uid = _dirtyMovementBodies[i];

            if (!ZPhysicsQuery.TryComp(uid, out var component))
                continue;

            var entity = (uid, component);
            RequestCacheMovement(entity);
            RefreshBody(entity);
        }

        _dirtyMovementBodies.Clear();
        _dirtyMovementBodySet.Clear();
    }
}

/// <summary>
/// Is called on an entity right before it moves between z-levels.
/// </summary>
/// <param name="offset">How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.</param>
[ByRefEvent]
public struct ClassicZLevelBeforeMapMoveEvent(int offset, int level)
{
    /// <summary>
    /// How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.
    /// </summary>
    public int Offset = offset;

    public int CurrentZLevel = level;
}

/// <summary>
/// Is called on an entity when it moves between z-levels.
/// </summary>
/// <param name="offset">How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.</param>
[ByRefEvent]
public struct ClassicZLevelMapMoveEvent(int offset, int level)
{
    /// <summary>
    /// How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.
    /// </summary>
    public int Offset = offset;

    public int CurrentZLevel = level;
}

/// <summary>
/// Is triggered when an entity falls to the lower z-levels under the force of gravity
/// </summary>
[ByRefEvent]
public struct ClassicZLevelFallMapEvent;


/// <summary>
///Called upon the essence before attempting to fall into the abyss
/// </summary>
public sealed class ClassicZLevelChasmAttempt(EntityUid falled) : CancellableEntityEventArgs, IInventoryRelayEvent
{
    public EntityUid Falled = falled;
    public SlotFlags TargetSlots => SlotFlags.All;
}

/// <summary>
/// It is called on an entity when it hits the floor or ceiling with force.
/// </summary>
/// <param name="impactPower">The speed at the moment of impact. Always positive</param>
[ByRefEvent]
public struct ClassicZLevelHitEvent(float impactPower)
{
    /// <summary>
    /// The speed at the moment of impact. Always positive
    /// </summary>
    public float ImpactPower = impactPower;
}

/// <summary>
/// Is called every frame to calculate the current vertical velocity of the active zphysics entities.
/// </summary>
[ByRefEvent]
public struct ClassicGetZVelocityEvent(Entity<ClassicZPhysicsComponent> target, float frameTime)
{
    /// <summary>A specialized controller has supplied the acceleration for this step.</summary>
    public bool Handled;
    public Entity<ClassicZPhysicsComponent> Target = target;
    public float FrameTime = frameTime;
    public float VelocityDelta = 0;
}

/// <summary>Allows movement into an obstructed destination to be rejected before map-move side effects run.</summary>
[ByRefEvent]
public struct ClassicZLevelMapMoveAttemptEvent(Entity<ClassicZMapComponent> destination, int offset)
{
    public Entity<ClassicZMapComponent> Destination = destination;
    public int Offset = offset;
    public bool Cancelled;

    /// <summary>The transition was cancelled by a physical obstruction and should produce an impact.</summary>
    public bool Blocked;
}

/// <summary>Raised when a floor, ceiling or destination fixture prevents a map transition.</summary>
[ByRefEvent]
public readonly record struct ClassicZLevelMapMoveBlockedEvent(int Offset);

/// <summary>
/// Called when UpdateGravityState is used to update the current strength of the active z-level gravity. Various systems can subscribe to this to disable gravity.
/// </summary>
public sealed class ClassicCheckGravityEvent : EntityEventArgs
{
    public float Gravity = 1f;
}
