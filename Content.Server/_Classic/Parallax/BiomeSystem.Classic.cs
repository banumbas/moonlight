// Namespace does not match folder structure
#pragma warning disable IDE0130
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Stopwatch = System.Diagnostics.Stopwatch;
using Content.Server._Classic.Geyser;
using Content.Server._Classic.Station;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using Robust.Shared.Timing;
using Content.Server._Classic.ZLevels.Core;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Shared._Classic.Vehicles;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Damage.Components;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.NPC.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Sprite;
using Content.Server.Body.Components;
using Content.Shared.Body.Components;
using Content.Shared.Temperature.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Rotting;
using Robust.Shared.Prototypes;
using Robust.Shared.Profiling;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Spawners;
using ChunkIndicesEnumerator = Robust.Shared.Map.Enumerators.ChunkIndicesEnumerator;

namespace Content.Server.Parallax;

/// <summary>
/// Classic-specific biome streaming fast paths.
/// </summary>
public sealed partial class BiomeSystem
{
    [Dependency] private EntityQuery<ClassicUndergroundBiomeComponent> _classicUndergroundQuery = default!;
    [Dependency] private EntityQuery<ClassicBiomeAlwaysUnloadComponent> _classicAlwaysUnloadQuery = default!;
    [Dependency] private EntityQuery<ClassicGeyserGeneratorComponent> _classicGeyserGeneratorQuery = default!;
    [Dependency] private EntityQuery<ClassicGeyserOutletComponent> _classicGeyserOutletQuery = default!;
    [Dependency] private EntityQuery<ClassicBiomeStreamingComponent> _classicStreamingQuery = default!;
    [Dependency] private IGameTiming _classicTiming = default!;
    [Dependency] private ISerializationManager _classicSerialization = default!;
    [Dependency] private EntityQuery<ClassicZMapComponent> _classicZMapQuery = default!;
    [Dependency] private EntityQuery<ClassicZPhysicsComponent> _classicZPhysicsQuery = default!;
    [Dependency] private EntityQuery<ClassicZLevelHighGroundComponent> _classicHighGroundQuery = default!;
    [Dependency] private ClassicZLevelsSystem _classicZLevels = default!;
    [Dependency] private ClassicVehicleSystem _classicVehicles = default!;
    [Dependency] private EntityQuery<DamageableComponent> _classicDamageableQuery = default!;
    [Dependency] private ProfManager _classicProfiler = default!;

    private readonly HashSet<EntityUid> _classicBiomeTileWrites = new();
    private readonly Dictionary<(EntityPrototype Prototype, int Depth), ClassicZPhysicsComponent> _classicZPhysicsDefaults = new();
    private readonly List<(EntityUid GridUid, BiomeComponent Biome, MapGridComponent Grid, ClassicBiomeStreamingComponent Streaming)> _classicStreamingBiomes = new();
    private readonly Dictionary<Vector2i, ulong> _classicViewerCellMasks = new();
    private readonly List<Vector2i> _classicViewerCellChunks = new();
    private readonly List<Vector2i> _classicStaleUnloadChunks = new();
    private readonly HashSet<Vector2i> _classicRearmedUnloadChunks = new();
    private readonly ClassicZLevelOpeningCache _classicOpeningCache = new();
    private readonly List<Entity<MapGridComponent>> _classicOpeningGrids = new();
    private readonly List<Box2> _classicOpeningBounds = new();
    private readonly List<ClassicBiomeOpeningScanKey> _classicStaleOpeningScans = new();
    private readonly HashSet<Vector2i> _classicZCacheBatch = new();
    private readonly HashSet<EntityUid> _classicHandledVehicleAnchors = new();
    private readonly List<MapCoordinates> _classicVehicleExitCoordinates = new();
    private readonly List<(ICommonSession Session, EntityUid Viewer, TransformComponent AttachedXform)> _classicViewRequests = new();
    private readonly Dictionary<EntityUid, TimeSpan> _classicPendingOpeningCacheRefreshes = new();
    private readonly List<EntityUid> _classicStalePendingOpeningCacheViewers = new();
    private readonly HashSet<EntityUid> _classicForegroundStreamingBiomes = new();
    private EntityQuery<EyeComponent> _eyeQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private readonly Tile?[] _chunkBiomeTiles = new Tile?[ChunkSize * ChunkSize];
    private readonly List<Vector2i> _unloadChunks = new();
    private readonly List<(Vector2i, Tile)> _unloadTiles = new(ChunkSize * ChunkSize);
    private readonly List<(EntityUid Entity, Vector2i Tile)> _classicUnloadEntities = new(ChunkSize * ChunkSize);
    private readonly List<Vector2i> _classicRestoreCells = new(ChunkSize * ChunkSize);
    private readonly List<string> _classicMarkerLayers = new();
    private readonly List<(string Layer, Vector2i Chunk)> _classicMarkerAreas = new();
    private float _classicLoadRange = DefaultLoadRange;
    private const int MaxClassicBackgroundChunksPerTick = 64;
    private const int ClassicChunkCells = ChunkSize * ChunkSize;
    private const int MaxClassicBackgroundEntitySpawnsPerTick = ClassicChunkCells;
    private const int MaxClassicUnloadEntitiesPerTick = ClassicChunkCells * 16;
    private const int MaxClassicForcedUnloadOperationsPerTick = 8;
    private const int MaxClassicPressureUnloadScan = ClassicChunkCells;
    private const int MaxClassicZLevelCachedChunks = ClassicChunkCells * 4;
    private const int MaxClassicOpeningSnapshotChunks = ClassicChunkCells * 4;
    private long _classicStreamingWorkStarted;
    private TimeSpan _classicStreamingWorkBudget;
    private bool _classicStreamingBudgetConfigured;
    private int _classicBackgroundEntitySpawns;
    private int _classicBackgroundEntitySpawnLimit;
    private int _classicUnloadEntitiesProcessed;
    private int _classicUnloadEntityLimit;
    private int _classicStreamingStartIndex;
    private int _classicViewRequestStartIndex;
    private bool _classicForcedProgressUsed;
    private bool _classicOpeningForcedProgressUsed;
    private int _classicForcedUnloadOperations;
    private bool _classicUnloadFirst;
    private TimeSpan _classicNextPendingOpeningCachePrune;
    private readonly Dictionary<EntityUid, int> _classicBackgroundEntitySpawnsByGrid = new();
    private readonly List<EntityUid> _classicBiomeSpawnedEntities = new();
    private readonly List<EntityUid> _classicMarkerTraversal = new();
    private readonly HashSet<EntityUid> _classicMarkerChildren = new();
    private readonly HashSet<string> _classicMarkerFactions = new();
    private readonly HashSet<string> _classicMarkerTags = new();
    private readonly HashSet<string> _classicMarkerSolutionContainers = new();
    private bool _classicBiomeSpawnTracking;

    private void BeginClassicStreamingBudget()
    {
        if (_classicTiming.CurTime >= _classicNextPendingOpeningCachePrune)
        {
            _classicNextPendingOpeningCachePrune = _classicTiming.CurTime + TimeSpan.FromSeconds(10);
            _classicStalePendingOpeningCacheViewers.Clear();
            foreach (var (viewer, refresh) in _classicPendingOpeningCacheRefreshes)
            {
                if (refresh <= _classicTiming.CurTime || !EntityManager.EntityExists(viewer))
                    _classicStalePendingOpeningCacheViewers.Add(viewer);
            }

            foreach (var viewer in _classicStalePendingOpeningCacheViewers)
                _classicPendingOpeningCacheRefreshes.Remove(viewer);
        }

        _classicStreamingWorkBudget = TimeSpan.Zero;
        _classicStreamingBudgetConfigured = false;
        _classicBackgroundEntitySpawns = 0;
        _classicBackgroundEntitySpawnsByGrid.Clear();
        _classicBackgroundEntitySpawnLimit = 1;
        _classicUnloadEntitiesProcessed = 0;
        _classicUnloadEntityLimit = 1;
        _classicStreamingWorkStarted = Stopwatch.GetTimestamp();
        _classicForcedProgressUsed = false;
        _classicOpeningForcedProgressUsed = false;
        _classicForcedUnloadOperations = 0;
    }

    private void IncludeClassicStreamingBudget(ClassicBiomeStreamingComponent streaming)
    {
        var budget = streaming.WorkBudget < TimeSpan.Zero ? TimeSpan.Zero : streaming.WorkBudget;
        if (_classicStreamingBudgetConfigured && budget >= _classicStreamingWorkBudget)
            return;

        _classicStreamingWorkBudget = budget;
        _classicStreamingBudgetConfigured = true;
    }

    private void PrecollectClassicViewerStreamingBudgets()
    {
        foreach (var pSession in Filter.GetAllPlayers(_playerManager))
        {
            if (pSession.Status != SessionStatus.InGame ||
                !_xformQuery.TryGetComponent(pSession.AttachedEntity, out var attachedXform) ||
                !CanLoad(pSession.AttachedEntity.Value))
            {
                continue;
            }

            var attachedBodyPos = _transform.GetWorldPosition(attachedXform);
            if (TryGetActiveBiome(
                    attachedXform,
                    attachedBodyPos,
                    out var attachedGridUid,
                    out var attachedBiome,
                    out _) &&
                attachedBiome.Enabled &&
                _classicStreamingQuery.TryComp(attachedGridUid, out var attachedStreaming))
            {
                IncludeClassicStreamingBudget(attachedStreaming);
            }

            foreach (var viewer in pSession.ViewSubscriptions)
            {
                if (!_xformQuery.TryGetComponent(viewer, out var viewerXform) || !CanLoad(viewer))
                    continue;

                var worldPos = ClassicBiomeViewerPosition(pSession.AttachedEntity, viewer, viewerXform);
                if (!TryGetActiveBiome(
                        viewerXform,
                        worldPos,
                        out var viewerGridUid,
                        out var viewerBiome,
                        out _) ||
                    !viewerBiome.Enabled)
                {
                    continue;
                }

                if (_classicStreamingQuery.TryComp(viewerGridUid, out var viewerStreaming))
                    IncludeClassicStreamingBudget(viewerStreaming);
                PrecollectClassicOpeningStreamingBudgets(
                    pSession.AttachedEntity,
                    viewer,
                    attachedXform,
                    viewerXform,
                    worldPos);
            }
        }
    }

    private void PrecollectClassicOpeningStreamingBudgets(
        EntityUid? attached,
        EntityUid viewer,
        TransformComponent attachedXform,
        TransformComponent viewerXform,
        Vector2 worldPos)
    {
        if (!_classicZLevels.IsViewerEye(attached, viewer) ||
            attachedXform.MapUid is not { } attachedMap ||
            viewerXform.MapUid is not { } viewerMap ||
            !_classicZMapQuery.TryComp(attachedMap, out var attachedZ) ||
            !_classicZMapQuery.TryComp(viewerMap, out var viewerZ) ||
            viewerZ.Depth >= attachedZ.Depth)
        {
            return;
        }

        var levelsToCheck = attachedZ.Depth - viewerZ.Depth;
        for (var offset = 0; offset < levelsToCheck; offset++)
        {
            EntityUid openingMap;
            if (offset == 0)
            {
                openingMap = attachedMap;
            }
            else if (!_classicZLevels.TryMapOffset((attachedMap, attachedZ), -offset, out var intermediate))
            {
                return;
            }
            else
            {
                openingMap = intermediate.Owner;
            }

            if (TryGetClassicMapBiome(
                    openingMap,
                    worldPos,
                    out var openingGridUid,
                    out _,
                    out _) &&
                _classicStreamingQuery.TryComp(openingGridUid, out var openingStreaming))
            {
                IncludeClassicStreamingBudget(openingStreaming);
            }
        }
    }

    private void PruneClassicOpeningScans(ClassicBiomeStreamingComponent streaming)
    {
        _classicStaleOpeningScans.Clear();
        foreach (var (key, scan) in streaming.OpeningScans)
        {
            if (scan.LastRequestGeneration != streaming.OpeningScanGeneration &&
                _classicTiming.CurTime - scan.LastRequestTime >= streaming.OpeningCacheDuration)
            {
                _classicStaleOpeningScans.Add(key);
            }
        }

        foreach (var key in _classicStaleOpeningScans)
            streaming.OpeningScans.Remove(key);

        var maxEntries = Math.Clamp(streaming.MaxOpeningCacheEntries, 1, 128);
        var excess = streaming.OpeningScans.Count - maxEntries;
        if (excess > 0)
        {
            _classicStaleOpeningScans.Clear();
            _classicStaleOpeningScans.AddRange(streaming.OpeningScans.Keys);
            _classicStaleOpeningScans.Sort((left, right) =>
            {
                var leftScan = streaming.OpeningScans[left];
                var rightScan = streaming.OpeningScans[right];
                var leftCurrent = leftScan.LastRequestGeneration == streaming.OpeningScanGeneration;
                var rightCurrent = rightScan.LastRequestGeneration == streaming.OpeningScanGeneration;
                var comparison = leftCurrent.CompareTo(rightCurrent);
                if (comparison != 0)
                    return comparison;

                var leftComplete = leftScan.Phase == ClassicBiomeOpeningScanPhase.Complete;
                var rightComplete = rightScan.Phase == ClassicBiomeOpeningScanPhase.Complete;
                comparison = rightComplete.CompareTo(leftComplete);
                if (comparison != 0)
                    return comparison;

                comparison = leftScan.Phase.CompareTo(rightScan.Phase);
                if (comparison != 0)
                    return comparison;

                comparison = leftScan.OpeningChunkMasks.Count.CompareTo(rightScan.OpeningChunkMasks.Count);
                if (comparison != 0)
                    return comparison;

                comparison = leftScan.LastRequestTime.CompareTo(rightScan.LastRequestTime);
                return comparison != 0 ? comparison : CompareClassicOpeningScanKeys(left, right);
            });

            for (var i = 0; i < excess; i++)
                streaming.OpeningScans.Remove(_classicStaleOpeningScans[i]);
        }

        TrimClassicDictionary(streaming.OpeningScans, maxEntries);
    }

    private bool TryAdmitClassicOpeningScan(
        ClassicBiomeStreamingComponent streaming,
        ClassicBiomeOpeningScanKey key,
        ClassicBiomeOpeningScanState scan)
    {
        var maxEntries = Math.Clamp(streaming.MaxOpeningCacheEntries, 1, 128);
        while (streaming.OpeningScans.Count >= maxEntries)
        {
            if (!TryGetClassicOpeningAdmissionCandidate(streaming, out var candidate))
                return false;

            streaming.OpeningScans.Remove(candidate);
        }

        streaming.OpeningScans.Add(key, scan);
        return true;
    }

    private static bool TryGetClassicOpeningAdmissionCandidate(
        ClassicBiomeStreamingComponent streaming,
        out ClassicBiomeOpeningScanKey candidate)
    {
        candidate = default;
        var found = false;
        var foundRank = int.MaxValue;
        var previousGeneration = unchecked(streaming.OpeningScanGeneration - 1);
        if (previousGeneration == 0)
            previousGeneration--;

        foreach (var (key, scan) in streaming.OpeningScans)
        {
            var stale = scan.LastRequestGeneration != streaming.OpeningScanGeneration &&
                        scan.LastRequestGeneration != previousGeneration;
            var served = scan.LastRequestGeneration == streaming.OpeningScanGeneration &&
                         scan.Phase == ClassicBiomeOpeningScanPhase.Complete;
            if (!stale && !served)
                continue;

            var rank = stale ? 0 : 1;
            if (found &&
                (rank > foundRank ||
                 rank == foundRank && CompareClassicOpeningScanKeys(key, candidate) >= 0))
            {
                continue;
            }

            found = true;
            foundRank = rank;
            candidate = key;
        }

        return found;
    }

    private static int CompareClassicOpeningScanKeys(
        ClassicBiomeOpeningScanKey left,
        ClassicBiomeOpeningScanKey right)
    {
        var comparison = CompareClassicChunks(left.RequiredFirstChunk, right.RequiredFirstChunk);
        if (comparison != 0)
            return comparison;

        comparison = CompareClassicChunks(left.RequiredLastChunk, right.RequiredLastChunk);
        if (comparison != 0)
            return comparison;

        comparison = CompareClassicChunks(left.FirstTile, right.FirstTile);
        return comparison != 0 ? comparison : CompareClassicChunks(left.LastTile, right.LastTile);
    }

    private void InvalidateClassicOpeningScansForChunk(
        ClassicBiomeStreamingComponent streaming,
        Vector2i chunk)
    {
        if (streaming.OpeningScans.Count == 0)
            return;

        var chunkLast = chunk + new Vector2i(ChunkSize - 1, ChunkSize - 1);
        _classicStaleOpeningScans.Clear();
        foreach (var (key, scan) in streaming.OpeningScans)
        {
            if (chunk.X <= scan.LastTile.X &&
                chunkLast.X >= scan.FirstTile.X &&
                chunk.Y <= scan.LastTile.Y &&
                chunkLast.Y >= scan.FirstTile.Y)
            {
                _classicStaleOpeningScans.Add(key);
            }
        }

        foreach (var key in _classicStaleOpeningScans)
            streaming.OpeningScans.Remove(key);
    }

    private void InitializeClassicBiome()
    {
        _eyeQuery = GetEntityQuery<EyeComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        Subs.CVar(_configManager, CVars.NetMaxUpdateRange, SetClassicLoadRange, true);
        Subs.CVar(_configManager, CVars.NetPvsPriorityRange, SetClassicLoadRange, true);
        UpdatesBefore.Add(typeof(ClassicZLevelsSystem));
        UpdatesBefore.Add(typeof(Robust.Server.GameObjects.PhysicsSystem));
        SubscribeLocalEvent<TileChangedEvent>(OnClassicTileChanged);
        SubscribeLocalEvent<GridRemovalEvent>(OnClassicOpeningGridRemoved);
        SubscribeLocalEvent<ClassicVehiclePrepareExitEvent>(OnClassicVehiclePrepareExit);
        SubscribeLocalEvent<ClassicBiomeAmbientMarkerComponent, PullStartedMessage>(OnClassicAmbientMarkerTouched);
        SubscribeLocalEvent<ClassicBiomeAmbientMarkerComponent, InteractHandEvent>(OnClassicAmbientMarkerTouched);
        SubscribeLocalEvent<ClassicBiomeAmbientMarkerComponent, InteractUsingEvent>(OnClassicAmbientMarkerTouched);
        EntityManager.EntityAdded += OnClassicBiomeEntityAdded;
    }

    public override void Shutdown()
    {
        EntityManager.EntityAdded -= OnClassicBiomeEntityAdded;
        base.Shutdown();
    }

    private void OnClassicBiomeEntityAdded(Entity<MetaDataComponent> entity)
    {
        if (_classicBiomeSpawnTracking)
            _classicBiomeSpawnedEntities.Add(entity);
    }

    private void OnClassicAmbientMarkerTouched(
        Entity<ClassicBiomeAmbientMarkerComponent> entity,
        ref PullStartedMessage args)
    {
        entity.Comp.PlayerTouched = true;
    }

    private void OnClassicAmbientMarkerTouched(
        Entity<ClassicBiomeAmbientMarkerComponent> entity,
        ref InteractHandEvent args)
    {
        entity.Comp.PlayerTouched = true;
    }

    private void OnClassicAmbientMarkerTouched(
        Entity<ClassicBiomeAmbientMarkerComponent> entity,
        ref InteractUsingEvent args)
    {
        entity.Comp.PlayerTouched = true;
    }

    private static bool ClassicMarkerUncomparableComponent(string name)
    {
        return name is "NpcFactionMember" or "MobState" or "Tag" or "Body" or "Internals" or
            "Actions" or "CustomLimbVisualizer" or "HealthExaminable" or "MovedByPressure" or
            "SolutionContainerManager";
    }

    private void CollectClassicMarkerChildren(EntityUid root, HashSet<EntityUid> children)
    {
        children.Clear();
        _classicMarkerTraversal.Clear();
        _classicMarkerTraversal.Add(root);
        for (var i = 0; i < _classicMarkerTraversal.Count; i++)
        {
            if (!_xformQuery.TryGetComponent(_classicMarkerTraversal[i], out var xform))
                continue;

            using var enumerator = xform.ChildEnumerator;
            while (enumerator.MoveNext(out var child))
            {
                if (children.Add(child))
                    _classicMarkerTraversal.Add(child);
            }
        }

        _classicMarkerTraversal.Clear();
    }

    private void CaptureClassicMarkerFactions(EntityUid root, HashSet<string> factions)
    {
        factions.Clear();
        if (!TryComp<NpcFactionMemberComponent>(root, out var member))
            return;

        foreach (var faction in member.Factions)
            factions.Add("member:" + faction);
        if (member.AddFriendlyFactions != null)
        {
            foreach (var faction in member.AddFriendlyFactions)
                factions.Add("friendly:" + faction);
        }

        if (member.AddHostileFactions != null)
        {
            foreach (var faction in member.AddHostileFactions)
                factions.Add("hostile:" + faction);
        }
    }

    private void CaptureClassicMarkerTags(EntityUid root, HashSet<string> tags)
    {
        tags.Clear();
        if (!TryComp<TagComponent>(root, out var component))
            return;

        foreach (var tag in component.Tags)
            tags.Add(tag.ToString());
    }

    private Dictionary<EntityUid, Vector2i>? SpawnClassicBiomeEntity(
        string prototype,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        Dictionary<EntityUid, Vector2i>? loaded)
    {
        _classicBiomeSpawnedEntities.Clear();
        _classicBiomeSpawnTracking = true;
        EntityUid root;
        try
        {
            root = Spawn(prototype, _mapSystem.GridTileToLocal(gridUid, grid, tile));
        }
        catch
        {
            _classicBiomeSpawnedEntities.Clear();
            throw;
        }
        finally
        {
            _classicBiomeSpawnTracking = false;
        }

        if (!Deleted(root) && !EntityManager.IsQueuedForDeletion(root) &&
            _xformQuery.TryGetComponent(root, out var rootXform) &&
            rootXform.ParentUid == gridUid && !rootXform.Anchored)
        {
            _transform.AnchorEntity((root, rootXform), (gridUid, grid), tile);
        }

        foreach (var spawned in _classicBiomeSpawnedEntities)
        {
            if (Deleted(spawned) || EntityManager.IsQueuedForDeletion(spawned) ||
                !_xformQuery.TryGetComponent(spawned, out var xform) || xform.ParentUid != gridUid)
            {
                continue;
            }

            loaded ??= new Dictionary<EntityUid, Vector2i>(ChunkSize * ChunkSize);
            loaded.TryAdd(spawned, tile);
        }

        _classicBiomeSpawnedEntities.Clear();
        return loaded;
    }

    private Dictionary<EntityUid, Vector2i>? SpawnClassicBiomeMarkerEntity(
        string prototype,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        Dictionary<EntityUid, Vector2i>? loaded,
        ClassicBiomeMarkerCellState marker)
    {
        _classicBiomeSpawnedEntities.Clear();
        _classicBiomeSpawnTracking = true;
        EntityUid root;
        try
        {
            root = EntityManager.CreateEntityUninitialized(
                prototype,
                _mapSystem.GridTileToLocal(gridUid, grid, tile));
            RemComp<GhostTakeoverAvailableComponent>(root);
            RemComp<GhostRoleComponent>(root);
            EntityManager.InitializeAndStartEntity(root);
        }
        catch
        {
            _classicBiomeSpawnedEntities.Clear();
            throw;
        }
        finally
        {
            _classicBiomeSpawnTracking = false;
        }

        marker.Entities.Clear();
        marker.Root = root;
        foreach (var spawned in _classicBiomeSpawnedEntities)
        {
            if (Deleted(spawned) || EntityManager.IsQueuedForDeletion(spawned) ||
                !_xformQuery.TryGetComponent(spawned, out var xform) || xform.ParentUid != gridUid)
            {
                continue;
            }

            loaded ??= new Dictionary<EntityUid, Vector2i>(ChunkSize * ChunkSize);
            loaded.TryAdd(spawned, tile);
            marker.Entities.Add(spawned);
        }

        if (!Deleted(root) && !EntityManager.IsQueuedForDeletion(root) &&
            _xformQuery.TryGetComponent(root, out var markerXform) &&
            markerXform.ParentUid == gridUid && HasComp<HTNComponent>(root))
        {
            EnsureComp<ClassicBiomeAmbientMarkerComponent>(root);
            var metadata = MetaData(root);
            marker.BaselineName = metadata.EntityName;
            marker.BaselineDescription = metadata.EntityDescription;
            var baseline = new Dictionary<string, IComponent>();
            var componentNames = new HashSet<string>();
            foreach (var component in AllComps(root))
            {
                if (component is MetaDataComponent or TransformComponent or ClassicBiomeAmbientMarkerComponent)
                    continue;

                var name = EntityManager.ComponentFactory.GetRegistration(component.GetType()).Name;
                if (name == "ActiveNPC")
                    continue;
                componentNames.Add(name);
                if (component is HTNComponent or PhysicsComponent || ClassicMarkerUncomparableComponent(name))
                    continue;
                baseline[name] = _classicSerialization.CreateCopy(component, notNullableOverride: true);
            }

            marker.BaselineComponents = baseline;
            marker.BaselineComponentNames = componentNames;
            var children = new HashSet<EntityUid>();
            CollectClassicMarkerChildren(root, children);
            marker.BaselineChildren = children;
            CaptureClassicMarkerFactions(root, _classicMarkerFactions);
            marker.BaselineFactions = new HashSet<string>(_classicMarkerFactions);
            CaptureClassicMarkerTags(root, _classicMarkerTags);
            marker.BaselineTags = new HashSet<string>(_classicMarkerTags);
            marker.BaselineSolutionContainers = TryComp<SolutionContainerManagerComponent>(root, out var solutions)
                ? new HashSet<string>(solutions.Containers)
                : new HashSet<string>();
        }

        _classicBiomeSpawnedEntities.Clear();
        return loaded;
    }

    private void SetClassicLoadRange(float _)
    {
        _classicLoadRange = Math.Max(
            16f,
            Math.Max(
                _configManager.GetCVar(CVars.NetMaxUpdateRange),
                _configManager.GetCVar(CVars.NetPvsPriorityRange))) / 2f;
    }

    private void ClearClassicPrototypeCaches()
    {
        _classicOpeningCache.Clear();
    }

    private void OnClassicOpeningGridRemoved(GridRemovalEvent args)
    {
        _classicOpeningCache.RemoveGrid(args.EntityUid);
    }

    private void OnClassicVehiclePrepareExit(ClassicVehiclePrepareExitEvent args)
    {
        if (!_mapSystem.TryGetMap(args.Coordinates.MapId, out var mapUid))
            return;

        MaterializeClassicVehicleExitCell(mapUid.Value, args.Coordinates.Position);
        if (_classicZMapQuery.TryComp(mapUid, out var zMap) &&
            _classicZLevels.TryMapDown((mapUid.Value, zMap), out var below))
        {
            MaterializeClassicVehicleExitCell(below.Owner, args.Coordinates.Position);
        }
    }

    private void MaterializeClassicVehicleExitCell(EntityUid mapUid, Vector2 worldPos)
    {
        if (!TryComp<MapComponent>(mapUid, out var map))
            return;

        if (TryMaterialize(mapUid))
            return;

        if (_mapSystem.TryFindGridAt(mapUid, worldPos, out var containingGridUid, out _) &&
            TryMaterialize(containingGridUid))
        {
            return;
        }

        foreach (var candidate in _mapSystem.GetAllGrids(map.MapId))
        {
            if (TryMaterialize(candidate.Owner))
                return;
        }

        bool TryMaterialize(EntityUid gridUid)
        {
            if (!_biomeQuery.TryComp(gridUid, out var biome) ||
                !biome.Enabled ||
                !_classicStreamingQuery.TryComp(gridUid, out var streaming) ||
                !TryComp<MapGridComponent>(gridUid, out var grid))
            {
                return false;
            }

            var tile = _mapSystem.TileIndicesFor(
                gridUid,
                grid,
                new MapCoordinates(worldPos, map.MapId));
            MaterializeClassicBiomeCell(biome, gridUid, grid, streaming, tile);
            return true;
        }
    }

    private void MaterializeClassicBiomeCell(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        ClassicBiomeStreamingComponent streaming,
        Vector2i tile)
    {
        var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
        var local = tile - chunk;
        var cellIndex = local.X * ChunkSize + local.Y;
        var bit = 1UL << cellIndex;

        streaming.PendingUnloads.Remove(chunk);
        streaming.CachedZChunks.Remove(chunk);
        streaming.CachedAdjacentPrefetchChunks.Remove(chunk);

        if (streaming.PartialUnloads.TryGetValue(chunk, out var unloading))
        {
            unloading.Phase = ClassicBiomePartialUnloadPhase.Restoring;
            RestoreClassicUnload(
                biome,
                gridUid,
                grid,
                chunk,
                biome.Seed,
                streaming,
                unloading,
                1,
                tile,
                false);

            if (streaming.PartialUnloads.ContainsKey(chunk))
            {
                if (unloading.WasPartial &&
                    unloading.PartialCursor <= cellIndex &&
                    (unloading.PriorityCells & bit) == 0)
                {
                    LoadChunkCells(biome, gridUid, grid, chunk, biome.Seed, cellIndex, 1);
                    unloading.PriorityCells |= bit;
                    unloading.MaterializedCells |= bit;
                }

                RearmUnload();
                return;
            }
        }

        if (biome.LoadedChunks.Contains(chunk))
        {
            RearmUnload();
            return;
        }

        streaming.PartialLoads.TryAdd(chunk, 0);
        var generated = streaming.PriorityLoadedCells.GetValueOrDefault(chunk);
        if (streaming.PartialLoads[chunk] > cellIndex || (generated & bit) != 0)
        {
            RearmUnload();
            return;
        }

        LoadChunkCells(biome, gridUid, grid, chunk, biome.Seed, cellIndex, 1);
        streaming.PriorityLoadedCells[chunk] = generated | bit;
        streaming.MaterializedCells[chunk] = streaming.MaterializedCells.GetValueOrDefault(chunk) | bit;
        RearmUnload();

        void RearmUnload()
        {
            streaming.PendingUnloads[chunk] = GetClassicUnloadDeadline(streaming, chunk);
        }
    }

    private void LoadClassicViewerChunks(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        ClassicBiomeStreamingComponent streaming)
    {
        _classicViewerCellMasks.Clear();
        foreach (var cell in streaming.ViewerCells)
        {
            var chunk = SharedMapSystem.GetChunkIndices(cell, ChunkSize) * ChunkSize;
            var local = cell - chunk;
            var cellIndex = local.X * ChunkSize + local.Y;
            var bit = 1UL << cellIndex;

            if (streaming.PartialUnloads.TryGetValue(chunk, out var unloading))
            {
                unloading.Phase = ClassicBiomePartialUnloadPhase.Restoring;
                RestoreClassicUnload(
                    biome,
                    gridUid,
                    grid,
                    chunk,
                    seed,
                    streaming,
                    unloading,
                    1,
                    cell);

                if (streaming.PartialUnloads.ContainsKey(chunk))
                {
                    if (unloading.WasPartial &&
                        unloading.PartialCursor <= cellIndex &&
                        (unloading.PriorityCells & bit) == 0)
                    {
                        using (_classicProfiler.Value("ClassicBiome.UrgentLoad"))
                            LoadChunkCells(biome, gridUid, grid, chunk, seed, cellIndex, 1);
                        unloading.PriorityCells |= bit;
                        unloading.MaterializedCells |= bit;
                    }

                    continue;
                }
            }

            if (biome.LoadedChunks.Contains(chunk))
            {
                streaming.PartialLoads.Remove(chunk);
                streaming.PriorityLoadedCells.Remove(chunk);
                streaming.MaterializedCells.Remove(chunk);
                continue;
            }

            streaming.PartialLoads.TryAdd(chunk, 0);
            var generated = streaming.PriorityLoadedCells.GetValueOrDefault(chunk);
            if (streaming.PartialLoads[chunk] > cellIndex || (generated & bit) != 0)
                continue;

            _classicViewerCellMasks[chunk] = _classicViewerCellMasks.GetValueOrDefault(chunk) | bit;
        }

        _classicViewerCellChunks.Clear();
        _classicViewerCellChunks.AddRange(_classicViewerCellMasks.Keys);
        _classicViewerCellChunks.Sort(CompareClassicChunks);
        if (_classicViewerCellChunks.Count == 0)
            return;

        using (_classicProfiler.Value("ClassicBiome.UrgentBatch"))
        {
            foreach (var chunk in _classicViewerCellChunks)
            {
                var pending = _classicViewerCellMasks[chunk];
                var generated = streaming.PriorityLoadedCells.GetValueOrDefault(chunk);
                while (pending != 0)
                {
                    var start = BitOperations.TrailingZeroCount(pending);
                    var end = start + 1;
                    while (end < ClassicChunkCells && (pending & (1UL << end)) != 0)
                        end++;

                    using (_classicProfiler.Value("ClassicBiome.UrgentLoad"))
                        LoadChunkCells(biome, gridUid, grid, chunk, seed, start, end - start);

                    var length = end - start;
                    var run = length == ClassicChunkCells
                        ? ulong.MaxValue
                        : ((1UL << length) - 1UL) << start;
                    generated |= run;
                    pending &= ~run;
                }

                streaming.PriorityLoadedCells[chunk] = generated;
                streaming.MaterializedCells[chunk] =
                    streaming.MaterializedCells.GetValueOrDefault(chunk) | generated;
            }
        }
    }

    /// <summary>
    /// Advances a Classic chunk teardown by one bounded operation. Entity comparison and entity
    /// deletion are the expensive part and are sliced; decal/tile finalization is kept as its own
    /// operation so it cannot be added to the last entity slice in the same tick.
    /// </summary>
    private void UnloadClassicChunks(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        ClassicBiomeStreamingComponent streaming)
    {
        var active = _activeChunks[biome];
        _classicRearmedUnloadChunks.Clear();
        PruneClassicZLevelCache(streaming);
        var unloadEntityLimit = Math.Min(
            _classicUnloadEntityLimit,
            _classicUnloadEntitiesProcessed +
            Math.Clamp(streaming.UnloadEntitiesPerTick, 1, MaxClassicUnloadEntitiesPerTick));

        using (_classicProfiler.Value("ClassicBiome.UnloadDiscover"))
        {
            if (!streaming.UnloadTrackingInitialized)
            {
                foreach (var chunk in biome.LoadedChunks)
                {
                    if (!IsClassicChunkRetained(active, streaming, chunk))
                        streaming.PendingUnloads.TryAdd(chunk, GetClassicUnloadDeadline(streaming, chunk));
                }

                foreach (var chunk in streaming.PartialLoads.Keys)
                {
                    if (!IsClassicChunkRetained(active, streaming, chunk))
                        streaming.PendingUnloads.TryAdd(chunk, GetClassicUnloadDeadline(streaming, chunk));
                }

                streaming.UnloadTrackingInitialized = true;
            }
            else
            {
                foreach (var chunk in streaming.PreviousActiveChunks)
                {
                    if (IsClassicChunkRetained(active, streaming, chunk) ||
                        !biome.LoadedChunks.Contains(chunk) && !streaming.PartialLoads.ContainsKey(chunk))
                    {
                        continue;
                    }

                    streaming.PendingUnloads.TryAdd(chunk, GetClassicUnloadDeadline(streaming, chunk));
                }
            }

            foreach (var chunk in active)
            {
                streaming.PendingUnloads.Remove(chunk);
                streaming.CachedZChunks.Remove(chunk);
                streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
                if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                    partial.Phase = ClassicBiomePartialUnloadPhase.Restoring;
            }
            foreach (var chunk in streaming.LandingChunks)
            {
                streaming.PendingUnloads.Remove(chunk);
                streaming.CachedZChunks.Remove(chunk);
                streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
                if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                    partial.Phase = ClassicBiomePartialUnloadPhase.Restoring;
            }
            foreach (var chunk in streaming.AdjacentPrefetchChunks)
            {
                streaming.PendingUnloads.Remove(chunk);
                streaming.CachedZChunks.Remove(chunk);
                streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
                if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                    partial.Phase = ClassicBiomePartialUnloadPhase.Restoring;
            }
            CacheClassicDepartedAdjacentPrefetchChunks(biome, active, streaming);
            foreach (var (chunk, partial) in streaming.PartialUnloads)
            {
                if (partial.Phase == ClassicBiomePartialUnloadPhase.Restoring &&
                    !IsClassicChunkRetained(active, streaming, chunk) &&
                    !ClassicZCacheAlive(streaming, chunk))
                {
                    partial.Phase = ClassicBiomePartialUnloadPhase.Entities;
                }
            }

            streaming.PreviousActiveChunks.Clear();
            streaming.PreviousActiveChunks.UnionWith(active);
            streaming.PreviousActiveChunks.UnionWith(streaming.LandingChunks);
            streaming.PreviousActiveChunks.UnionWith(streaming.AdjacentPrefetchChunks);
            streaming.PreviousAdjacentPrefetchChunks.Clear();
            streaming.PreviousAdjacentPrefetchChunks.UnionWith(streaming.AdjacentPrefetchChunks);
        }

        var unloadPressure = ClassicBiomeHasUnloadPressure(streaming);

        var configuredOperations = Math.Clamp(
            streaming.BackgroundChunksPerTick,
            0,
            MaxClassicBackgroundChunksPerTick);
        var operations = unloadPressure
            ? Math.Max(MaxClassicForcedUnloadOperationsPerTick, configuredOperations)
            : Math.Max(1, configuredOperations);
        var completedChunks = 0;
        var completionLimit = Math.Max(1, configuredOperations);
        var forcedSelectionUsed = false;
        var selectedChunk = default(Vector2i);
        ClassicBiomePartialUnloadState? selectedState = null;

        for (var operation = 0; operation < operations; operation++)
        {
            if (unloadPressure && completedChunks >= completionLimit)
                break;

            var budgetExpired = ClassicStreamingBudgetExpired();
            var forcedLimit = unloadPressure ? MaxClassicForcedUnloadOperationsPerTick : 1;
            if (budgetExpired && _classicForcedUnloadOperations >= forcedLimit)
                break;

            if (selectedState == null)
            {
                if (budgetExpired)
                {
                    if (forcedSelectionUsed)
                        break;
                    forcedSelectionUsed = true;
                }

                if (!TryGetClassicPartialUnload(streaming, out selectedChunk, out selectedState) &&
                    !TryStartClassicUnload(
                        biome,
                        active,
                        streaming,
                        unloadPressure,
                        out selectedChunk,
                        out selectedState))
                {
                    break;
                }
            }

            if (budgetExpired)
                _classicForcedUnloadOperations++;

            var chunk = selectedChunk;
            var currentState = selectedState!;
            switch (currentState.Phase)
            {
                case ClassicBiomePartialUnloadPhase.Entities:
                    if (_classicUnloadEntitiesProcessed >= unloadEntityLimit)
                    {
                        TrimClassicUnloadDictionaries(streaming);
                        return;
                    }

                    using (_classicProfiler.Value("ClassicBiome.UnloadEntitySlice"))
                    {
                        var remaining = unloadEntityLimit - _classicUnloadEntitiesProcessed;
                        _classicUnloadEntitiesProcessed += UnloadClassicEntitySlice(
                            biome,
                            gridUid,
                            grid,
                            chunk,
                            streaming,
                            currentState,
                            Math.Min(
                                remaining,
                                Math.Clamp(streaming.UnloadEntitiesPerSlice, 1, ClassicChunkCells)),
                            stopWhenBudgetExpires: !budgetExpired);
                    }
                    break;
                case ClassicBiomePartialUnloadPhase.Finalize:
                    if (IsClassicChunkRetained(active, streaming, chunk))
                    {
                        currentState.Phase = ClassicBiomePartialUnloadPhase.Restoring;
                        goto case ClassicBiomePartialUnloadPhase.Restoring;
                    }

                    using (_classicProfiler.Value("ClassicBiome.UnloadFinalize"))
                    {
                        AdvanceClassicUnloadFinalize(
                            biome,
                            gridUid,
                            grid,
                            chunk,
                            seed,
                            currentState,
                            Math.Clamp(streaming.UnloadCellsPerSlice, 1, ClassicChunkCells),
                            stopWhenBudgetExpires: !budgetExpired);
                    }
                    break;
                case ClassicBiomePartialUnloadPhase.Commit:
                    if (IsClassicChunkRetained(active, streaming, chunk))
                    {
                        currentState.Phase = ClassicBiomePartialUnloadPhase.Restoring;
                        goto case ClassicBiomePartialUnloadPhase.Restoring;
                    }

                    using (_classicProfiler.Value("ClassicBiome.UnloadCommit"))
                    {
                        CommitClassicUnload(biome, gridUid, grid, chunk, streaming, currentState);
                    }
                    completedChunks++;
                    break;
                case ClassicBiomePartialUnloadPhase.Restoring:
                    if (_classicUnloadEntitiesProcessed >= unloadEntityLimit)
                    {
                        TrimClassicUnloadDictionaries(streaming);
                        return;
                    }

                    using (_classicProfiler.Value("ClassicBiome.UnloadRestore"))
                    {
                        var remaining = unloadEntityLimit - _classicUnloadEntitiesProcessed;
                        _classicUnloadEntitiesProcessed += RestoreClassicUnload(
                            biome,
                            gridUid,
                            grid,
                            chunk,
                            seed,
                            streaming,
                            currentState,
                            Math.Min(
                                remaining,
                                Math.Clamp(streaming.UnloadEntitiesPerSlice, 1, ClassicChunkCells)),
                            stopWhenBudgetExpires: !budgetExpired);
                    }
                    break;
            }

            if (!streaming.PartialUnloads.TryGetValue(chunk, out var continuingState) ||
                !ReferenceEquals(currentState, continuingState))
            {
                selectedState = null;
            }

            if (ClassicStreamingBudgetExpired() &&
                _classicForcedUnloadOperations >= forcedLimit)
            {
                break;
            }
        }

        TrimClassicUnloadDictionaries(streaming);
    }

    private static void TrimClassicUnloadDictionaries(ClassicBiomeStreamingComponent streaming)
    {
        if (streaming.PendingUnloads.Count <= 256)
            TrimClassicDictionary(streaming.PendingUnloads, 16);
        if (streaming.PartialUnloads.Count <= 64)
            TrimClassicDictionary(streaming.PartialUnloads, 8);
    }

    private bool TryStartClassicUnload(
        BiomeComponent biome,
        HashSet<Vector2i> active,
        ClassicBiomeStreamingComponent streaming,
        bool ignoreDelay,
        out Vector2i chunk,
        [NotNullWhen(true)] out ClassicBiomePartialUnloadState? state)
    {
        chunk = default;
        state = null;
        var found = false;
        var foundPartial = false;
        var foundDeadline = TimeSpan.MaxValue;
        var pressureCandidates = 0;

        _classicStaleUnloadChunks.Clear();
        foreach (var (candidate, deadline) in streaming.PendingUnloads)
        {
            if (_classicRearmedUnloadChunks.Contains(candidate))
                continue;

            if (IsClassicChunkRetained(active, streaming, candidate))
            {
                _classicStaleUnloadChunks.Add(candidate);
                continue;
            }

            var partial = streaming.PartialLoads.ContainsKey(candidate);
            if (!partial && !biome.LoadedChunks.Contains(candidate))
            {
                _classicStaleUnloadChunks.Add(candidate);
                continue;
            }

            if (ClassicZCacheAlive(streaming, candidate) ||
                !ignoreDelay && _classicTiming.CurTime < deadline)
            {
                continue;
            }

            if (ignoreDelay && pressureCandidates++ >= MaxClassicPressureUnloadScan)
                break;

            if (found && (foundPartial && !partial ||
                foundPartial == partial &&
                (deadline > foundDeadline ||
                 deadline == foundDeadline && CompareClassicChunks(candidate, chunk) >= 0)))
            {
                continue;
            }

            chunk = candidate;
            found = true;
            foundPartial = partial;
            foundDeadline = deadline;
        }

        foreach (var stale in _classicStaleUnloadChunks)
            streaming.PendingUnloads.Remove(stale);

        if (!found)
            return false;

        var partialCursor = 0;
        var priorityCells = 0UL;
        var materializedCells = ulong.MaxValue;
        if (foundPartial)
        {
            if (!streaming.PartialLoads.Remove(chunk, out partialCursor))
                return false;
            streaming.PriorityLoadedCells.Remove(chunk, out priorityCells);
            streaming.MaterializedCells.Remove(chunk, out materializedCells);
            materializedCells |= ClassicCellMask(0, partialCursor) | priorityCells;
        }
        else if (!biome.LoadedChunks.Remove(chunk))
        {
            return false;
        }
        else
        {
            InvalidateClassicOpeningScansForChunk(streaming, chunk);
        }

        streaming.PendingUnloads.Remove(chunk);
        state = new ClassicBiomePartialUnloadState
        {
            WasPartial = foundPartial,
            PartialCursor = Math.Clamp(partialCursor, 0, ClassicChunkCells),
            PriorityCells = priorityCells,
            MaterializedCells = materializedCells,
        };
        streaming.PartialUnloads.Add(chunk, state);
        return true;
    }

    private static bool TryGetClassicPartialUnload(
        ClassicBiomeStreamingComponent streaming,
        out Vector2i chunk,
        [NotNullWhen(true)] out ClassicBiomePartialUnloadState? state)
    {
        chunk = default;
        state = null;
        var found = false;
        var foundRank = int.MaxValue;

        foreach (var (candidate, candidateState) in streaming.PartialUnloads)
        {
            var rank = candidateState.Phase == ClassicBiomePartialUnloadPhase.Restoring
                ? 0
                : candidateState.WasPartial
                    ? 1
                    : 2;
            if (found && (rank > foundRank ||
                rank == foundRank && CompareClassicChunks(candidate, chunk) >= 0))
            {
                continue;
            }

            chunk = candidate;
            state = candidateState;
            found = true;
            foundRank = rank;
        }

        return found;
    }

    private int UnloadClassicEntitySlice(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        ClassicBiomeStreamingComponent streaming,
        ClassicBiomePartialUnloadState state,
        int limit,
        bool stopWhenBudgetExpires = true)
    {
        if (!biome.LoadedEntities.TryGetValue(chunk, out var loaded) || loaded.Count == 0)
        {
            biome.LoadedEntities.Remove(chunk);
            state.Phase = ClassicBiomePartialUnloadPhase.Finalize;
            return 0;
        }

        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();
        var depth = ClassicBiomeDepth(gridUid);
        var processed = 0;
        while (loaded.Count > 0 && processed < limit)
        {
            var tile = default(Vector2i);
            foreach (var pair in loaded)
            {
                tile = pair.Value;
                break;
            }

            _classicUnloadEntities.Clear();
            foreach (var pair in loaded)
            {
                if (pair.Value == tile)
                    _classicUnloadEntities.Add((pair.Key, pair.Value));
            }

            ClassicBiomeMarkerCellState? marker = null;
            if (streaming.MarkerCells.TryGetValue(chunk, out var markerCells))
                markerCells.TryGetValue(tile, out marker);

            var canUnload = true;
            foreach (var (entity, _) in _classicUnloadEntities)
            {
                if (marker != null && !marker.Entities.Contains(entity) ||
                    Deleted(entity) || !_xformQuery.TryGetComponent(entity, out var xform) ||
                    xform.ParentUid != gridUid ||
                    (marker == null || marker.Root != entity || marker.BaselineComponents == null) &&
                    _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != tile ||
                    ClassicHasPersistentBiomeDependent(gridUid, grid, tile, entity) ||
                    !(marker == null
                        ? ClassicCanUnloadTrackedBiomeEntity(biome, gridUid, grid, tile, entity, depth)
                        : ClassicCanUnloadMarkerEntity(marker, gridUid, entity, depth)))
                {
                    canUnload = false;
                    break;
                }
            }
            if (marker != null && marker.Entities.Count != _classicUnloadEntities.Count)
                canUnload = false;

            if (canUnload)
            {
                foreach (var (entity, _) in _classicUnloadEntities)
                    Del(entity);
                marker?.Entities.Clear();
                state.RemovedEntityCells.Add(tile);
            }
            else
            {
                modified.Add(tile);
            }

            if (marker != null && (!canUnload || modified.Contains(tile)))
                marker.Modified = true;

            foreach (var (entity, _) in _classicUnloadEntities)
                loaded.Remove(entity);
            processed += _classicUnloadEntities.Count;

            if (stopWhenBudgetExpires && ClassicStreamingBudgetExpired())
                break;
        }

        if (loaded.Count == 0)
        {
            biome.LoadedEntities.Remove(chunk);
            state.Phase = ClassicBiomePartialUnloadPhase.Finalize;
        }

        StoreClassicModifiedTiles(biome, chunk, modified);
        return processed;
    }

    private void AdvanceClassicUnloadFinalize(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        int seed,
        ClassicBiomePartialUnloadState state,
        int limit,
        bool stopWhenBudgetExpires = true)
    {
        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();

        var processed = 0;
        state.FinalizeCursor = Math.Clamp(state.FinalizeCursor, 0, ClassicChunkCells);
        while (state.FinalizeCursor < ClassicChunkCells && processed < Math.Clamp(limit, 1, ClassicChunkCells))
        {
            if (processed > 0 && stopWhenBudgetExpires && ClassicStreamingBudgetExpired())
                break;

            var remaining = state.MaterializedCells & (ulong.MaxValue << state.FinalizeCursor);
            if (remaining == 0)
            {
                state.FinalizeCursor = ClassicChunkCells;
                break;
            }

            var cell = BitOperations.TrailingZeroCount(remaining);
            state.FinalizeCursor = cell + 1;
            processed++;

            var x = cell / ChunkSize;
            var y = cell % ChunkSize;
            var indices = new Vector2i(x + chunk.X, y + chunk.Y);
            if (modified.Contains(indices))
                continue;

            if (ClassicHasBlockingAnchoredEntity(gridUid, grid, indices))
            {
                modified.Add(indices);
                continue;
            }

            if (!TryGetTile(indices, biome.Layers, seed, (Entity<MapGridComponent>?) null, out var biomeTile) ||
                _mapSystem.TryGetTileRef(gridUid, grid, indices, out var tileRef) &&
                tileRef.Tile != biomeTile.Value)
            {
                modified.Add(indices);
                continue;
            }

            state.ClearableCells |= 1UL << cell;

            if (stopWhenBudgetExpires && ClassicStreamingBudgetExpired())
                break;
        }

        if (state.FinalizeCursor < ClassicChunkCells &&
            (state.MaterializedCells & (ulong.MaxValue << state.FinalizeCursor)) == 0)
            state.FinalizeCursor = ClassicChunkCells;

        StoreClassicModifiedTiles(biome, chunk, modified);
        if (state.FinalizeCursor >= ClassicChunkCells)
            state.Phase = ClassicBiomePartialUnloadPhase.Commit;
    }

    private void CommitClassicUnload(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        ClassicBiomeStreamingComponent streaming,
        ClassicBiomePartialUnloadState state)
    {
        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();

        if (biome.LoadedDecals.Remove(chunk, out var decals))
        {
            foreach (var (decal, indices) in decals)
            {
                if (!_decals.RemoveDecal(gridUid, decal))
                    modified.Add(indices);
            }
        }

        _unloadTiles.Clear();
        for (var cell = 0; cell < ClassicChunkCells; cell++)
        {
            if ((state.ClearableCells & (1UL << cell)) == 0)
                continue;

            var x = cell / ChunkSize;
            var y = cell % ChunkSize;
            var indices = new Vector2i(x + chunk.X, y + chunk.Y);
            if (modified.Contains(indices))
                continue;

            if (ClassicHasBlockingAnchoredEntity(gridUid, grid, indices))
            {
                modified.Add(indices);
                continue;
            }

            _unloadTiles.Add((indices, Tile.Empty));
        }

        SetClassicBiomeTiles(gridUid, grid, _unloadTiles);
        _unloadTiles.Clear();

        if (streaming.MarkerCells.TryGetValue(chunk, out var markerCells))
        {
            foreach (var (tile, marker) in markerCells)
            {
                if (!Deleted(marker.Root))
                    RemComp<ClassicBiomeAmbientMarkerComponent>(marker.Root);

                if (marker.Modified || modified.Contains(tile))
                    continue;

                var nodes = biome.PendingMarkers.GetOrNew(chunk).GetOrNew(marker.Layer);
                if (!nodes.Contains(tile))
                    nodes.Add(tile);
            }

            streaming.MarkerCells.Remove(chunk);
        }

        streaming.PartialUnloads.Remove(chunk);
        streaming.PendingUnloads.Remove(chunk);
        StoreClassicModifiedTiles(biome, chunk, modified);
    }

    private int RestoreClassicUnload(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        int seed,
        ClassicBiomeStreamingComponent streaming,
        ClassicBiomePartialUnloadState state,
        int limit,
        Vector2i? priorityCell = null,
        bool stopWhenBudgetExpires = true)
    {
        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();
        _classicRestoreCells.Clear();
        if (priorityCell is { } requested)
        {
            if (state.RemovedEntityCells.Contains(requested))
                _classicRestoreCells.Add(requested);
        }
        else
        {
            foreach (var tile in state.RemovedEntityCells)
                _classicRestoreCells.Add(tile);
            _classicRestoreCells.Sort(CompareClassicChunks);
            if (_classicRestoreCells.Count > limit)
                _classicRestoreCells.RemoveRange(limit, _classicRestoreCells.Count - limit);
        }

        biome.LoadedEntities.TryGetValue(chunk, out var loaded);
        var processed = 0;
        foreach (var tile in _classicRestoreCells)
        {
            ClassicBiomeMarkerCellState? marker = null;
            if (streaming.MarkerCells.TryGetValue(chunk, out var markerCells))
                markerCells.TryGetValue(tile, out marker);

            if (!modified.Contains(tile))
            {
                _classicUnloadEntities.Clear();
                if (loaded != null)
                {
                    foreach (var pair in loaded)
                    {
                        if (pair.Value == tile)
                            _classicUnloadEntities.Add((pair.Key, pair.Value));
                    }
                }

                var canRestore = true;
                var depth = ClassicBiomeDepth(gridUid);
                foreach (var (entity, _) in _classicUnloadEntities)
                {
                    if (marker != null && !marker.Entities.Contains(entity) ||
                        Deleted(entity) || !_xformQuery.TryGetComponent(entity, out var xform) ||
                        xform.ParentUid != gridUid ||
                        (marker == null || marker.Root != entity || marker.BaselineComponents == null) &&
                        _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != tile ||
                        ClassicHasPersistentBiomeDependent(gridUid, grid, tile, entity) ||
                        !(marker == null
                            ? ClassicCanUnloadTrackedBiomeEntity(biome, gridUid, grid, tile, entity, depth)
                            : ClassicCanUnloadMarkerEntity(marker, gridUid, entity, depth)))
                    {
                        canRestore = false;
                        break;
                    }
                }

                if (canRestore)
                {
                    foreach (var (entity, _) in _classicUnloadEntities)
                    {
                        Del(entity);
                        loaded!.Remove(entity);
                    }

                    if (ClassicHasBlockingAnchoredEntity(gridUid, grid, tile))
                    {
                        modified.Add(tile);
                        if (marker != null)
                            marker.Modified = true;
                    }
                    else if (marker is { Modified: false })
                    {
                        loaded = SpawnClassicBiomeMarkerEntity(
                            marker.Prototype,
                            gridUid,
                            grid,
                            tile,
                            loaded,
                            marker);
                    }
                    else if (marker == null && TryGetEntity(tile, biome, (gridUid, grid), out var prototype))
                    {
                        loaded = SpawnClassicBiomeEntity(prototype, gridUid, grid, tile, loaded);
                    }
                }
                else
                {
                    modified.Add(tile);
                    if (marker != null)
                        marker.Modified = true;
                }
            }
            else if (marker != null)
            {
                marker.Modified = true;
            }

            state.RemovedEntityCells.Remove(tile);
            processed++;

            if (stopWhenBudgetExpires && ClassicStreamingBudgetExpired())
                break;
        }

        if (loaded is { Count: > 0 })
            biome.LoadedEntities[chunk] = loaded;
        else
            biome.LoadedEntities.Remove(chunk);

        StoreClassicModifiedTiles(biome, chunk, modified);
        if (state.RemovedEntityCells.Count > 0)
            return processed;

        streaming.PartialUnloads.Remove(chunk);
        streaming.PartialLoads.Remove(chunk);
        streaming.PriorityLoadedCells.Remove(chunk);
        streaming.MaterializedCells.Remove(chunk);
        if (_activeChunks.TryGetValue(biome, out var active) &&
            IsClassicChunkRetained(active, streaming, chunk))
        {
            streaming.PendingUnloads.Remove(chunk);
        }
        else
        {
            streaming.PendingUnloads.TryAdd(chunk, GetClassicUnloadDeadline(streaming, chunk));
            _classicRearmedUnloadChunks.Add(chunk);
        }

        if (!state.WasPartial || state.PartialCursor >= ClassicChunkCells)
        {
            if (biome.LoadedChunks.Add(chunk))
                InvalidateClassicOpeningScansForChunk(streaming, chunk);
            return processed;
        }

        var cursor = Math.Clamp(state.PartialCursor, 0, ClassicChunkCells);
        streaming.PartialLoads[chunk] = cursor;
        var sequential = cursor == 0 ? 0UL : (1UL << cursor) - 1UL;
        var priority = state.PriorityCells & ~sequential;
        if (priority != 0)
            streaming.PriorityLoadedCells[chunk] = priority;

        var materialized = state.MaterializedCells & ~sequential;
        if (materialized != 0 || cursor != 0)
            streaming.MaterializedCells[chunk] = materialized | sequential;
        return processed;
    }

    private void StoreClassicModifiedTiles(BiomeComponent biome, Vector2i chunk, HashSet<Vector2i> modified)
    {
        if (modified.Count > 0)
        {
            biome.ModifiedTiles[chunk] = modified;
            return;
        }

        biome.ModifiedTiles.Remove(chunk);
        _tilePool.Return(modified);
    }

    private static int CompareClassicChunks(Vector2i a, Vector2i b)
    {
        var result = a.X.CompareTo(b.X);
        return result != 0 ? result : a.Y.CompareTo(b.Y);
    }

    private static bool IsClassicChunkRetained(
        HashSet<Vector2i> active,
        ClassicBiomeStreamingComponent streaming,
        Vector2i chunk)
    {
        return active.Contains(chunk) ||
               streaming.LandingChunks.Contains(chunk) ||
               streaming.AdjacentPrefetchChunks.Contains(chunk);
    }

    private static ulong ClassicCellMask(int start, int end)
    {
        start = Math.Clamp(start, 0, ClassicChunkCells);
        end = Math.Clamp(end, start, ClassicChunkCells);
        var belowEnd = end == ClassicChunkCells ? ulong.MaxValue : (1UL << end) - 1UL;
        var belowStart = start == 0 ? 0UL : (1UL << start) - 1UL;
        return belowEnd & ~belowStart;
    }

    private int GetClassicBackgroundEntitySpawnLimit(
        EntityUid gridUid,
        ClassicBiomeStreamingComponent streaming)
    {
        var gridLimit = Math.Clamp(
            streaming.BackgroundEntitySpawnsPerTick,
            1,
            MaxClassicBackgroundEntitySpawnsPerTick);
        var gridSpawns = _classicBackgroundEntitySpawnsByGrid.GetValueOrDefault(gridUid);
        var remaining = Math.Min(
            _classicBackgroundEntitySpawnLimit - _classicBackgroundEntitySpawns,
            gridLimit - gridSpawns);
        return _classicBackgroundEntitySpawns + Math.Max(0, remaining);
    }

    private void RecordClassicBackgroundEntitySpawns(EntityUid gridUid, int count)
    {
        if (count <= 0)
            return;

        _classicBackgroundEntitySpawns += count;
        _classicBackgroundEntitySpawnsByGrid[gridUid] =
            _classicBackgroundEntitySpawnsByGrid.GetValueOrDefault(gridUid) + count;
    }

    private void LoadClassicBackgroundChunks(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        ClassicBiomeStreamingComponent streaming,
        bool includeAdjacentPrefetch)
    {
        var unloadPressure = ClassicBiomeHasUnloadPressure(streaming);
        var entitySpawnLimit = GetClassicBackgroundEntitySpawnLimit(gridUid, streaming);

        var loadSlots = Math.Clamp(streaming.BackgroundChunksPerTick, 0, MaxClassicBackgroundChunksPerTick);
        if (loadSlots == 0 && streaming.PartialLoads.Count > 0)
            loadSlots = 1;
        if (loadSlots == 0)
            return;

        var cellsPerSlice = Math.Clamp(streaming.BackgroundCellsPerSlice, 1, ClassicChunkCells);
        for (var slot = 0; slot < loadSlots; slot++)
        {
            if (ClassicBiomeLoadsEntities(gridUid) &&
                _classicBackgroundEntitySpawns >= entitySpawnLimit)
            {
                break;
            }

            Vector2i chunk;
            bool continuation;
            if (ClassicStreamingBudgetExpired())
            {
                if (_classicForcedProgressUsed ||
                    !(unloadPressure
                        ? TrySelectClassicActivePartialLoad(
                            biome,
                            streaming,
                            out chunk,
                            true,
                            false)
                        : _classicStreamingWorkBudget > TimeSpan.Zero
                            ? TrySelectClassicBackgroundChunk(biome, streaming, out chunk, false)
                            : TrySelectClassicActivePartialLoad(biome, streaming, out chunk, false, false)))
                {
                    break;
                }

                continuation = streaming.PartialLoads.ContainsKey(chunk);
            }
            else
            {
                if (unloadPressure
                        ? !TrySelectClassicActivePartialLoad(
                            biome,
                            streaming,
                            out chunk,
                            includeAdjacentPrefetch: includeAdjacentPrefetch)
                        : !TrySelectClassicBackgroundChunk(
                            biome,
                            streaming,
                            out chunk,
                            includeAdjacentPrefetch))
                {
                    break;
                }

                continuation = streaming.PartialLoads.ContainsKey(chunk);
            }

            if (!TryBeginClassicBoundedOperation(continuation || unloadPressure || ClassicStreamingBudgetExpired()))
                break;

            if (!continuation)
                streaming.PartialLoads.Add(chunk, 0);

            if (biome.LoadedChunks.Contains(chunk))
            {
                streaming.PartialLoads.Remove(chunk);
                streaming.PriorityLoadedCells.Remove(chunk);
                streaming.MaterializedCells.Remove(chunk);
                continue;
            }

            if (biome.PendingMarkers.ContainsKey(chunk))
            {
                var remainingSpawns = entitySpawnLimit - _classicBackgroundEntitySpawns;
                if (remainingSpawns <= 0)
                    break;

                using (_classicProfiler.Value("ClassicBiome.MarkerNodeSlice"))
                {
                    var processed = LoadChunkMarkerNodes(
                        biome,
                        gridUid,
                        grid,
                        chunk,
                        seed,
                        Math.Min(
                            remainingSpawns,
                            Math.Clamp(streaming.MarkerNodesPerSlice, 1, ClassicChunkCells)));
                    RecordClassicBackgroundEntitySpawns(gridUid, processed);
                }

                if (ClassicStreamingBudgetExpired())
                    _classicForcedProgressUsed = true;

                continue;
            }

            var previousCell = Math.Clamp(streaming.PartialLoads[chunk], 0, ClassicChunkCells);
            var nextCell = previousCell;
            var scheduledEnd = Math.Min(ClassicChunkCells, previousCell + cellsPerSlice);
            using (_classicProfiler.Value("ClassicBiome.BackgroundSlice"))
            {
                nextCell = LoadChunkCells(
                    biome,
                    gridUid,
                    grid,
                    chunk,
                    seed,
                    nextCell,
                    cellsPerSlice,
                    stopWhenClassicBudgetExpires: !ClassicStreamingBudgetExpired(),
                    classicEntitySpawnLimit: entitySpawnLimit);
            }

            if (ClassicStreamingBudgetExpired())
                _classicForcedProgressUsed = true;

            streaming.MaterializedCells[chunk] =
                streaming.MaterializedCells.GetValueOrDefault(chunk) |
                ClassicCellMask(previousCell, scheduledEnd);

            if (nextCell < ClassicChunkCells)
            {
                streaming.PartialLoads[chunk] = nextCell;
                if (streaming.PriorityLoadedCells.TryGetValue(chunk, out var priority))
                {
                    var sequential = nextCell == 0 ? 0UL : (1UL << nextCell) - 1UL;
                    priority &= ~sequential;
                    if (priority == 0)
                        streaming.PriorityLoadedCells.Remove(chunk);
                    else
                        streaming.PriorityLoadedCells[chunk] = priority;
                }

                if (nextCell == previousCell &&
                    _classicBackgroundEntitySpawns >= entitySpawnLimit)
                {
                    break;
                }
                continue;
            }

            streaming.PartialLoads.Remove(chunk);
            streaming.PriorityLoadedCells.Remove(chunk);
            streaming.MaterializedCells.Remove(chunk);
            if (biome.LoadedChunks.Add(chunk))
                InvalidateClassicOpeningScansForChunk(streaming, chunk);
        }
    }

    private bool ClassicBiomeHasUnloadPressure(ClassicBiomeStreamingComponent streaming)
    {
        var limit = Math.Clamp(streaming.MaxUnloadBacklogBeforeThrottling, 0, 4096);
        var backlog = streaming.PartialUnloads.Count + streaming.PendingUnloads.Count;

        foreach (var chunk in streaming.CachedZChunks.Keys)
        {
            if (!ClassicZCacheAlive(streaming, chunk))
                continue;
            if (streaming.PartialUnloads.ContainsKey(chunk))
                backlog--;
            if (streaming.PendingUnloads.ContainsKey(chunk))
                backlog--;
        }

        return backlog > limit;
    }

    private bool ClassicBiomeHasReadyUnload(ClassicBiomeStreamingComponent streaming)
    {
        if (streaming.PendingUnloads.Count == 0)
            return false;
        if (ClassicBiomeHasUnloadPressure(streaming))
            return true;

        foreach (var (chunk, deadline) in streaming.PendingUnloads)
        {
            if (!ClassicZCacheAlive(streaming, chunk) && deadline <= _classicTiming.CurTime)
                return true;
        }

        return false;
    }

    private static bool ClassicBiomeHasInactivePreviousChunks(
        BiomeComponent biome,
        HashSet<Vector2i> active,
        ClassicBiomeStreamingComponent streaming)
    {
        foreach (var chunk in streaming.PreviousActiveChunks)
        {
            if (!IsClassicChunkRetained(active, streaming, chunk) &&
                (biome.LoadedChunks.Contains(chunk) || streaming.PartialLoads.ContainsKey(chunk)))
            {
                return true;
            }
        }

        return false;
    }

    private bool ClassicZCacheAlive(ClassicBiomeStreamingComponent streaming, Vector2i chunk)
    {
        return streaming.CachedZChunks.TryGetValue(chunk, out var deadline) &&
               deadline > _classicTiming.CurTime;
    }

    private bool ClassicBiomeHasForegroundStreamingWork(
        BiomeComponent biome,
        ClassicBiomeStreamingComponent streaming)
    {
        if (_markerChunks.TryGetValue(biome, out var markerRequests) && markerRequests.Count > 0 ||
            streaming.MarkerAreaScan != null)
            return true;

        foreach (var candidate in _activeChunks[biome])
        {
            if (streaming.AdjacentPrefetchChunks.Contains(candidate))
                continue;
            if (biome.PendingMarkers.ContainsKey(candidate))
                return true;
            if (biome.LoadedChunks.Contains(candidate) || streaming.PartialUnloads.ContainsKey(candidate))
                continue;
            if (streaming.ViewerChunks.Contains(candidate) && !streaming.PartialLoads.ContainsKey(candidate))
                continue;
            return true;
        }

        return false;
    }

    private void LoadClassicPendingMarkerNodes(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        ClassicBiomeStreamingComponent streaming)
    {
        var entitySpawnLimit = GetClassicBackgroundEntitySpawnLimit(gridUid, streaming);
        var remainingSpawns = entitySpawnLimit - _classicBackgroundEntitySpawns;
        if (remainingSpawns <= 0)
            return;

        var active = _activeChunks[biome];
        var found = false;
        var foundRank = int.MaxValue;
        var chunk = default(Vector2i);
        foreach (var candidate in active)
        {
            var rank = streaming.AdjacentPrefetchChunks.Contains(candidate) ? 1 : 0;
            if (!biome.PendingMarkers.ContainsKey(candidate) || !biome.LoadedChunks.Contains(candidate) ||
                found && (rank > foundRank ||
                          rank == foundRank && CompareClassicChunks(candidate, chunk) >= 0))
            {
                continue;
            }

            chunk = candidate;
            found = true;
            foundRank = rank;
        }

        if (!found)
            return;

        if (!TryBeginClassicBoundedOperation(true))
            return;

        using (_classicProfiler.Value("ClassicBiome.MarkerNodeSlice"))
        {
            var processed = LoadChunkMarkerNodes(
                biome,
                gridUid,
                grid,
                chunk,
                seed,
                Math.Min(
                    remainingSpawns,
                    Math.Clamp(streaming.MarkerNodesPerSlice, 1, ClassicChunkCells)));
            RecordClassicBackgroundEntitySpawns(gridUid, processed);
        }

        if (ClassicStreamingBudgetExpired())
            _classicForcedProgressUsed = true;
    }

    private bool TrySelectClassicBackgroundChunk(
        BiomeComponent biome,
        ClassicBiomeStreamingComponent streaming,
        out Vector2i chunk,
        bool includeAdjacentPrefetch = true)
    {
        var active = _activeChunks[biome];

        chunk = default;
        var viewers = streaming.ViewerCenters;
        if (viewers.Count > 0)
        {
            var viewerIndex = Math.Abs(streaming.ViewerCursor % viewers.Count);
            var viewer = viewers[viewerIndex];
            var found = false;
            var distance = float.MaxValue;

            foreach (var candidate in active)
            {
                if (biome.LoadedChunks.Contains(candidate) ||
                    !includeAdjacentPrefetch && streaming.AdjacentPrefetchChunks.Contains(candidate) ||
                    streaming.ViewerChunks.Contains(candidate) && !streaming.PartialLoads.ContainsKey(candidate) ||
                    streaming.PartialUnloads.ContainsKey(candidate))
                {
                    continue;
                }

                var delta = candidate - viewer;
                var candidateDistance = (float) delta.X * delta.X + (float) delta.Y * delta.Y;
                if (found && (candidateDistance > distance ||
                    candidateDistance.Equals(distance) && CompareClassicChunks(candidate, chunk) >= 0))
                {
                    continue;
                }

                chunk = candidate;
                distance = candidateDistance;
                found = true;
            }

            if (found)
            {
                streaming.ViewerCursor = (viewerIndex + 1) % viewers.Count;
                return true;
            }
        }

        var foundPartial = false;
        chunk = default;
        foreach (var candidate in streaming.PartialLoads.Keys)
        {
            if (!active.Contains(candidate) ||
                !includeAdjacentPrefetch && streaming.AdjacentPrefetchChunks.Contains(candidate) ||
                foundPartial && CompareClassicChunks(candidate, chunk) >= 0)
            {
                continue;
            }

            chunk = candidate;
            foundPartial = true;
        }

        return foundPartial;
    }

    /// <summary>
    /// Selects an already-started active chunk without walking the much larger active view range.
    /// This is used only for the single globally forced continuation after the shared deadline.
    /// </summary>
    private bool TrySelectClassicActivePartialLoad(
        BiomeComponent biome,
        ClassicBiomeStreamingComponent streaming,
        out Vector2i chunk,
        bool includePriorityOnly = true,
        bool includeAdjacentPrefetch = true)
    {
        var active = _activeChunks[biome];
        var viewers = streaming.ViewerCenters;
        var viewerIndex = viewers.Count == 0
            ? -1
            : Math.Abs(streaming.ViewerCursor % viewers.Count);
        var viewer = viewerIndex < 0 ? default : viewers[viewerIndex];
        var found = false;
        var distance = float.MaxValue;
        chunk = default;

        foreach (var candidate in streaming.PartialLoads.Keys)
        {
            if (!active.Contains(candidate) ||
                (!includePriorityOnly &&
                 streaming.PartialLoads[candidate] == 0 &&
                 streaming.PriorityLoadedCells.GetValueOrDefault(candidate) != 0) ||
                !includeAdjacentPrefetch && streaming.AdjacentPrefetchChunks.Contains(candidate) ||
                biome.LoadedChunks.Contains(candidate) ||
                streaming.PartialUnloads.ContainsKey(candidate))
            {
                continue;
            }

            if (viewerIndex < 0)
            {
                if (found && CompareClassicChunks(candidate, chunk) >= 0)
                    continue;
            }
            else
            {
                var delta = candidate - viewer;
                var candidateDistance = (float) delta.X * delta.X + (float) delta.Y * delta.Y;
                if (found && (candidateDistance > distance ||
                    candidateDistance.Equals(distance) && CompareClassicChunks(candidate, chunk) >= 0))
                {
                    continue;
                }

                distance = candidateDistance;
            }

            chunk = candidate;
            found = true;
        }

        if (found && viewerIndex >= 0)
            streaming.ViewerCursor = (viewerIndex + 1) % viewers.Count;

        return found;
    }

    private bool ClassicStreamingBudgetExpired()
    {
        return Stopwatch.GetElapsedTime(_classicStreamingWorkStarted) >= _classicStreamingWorkBudget;
    }

    private bool ClassicStreamingBudgetExpired(ClassicBiomeStreamingComponent streaming)
    {
        return ClassicStreamingBudgetExpired();
    }

    private bool TryBeginClassicBoundedOperation(bool continuation)
    {
        if (!ClassicStreamingBudgetExpired())
            return true;
        if (!continuation || _classicForcedProgressUsed)
            return false;

        _classicForcedProgressUsed = true;
        return true;
    }

    private bool TryBeginClassicOpeningOperation(out bool forced)
    {
        forced = false;
        if (!ClassicStreamingBudgetExpired())
            return true;
        if (_classicOpeningForcedProgressUsed)
            return false;

        _classicOpeningForcedProgressUsed = true;
        forced = true;
        return true;
    }

    /// <summary>
    /// Processes every procedural Z biome under one server-tick budget. Rotating the first biome
    /// prevents a busy surface (or any fixed entity-query order) from starving deeper levels.
    /// Viewer chunks remain synchronous because Z movement needs their landing tile this tick.
    /// </summary>
    private void ProcessClassicStreamingBiomes()
    {
        var count = _classicStreamingBiomes.Count;
        if (count == 0)
            return;

        ConfigureClassicStreamingLimits();

        var start = _classicStreamingStartIndex % count;
        for (var i = 0; i < count; i++)
        {
            var entry = _classicStreamingBiomes[(start + i) % count];
            LoadClassicViewerChunks(entry.Biome, entry.GridUid, entry.Grid, entry.Biome.Seed, entry.Streaming);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var unloadPass = _classicUnloadFirst ? pass == 0 : pass == 1;
            if (unloadPass)
            {
                for (var i = 0; i < count; i++)
                {
                    var entry = _classicStreamingBiomes[(start + i) % count];
                    UnloadChunks(entry.Biome, entry.GridUid, entry.Grid, entry.Biome.Seed);
                }
                continue;
            }

            _classicForegroundStreamingBiomes.Clear();
            foreach (var entry in _classicStreamingBiomes)
            {
                if (ClassicBiomeHasForegroundStreamingWork(entry.Biome, entry.Streaming))
                    _classicForegroundStreamingBiomes.Add(entry.GridUid);
            }

            for (var priority = 0; priority < 2; priority++)
            for (var i = 0; i < count; i++)
            {
                var entry = _classicStreamingBiomes[(start + i) % count];
                var foreground = _classicForegroundStreamingBiomes.Contains(entry.GridUid);
                if (foreground != (priority == 0))
                    continue;

                _markerChunks.TryGetValue(entry.Biome, out var markerRequests);
                PruneClassicMarkerAreas(entry.Biome, entry.Streaming, markerRequests);
                BuildMarkerChunks(entry.Biome, entry.GridUid, entry.Grid, entry.Biome.Seed);
                LoadClassicPendingMarkerNodes(
                    entry.Biome,
                    entry.GridUid,
                    entry.Grid,
                    entry.Biome.Seed,
                    entry.Streaming);
                LoadClassicBackgroundChunks(
                    entry.Biome,
                    entry.GridUid,
                    entry.Grid,
                    entry.Biome.Seed,
                    entry.Streaming,
                    !foreground);
            }
        }

        _classicUnloadFirst = !_classicUnloadFirst;
        _classicStreamingStartIndex = (start + 1) % count;
        _classicForegroundStreamingBiomes.Clear();
        _classicStreamingBiomes.Clear();
    }

    private void ConfigureClassicStreamingLimits()
    {
        var hasBudget = false;
        var budget = TimeSpan.Zero;
        foreach (var entry in _classicStreamingBiomes)
        {
            var active = _activeChunks[entry.Biome];
            var runnable = active.Count > 0 ||
                           entry.Streaming.PartialUnloads.Count > 0 ||
                           ClassicBiomeHasInactivePreviousChunks(entry.Biome, active, entry.Streaming) ||
                           !entry.Streaming.UnloadTrackingInitialized &&
                           (entry.Streaming.PartialLoads.Count > 0 || entry.Biome.LoadedChunks.Count > 0) ||
                           ClassicBiomeHasReadyUnload(entry.Streaming);
            if (!runnable)
                continue;

            var entryBudget = entry.Streaming.WorkBudget < TimeSpan.Zero
                ? TimeSpan.Zero
                : entry.Streaming.WorkBudget;
            if (!hasBudget || entryBudget < budget)
            {
                budget = entryBudget;
                hasBudget = true;
            }

            if (active.Count > 0 || entry.Streaming.PartialLoads.Count > 0)
            {
                _classicBackgroundEntitySpawnLimit = Math.Max(
                    _classicBackgroundEntitySpawnLimit,
                    Math.Clamp(
                        entry.Streaming.BackgroundEntitySpawnsPerTick,
                        1,
                        MaxClassicBackgroundEntitySpawnsPerTick));
            }

            _classicUnloadEntityLimit = Math.Max(
                _classicUnloadEntityLimit,
                Math.Clamp(
                    entry.Streaming.UnloadEntitiesPerTick,
                    1,
                    MaxClassicUnloadEntitiesPerTick));
        }

        _classicStreamingWorkBudget = hasBudget ? budget : TimeSpan.Zero;
        _classicStreamingBudgetConfigured = hasBudget;
    }

    private void AddClassicAdjacentLandingChunks(EntityUid viewer, TransformComponent xform, Vector2 worldPos)
    {
        if (xform.MapUid is not { } mapUid ||
            !_classicZMapQuery.TryComp(mapUid, out var zMap) ||
            !_classicZPhysicsQuery.TryComp(viewer, out var zPhysics) ||
            zPhysics.Disabled)
        {
            return;
        }

        var hasBelow = _classicZLevels.TryMapDown((mapUid, zMap), out var below);
        var hasAbove = _classicZLevels.TryMapUp((mapUid, zMap), out var above);
        if (!hasBelow && !hasAbove)
            return;

        var hasCurrentGrid = TryGetClassicViewerGrid(
            xform,
            worldPos,
            out var currentGridUid,
            out var currentGrid,
            out var currentTile);

        if (hasBelow)
        {
            AddClassicAdjacentPrefetchChunks(below.Owner, worldPos);
            var hasCurrentFloor = hasCurrentGrid &&
                                  ClassicCurrentCellHasFloor(currentGridUid, currentGrid!, currentTile);
            if (!hasCurrentFloor || zPhysics.Velocity < 0f || zPhysics.LocalPosition < 0f)
                AddClassicLandingChunk(below.Owner, worldPos);
        }

        if (!hasAbove)
            return;

        var upper = above;
        for (var i = 0; i < ClassicSharedZLevelsSystem.MaxZLevelsAboveRendering; i++)
        {
            AddClassicAdjacentPrefetchChunks(upper.Owner, worldPos);
            if (i == 0 &&
                (zPhysics.Velocity > 0f ||
                 zPhysics.LocalPosition > 0f ||
                 hasCurrentGrid && ClassicCurrentCellHasHighGround(currentGridUid, currentGrid!, currentTile)))
            {
                AddClassicLandingChunk(upper.Owner, worldPos);
            }

            if (!_classicZLevels.TryMapUp(upper.AsNullable(), out var nextUpper))
                break;

            upper = nextUpper;
        }
    }

    private void AddClassicVehicleExitSafety(EntityUid viewer)
    {
        if (HasComp<GhostComponent>(viewer) ||
            !_classicVehicles.TryResolveControlledVehicle(viewer, out var vehicle) ||
            !_classicHandledVehicleAnchors.Add(vehicle) ||
            !_xformQuery.TryGetComponent(vehicle, out var vehicleXform) ||
            vehicleXform.MapUid is not { } mapUid ||
            !_classicZMapQuery.TryComp(mapUid, out var zMap))
        {
            return;
        }

        var vehiclePos = _transform.GetWorldPosition(vehicleXform);
        var hasBelow = _classicZLevels.TryMapDown((mapUid, zMap), out var below);
        if (hasBelow)
            AddClassicAdjacentPrefetchChunks(below.Owner, vehiclePos);
        if (_classicZLevels.TryMapUp((mapUid, zMap), out var above))
        {
            for (var i = 0; i < ClassicSharedZLevelsSystem.MaxZLevelsAboveRendering; i++)
            {
                AddClassicAdjacentPrefetchChunks(above.Owner, vehiclePos);
                if (!_classicZLevels.TryMapUp(above.AsNullable(), out var nextAbove))
                    break;

                above = nextAbove;
            }
        }

        _classicVehicles.GetExteriorExitCoordinates(vehicle, _classicVehicleExitCoordinates);
        foreach (var exit in _classicVehicleExitCoordinates)
        {
            if (exit.MapId != vehicleXform.MapID)
                continue;

            AddClassicLandingChunk(mapUid, exit.Position);
            if (hasBelow)
                AddClassicLandingChunk(below.Owner, exit.Position);
        }
    }

    private void AddClassicAdjacentPrefetchChunks(EntityUid mapUid, Vector2 worldPos)
    {
        if (!TryComp<MapComponent>(mapUid, out var map))
            return;

        if (TryAdd(mapUid))
            return;

        if (_mapSystem.TryFindGridAt(mapUid, worldPos, out var containingGridUid, out _) &&
            TryAdd(containingGridUid))
        {
            return;
        }

        foreach (var grid in _mapSystem.GetAllGrids(map.MapId))
        {
            if (TryAdd(grid.Owner))
                return;
        }

        bool TryAdd(EntityUid gridUid)
        {
            if (!_biomeQuery.TryComp(gridUid, out var biome) ||
                !_classicStreamingQuery.TryComp(gridUid, out var streaming) ||
                !TryComp<MapGridComponent>(gridUid, out var grid) ||
                !_activeChunks.TryGetValue(biome, out var active))
            {
                return false;
            }

            var radius = Math.Clamp(streaming.AdjacentLevelPrefetchRadius, 0, ChunkSize / 2);
            if (radius == 0)
                return true;

            IncludeClassicStreamingBudget(streaming);
            var tile = _mapSystem.TileIndicesFor(
                gridUid,
                grid,
                new MapCoordinates(worldPos, map.MapId));
            var center = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
            if (!streaming.ViewerCenters.Contains(center))
                streaming.ViewerCenters.Add(center);

            var extent = new Vector2i(radius, radius);
            var firstChunk = SharedMapSystem.GetChunkIndices(tile - extent, ChunkSize);
            var lastChunk = SharedMapSystem.GetChunkIndices(tile + extent, ChunkSize);
            for (var x = firstChunk.X; x <= lastChunk.X; x++)
            for (var y = firstChunk.Y; y <= lastChunk.Y; y++)
            {
                var chunk = new Vector2i(x, y) * ChunkSize;
                if (active.Add(chunk))
                    streaming.AdjacentPrefetchChunks.Add(chunk);
            }

            return true;
        }
    }

    private bool TryGetClassicViewerGrid(
        TransformComponent xform,
        Vector2 worldPos,
        out EntityUid gridUid,
        [NotNullWhen(true)] out MapGridComponent? grid,
        out Vector2i tile)
    {
        if (xform.GridUid is { } attachedGrid && TryComp(attachedGrid, out grid))
        {
            gridUid = attachedGrid;
            tile = _mapSystem.WorldToTile(gridUid, grid, worldPos);
            return true;
        }

        if (xform.MapUid is { } mapUid && TryComp(mapUid, out grid))
        {
            gridUid = mapUid;
            tile = _mapSystem.WorldToTile(gridUid, grid, worldPos);
            return true;
        }

        if (xform.MapUid is { } fallbackMap &&
            _mapSystem.TryFindGridAt(fallbackMap, worldPos, out gridUid, out grid))
        {
            tile = _mapSystem.WorldToTile(gridUid, grid, worldPos);
            return true;
        }

        gridUid = default;
        grid = null;
        tile = default;
        return false;
    }

    private bool ClassicCurrentCellHasFloor(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (_mapSystem.TryGetTileRef(gridUid, grid, tile, out var tileRef) && !tileRef.Tile.IsEmpty)
            return true;

        if (!_biomeQuery.TryComp(gridUid, out var biome) || !biome.Enabled)
            return false;

        var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
        if (biome.ModifiedTiles.TryGetValue(chunk, out var modified) && modified.Contains(tile))
            return false;

        return TryGetBiomeTile(tile, biome.Layers, biome.Seed, (gridUid, grid), out var predicted) &&
               !predicted.Value.IsEmpty;
    }

    public bool ClassicIsTileOpaque(EntityUid mapUid, Vector2 worldPos)
    {
        EntityUid gridUid;
        MapGridComponent grid;
        if (_mapSystem.TryFindGridAt(mapUid, worldPos, out var containingGridUid, out var containingGrid))
        {
            gridUid = containingGridUid;
            grid = containingGrid;
        }
        else if (TryComp<MapGridComponent>(mapUid, out var mapGrid))
        {
            gridUid = mapUid;
            grid = mapGrid;
        }
        else
        {
            return false;
        }

        var tile = _mapSystem.WorldToTile(gridUid, grid, worldPos);
        if (_mapSystem.TryGetTileRef(gridUid, grid, tile, out var tileRef) && !tileRef.Tile.IsEmpty)
            return !ClassicZLevelOpeningCache.IsOpeningTile(tileRef.Tile, TileDefManager);

        if (!_biomeQuery.TryComp(gridUid, out var biome) || !biome.Enabled)
            return false;

        var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
        if (biome.ModifiedTiles.TryGetValue(chunk, out var modified) && modified.Contains(tile))
            return false;

        return TryGetBiomeTile(tile, biome.Layers, biome.Seed, (gridUid, grid), out var predicted) &&
               !ClassicZLevelOpeningCache.IsOpeningTile(predicted.Value, TileDefManager);
    }

    private bool ClassicCurrentCellHasHighGround(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (_classicHighGroundQuery.HasComp(uid.Value))
                return true;
        }

        return false;
    }

    private void AddClassicLandingChunk(EntityUid mapUid, Vector2 worldPos)
    {
        if (!TryComp<MapComponent>(mapUid, out var map))
            return;

        if (TryAdd(mapUid))
            return;

        if (_mapSystem.TryFindGridAt(mapUid, worldPos, out var containingGridUid, out _) &&
            TryAdd(containingGridUid))
        {
            return;
        }

        foreach (var grid in _mapSystem.GetAllGrids(map.MapId))
        {
            if (TryAdd(grid.Owner))
                return;
        }

        bool TryAdd(EntityUid gridUid)
        {
            if (!_biomeQuery.TryComp(gridUid, out var biome) ||
                !_classicStreamingQuery.TryComp(gridUid, out var streaming) ||
                !TryComp<MapGridComponent>(gridUid, out var grid) ||
                !_activeChunks.TryGetValue(biome, out var active))
            {
                return false;
            }

            var tile = _mapSystem.TileIndicesFor(
                gridUid,
                grid,
                new MapCoordinates(worldPos, map.MapId));
            var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
            streaming.LandingChunks.Add(chunk);
            streaming.ViewerCells.Add(tile);
            return true;
        }
    }

    private Vector2 ClassicBiomeViewerPosition(EntityUid? attached, EntityUid viewer, TransformComponent xform)
    {
        if (_classicZLevels.IsViewerEye(attached, viewer) &&
            _xformQuery.TryGetComponent(attached, out var ownerXform))
        {
            var ownerPosition = _transform.GetWorldPosition(ownerXform);
            return _eyeQuery.TryComp(viewer, out var zEye) ? ownerPosition + zEye.Offset : ownerPosition;
        }

        var position = _transform.GetWorldPosition(xform);
        return _eyeQuery.TryComp(viewer, out var eye) ? position + eye.Offset : position;
    }

    private float ClassicBiomeViewerScale(EntityUid viewer)
    {
        return _eyeQuery.TryComp(viewer, out var eye) ? MathF.Max(eye.PvsScale, 0.1f) : 1f;
    }

    /// <summary>
    /// Lower Z-eyes are useful only through transparent/open tiles. Wait until each intervening
    /// procedural level has finished its own PVS range, then mirror the client's opening test.
    /// This prevents an opaque surface from generating a complete hidden -Z1 range (and, with a
    /// larger render setting, all deeper ranges) while retaining exact fall landing cells.
    /// </summary>
    private bool ClassicTryGetViewerRange(
        EntityUid? attached,
        EntityUid viewer,
        TransformComponent attachedXform,
        TransformComponent viewerXform,
        Vector2 worldPos,
        float pvsScale,
        out Box2? worldOpeningClip)
    {
        var range = _classicLoadRange * MathF.Max(pvsScale, 0.1f);
        worldOpeningClip = null;
        if (!_classicZLevels.IsViewerEye(attached, viewer) ||
            attachedXform.MapUid is not { } attachedMap ||
            viewerXform.MapUid is not { } viewerMap ||
            !_classicZMapQuery.TryComp(attachedMap, out var attachedZ) ||
            !_classicZMapQuery.TryComp(viewerMap, out var viewerZ) ||
            viewerZ.Depth >= attachedZ.Depth)
        {
            return true;
        }

        var levelsToCheck = attachedZ.Depth - viewerZ.Depth;
        for (var offset = 0; offset < levelsToCheck; offset++)
        {
            EntityUid openingMap;
            if (offset == 0)
            {
                openingMap = attachedMap;
            }
            else if (!_classicZLevels.TryMapOffset((attachedMap, attachedZ), -offset, out var intermediate))
            {
                return false;
            }
            else
            {
                openingMap = intermediate.Owner;
            }

            if (TryGetClassicMapBiome(
                    openingMap,
                    worldPos,
                    out var openingGridUid,
                    out var openingBiome,
                    out var openingGrid))
            {
                var requiredLocalBounds = GetLocalBiomeViewBounds(
                    openingGridUid,
                    openingGrid,
                    worldPos,
                    pvsScale,
                    worldOpeningClip);
                if (requiredLocalBounds.Width <= 0f || requiredLocalBounds.Height <= 0f)
                    return false;

                Box2 openingBounds;
                using (_classicProfiler.Value("ClassicBiome.OpeningSlice"))
                {
                    if (!TryGetClassicOpeningWorldBoundsState(
                            openingGridUid,
                            openingBiome,
                            openingGrid,
                            requiredLocalBounds,
                            out openingBounds,
                            out var pending))
                    {
                        if (pending)
                            CacheClassicPendingZLevel(viewer);
                        else
                            _classicPendingOpeningCacheRefreshes.Remove(viewer);
                        return false;
                    }

                    _classicPendingOpeningCacheRefreshes.Remove(viewer);
                }

                var nextClip = openingBounds.Enlarged(3f);
                worldOpeningClip = worldOpeningClip is { } existingClip
                    ? existingClip.Intersect(nextClip)
                    : nextClip;
                if (worldOpeningClip.Value.Width <= 0f || worldOpeningClip.Value.Height <= 0f)
                    return false;
                continue;
            }

            var mapId = Transform(openingMap).MapID;
            var requiredWorldBounds = new Box2(-range, -range, range, range).Translated(worldPos);
            if (worldOpeningClip is { } existingWorldClip)
                requiredWorldBounds = requiredWorldBounds.Intersect(existingWorldClip);
            if (requiredWorldBounds.Width <= 0f || requiredWorldBounds.Height <= 0f)
                return false;

            _classicOpeningBounds.Clear();
            if (!_classicOpeningCache.TryFindOpeningBounds(
                    mapId,
                    requiredWorldBounds,
                    _classicOpeningBounds,
                    out var combinedOpeningBounds,
                    maxOpeningBounds: 4096,
                    exactOpeningBounds: true,
                    _classicOpeningGrids,
                    _mapSystem,
                    _transform,
                    TileDefManager))
            {
                return false;
            }

            var nextWorldClip = combinedOpeningBounds.Enlarged(3f);
            worldOpeningClip = worldOpeningClip is { } previousWorldClip
                ? previousWorldClip.Intersect(nextWorldClip)
                : nextWorldClip;
            if (worldOpeningClip.Value.Width <= 0f || worldOpeningClip.Value.Height <= 0f)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Retains the exact generated chunk range of a Z-eye that is about to be unsubscribed.
    /// Returning before the deadline cancels PendingUnloads through the normal active-chunk path.
    /// </summary>
    public void CacheClassicZLevel(EntityUid eye)
    {
        _classicPendingOpeningCacheRefreshes.Remove(eye);
        CacheClassicZLevelInternal(eye);
    }

    private void CacheClassicPendingZLevel(EntityUid eye)
    {
        if (_classicPendingOpeningCacheRefreshes.TryGetValue(eye, out var refresh) &&
            refresh > _classicTiming.CurTime)
        {
            return;
        }

        var duration = CacheClassicZLevelInternal(eye);
        if (duration is not { } cacheDuration || cacheDuration <= TimeSpan.Zero)
        {
            _classicPendingOpeningCacheRefreshes.Remove(eye);
            return;
        }

        var interval = TimeSpan.FromTicks(Math.Max(1L, cacheDuration.Ticks / 2L));
        if (interval > TimeSpan.FromSeconds(1))
            interval = TimeSpan.FromSeconds(1);
        var minimumTicks = Math.Max(1L, interval.Ticks / 2L);
        var jitterTicks = interval.Ticks - minimumTicks;
        var hash = unchecked((uint) eye.Id * 2654435761U);
        var staggerTicks = jitterTicks == 0L ? 0L : hash % (ulong) (jitterTicks + 1L);
        _classicPendingOpeningCacheRefreshes[eye] =
            _classicTiming.CurTime + TimeSpan.FromTicks(minimumTicks + (long) staggerTicks);
    }

    private TimeSpan? CacheClassicZLevelInternal(EntityUid eye)
    {
        if (!_xformQuery.TryComp(eye, out var eyeXform) || eyeXform.MapUid is not { } mapUid)
            return null;

        var worldPos = ClassicBiomeViewerPosition(null, eye, eyeXform);
        if (!TryGetClassicMapBiome(
                mapUid,
                worldPos,
                out var gridUid,
                out var biome,
                out var grid) ||
            !_classicStreamingQuery.TryComp(gridUid, out var streaming))
        {
            return null;
        }

        PruneClassicZLevelCache(streaming);
        var maxChunks = Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxClassicZLevelCachedChunks);
        if (maxChunks == 0)
            return TimeSpan.Zero;

        var duration = streaming.ZLevelCacheDuration < TimeSpan.Zero
            ? TimeSpan.Zero
            : streaming.ZLevelCacheDuration;
        if (duration <= TimeSpan.Zero)
            return duration;
        var deadline = _classicTiming.CurTime + duration;
        var bounds = GetLocalBiomeViewBounds(
            gridUid,
            grid,
            worldPos,
            ClassicBiomeViewerScale(eye));
        var chunks = new ChunkIndicesEnumerator(bounds, ChunkSize);
        _classicZCacheBatch.Clear();
        while (chunks.MoveNext(out var chunkIndices))
        {
            if (_classicZCacheBatch.Count >= maxChunks)
                break;

            var chunk = chunkIndices.Value * ChunkSize;
            if (!biome.LoadedChunks.Contains(chunk) &&
                !streaming.PartialLoads.ContainsKey(chunk) &&
                !streaming.PartialUnloads.ContainsKey(chunk))
            {
                continue;
            }

            if (!streaming.CachedZChunks.ContainsKey(chunk) &&
                streaming.CachedZChunks.Count >= maxChunks &&
                !TryEvictClassicZLevelCache(streaming, _classicZCacheBatch))
            {
                break;
            }

            streaming.CachedZChunks[chunk] = deadline;
            streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
            _classicZCacheBatch.Add(chunk);
            if (streaming.PendingUnloads.ContainsKey(chunk))
            {
                var ordinaryDeadline = GetClassicOrdinaryUnloadDeadline(streaming);
                streaming.PendingUnloads[chunk] = ordinaryDeadline > deadline ? ordinaryDeadline : deadline;
            }
            if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                partial.Phase = ClassicBiomePartialUnloadPhase.Restoring;
        }

        _classicZCacheBatch.Clear();
        return duration;
    }

    private void CacheClassicDepartedAdjacentPrefetchChunks(
        BiomeComponent biome,
        HashSet<Vector2i> active,
        ClassicBiomeStreamingComponent streaming)
    {
        if (streaming.PreviousAdjacentPrefetchChunks.Count == 0)
            return;

        var totalMax = Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxClassicZLevelCachedChunks);
        var maxChunks = Math.Min(
            Math.Clamp(streaming.MaxAdjacentPrefetchCachedChunks, 0, MaxClassicZLevelCachedChunks),
            totalMax);
        if (maxChunks == 0 || streaming.AdjacentPrefetchCacheDuration <= TimeSpan.Zero)
            return;

        var deadline = _classicTiming.CurTime + streaming.AdjacentPrefetchCacheDuration;
        _classicZCacheBatch.Clear();
        foreach (var chunk in streaming.PreviousAdjacentPrefetchChunks)
        {
            if (streaming.AdjacentPrefetchChunks.Contains(chunk) ||
                streaming.LandingChunks.Contains(chunk) ||
                active.Contains(chunk) ||
                !biome.LoadedChunks.Contains(chunk) &&
                !streaming.PartialLoads.ContainsKey(chunk) &&
                !streaming.PartialUnloads.ContainsKey(chunk))
            {
                continue;
            }

            if (streaming.CachedZChunks.ContainsKey(chunk) &&
                !streaming.CachedAdjacentPrefetchChunks.Contains(chunk))
            {
                continue;
            }

            var needsEntry = !streaming.CachedZChunks.ContainsKey(chunk);
            while (needsEntry &&
                   (streaming.CachedAdjacentPrefetchChunks.Count >= maxChunks ||
                    streaming.CachedZChunks.Count >= totalMax))
            {
                if (!TryEvictClassicAdjacentPrefetchCache(streaming, _classicZCacheBatch))
                    break;
            }

            if (needsEntry &&
                (streaming.CachedAdjacentPrefetchChunks.Count >= maxChunks ||
                 streaming.CachedZChunks.Count >= totalMax))
                continue;

            streaming.CachedZChunks[chunk] = deadline;
            streaming.CachedAdjacentPrefetchChunks.Add(chunk);
            _classicZCacheBatch.Add(chunk);
            if (streaming.PendingUnloads.TryGetValue(chunk, out var pending) && pending < deadline)
                streaming.PendingUnloads[chunk] = deadline;
            if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                partial.Phase = ClassicBiomePartialUnloadPhase.Restoring;
        }

        _classicZCacheBatch.Clear();
    }

    private bool TryEvictClassicAdjacentPrefetchCache(
        ClassicBiomeStreamingComponent streaming,
        HashSet<Vector2i> retained)
    {
        var found = false;
        var oldestChunk = default(Vector2i);
        var oldestDeadline = TimeSpan.MaxValue;
        foreach (var chunk in streaming.CachedAdjacentPrefetchChunks)
        {
            if (retained.Contains(chunk) ||
                !streaming.CachedZChunks.TryGetValue(chunk, out var deadline) ||
                found && (deadline > oldestDeadline ||
                          deadline == oldestDeadline && CompareClassicChunks(chunk, oldestChunk) >= 0))
            {
                continue;
            }

            found = true;
            oldestChunk = chunk;
            oldestDeadline = deadline;
        }

        if (!found)
            return false;

        RemoveClassicZLevelCache(streaming, oldestChunk);
        return true;
    }

    private TimeSpan GetClassicUnloadDeadline(ClassicBiomeStreamingComponent streaming, Vector2i chunk)
    {
        var deadline = GetClassicOrdinaryUnloadDeadline(streaming);
        return streaming.CachedZChunks.TryGetValue(chunk, out var cached) && cached > deadline
            ? cached
            : deadline;
    }

    private TimeSpan GetClassicOrdinaryUnloadDeadline(ClassicBiomeStreamingComponent streaming)
    {
        var delay = streaming.UnloadDelay < TimeSpan.Zero ? TimeSpan.Zero : streaming.UnloadDelay;
        return _classicTiming.CurTime + delay;
    }

    private void PruneClassicZLevelCache(ClassicBiomeStreamingComponent streaming)
    {
        _classicStaleUnloadChunks.Clear();
        foreach (var (chunk, deadline) in streaming.CachedZChunks)
        {
            if (deadline <= _classicTiming.CurTime)
                _classicStaleUnloadChunks.Add(chunk);
        }

        foreach (var chunk in _classicStaleUnloadChunks)
            RemoveClassicZLevelCache(streaming, chunk);

        var maxAdjacent = streaming.AdjacentPrefetchCacheDuration <= TimeSpan.Zero
            ? 0
            : Math.Min(
                Math.Clamp(streaming.MaxAdjacentPrefetchCachedChunks, 0, MaxClassicZLevelCachedChunks),
                Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxClassicZLevelCachedChunks));
        _classicZCacheBatch.Clear();
        while (streaming.CachedAdjacentPrefetchChunks.Count > maxAdjacent &&
               TryEvictClassicAdjacentPrefetchCache(streaming, _classicZCacheBatch))
        {
        }
        _classicZCacheBatch.Clear();
        TrimClassicZLevelCache(streaming);
    }

    private void TrimClassicZLevelCache(ClassicBiomeStreamingComponent streaming)
    {
        var maxChunks = Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxClassicZLevelCachedChunks);
        while (streaming.CachedZChunks.Count > maxChunks)
        {
            if (!TryEvictClassicZLevelCache(streaming))
                break;
        }

        TrimClassicDictionary(streaming.CachedZChunks, maxChunks);
    }

    private bool TryEvictClassicZLevelCache(
        ClassicBiomeStreamingComponent streaming,
        HashSet<Vector2i>? retained = null)
    {
        var found = false;
        var oldestChunk = default(Vector2i);
        var oldestDeadline = TimeSpan.MaxValue;
        foreach (var (chunk, deadline) in streaming.CachedZChunks)
        {
            if (retained?.Contains(chunk) == true ||
                found && (deadline > oldestDeadline ||
                          deadline == oldestDeadline && CompareClassicChunks(chunk, oldestChunk) >= 0))
            {
                continue;
            }

            found = true;
            oldestChunk = chunk;
            oldestDeadline = deadline;
        }

        if (!found)
            return false;

        RemoveClassicZLevelCache(streaming, oldestChunk);
        return true;
    }

    private void RemoveClassicZLevelCache(ClassicBiomeStreamingComponent streaming, Vector2i chunk)
    {
        streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
        if (!streaming.CachedZChunks.Remove(chunk, out var cacheDeadline) ||
            !streaming.PendingUnloads.TryGetValue(chunk, out var pending) ||
            pending != cacheDeadline)
        {
            return;
        }

        var ordinaryDeadline = GetClassicOrdinaryUnloadDeadline(streaming);
        if (pending > ordinaryDeadline)
            streaming.PendingUnloads[chunk] = ordinaryDeadline;
    }

    private static void TrimClassicDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> dictionary,
        int minimumCapacity)
        where TKey : notnull
    {
        var capacity = dictionary.EnsureCapacity(0);
        var target = Math.Max(minimumCapacity, dictionary.Count);
        if (capacity <= Math.Max(4L, (long) target * 8L))
            return;

        if (target == 0)
            dictionary.TrimExcess();
        else
            dictionary.TrimExcess(target);
    }

    private bool TryGetClassicOpeningWorldBounds(
        EntityUid gridUid,
        BiomeComponent biome,
        MapGridComponent grid,
        Box2 localBounds,
        out Box2 openingWorldBounds)
    {
        return TryGetClassicOpeningWorldBoundsState(
            gridUid,
            biome,
            grid,
            localBounds,
            out openingWorldBounds,
            out _);
    }

    private bool TryGetClassicOpeningWorldBoundsState(
        EntityUid gridUid,
        BiomeComponent biome,
        MapGridComponent grid,
        Box2 localBounds,
        out Box2 openingWorldBounds,
        out bool pending)
    {
        if (!_classicStreamingQuery.TryComp(gridUid, out var streaming))
        {
            pending = false;
            return TryGetClassicOpeningWorldBoundsAtomic(gridUid, biome, grid, localBounds, out openingWorldBounds);
        }

        IncludeClassicStreamingBudget(streaming);
        pending = false;

        var requiredFirstChunk = (localBounds.BottomLeft / ChunkSize).Floored();
        var requiredLastChunk = (localBounds.TopRight / ChunkSize).Floored();
        var exactFirstTile = new Vector2i(
            (int) MathF.Floor(localBounds.Left) - 1,
            (int) MathF.Floor(localBounds.Bottom) - 1);
        var exactLastTile = new Vector2i(
            (int) MathF.Floor(localBounds.Right) + 1,
            (int) MathF.Floor(localBounds.Top) + 1);
        var openingChunkSize = _classicOpeningCache.ChunkSize;
        var firstOpeningChunk = SharedMapSystem.GetChunkIndices(exactFirstTile, openingChunkSize);
        var lastOpeningChunk = SharedMapSystem.GetChunkIndices(exactLastTile, openingChunkSize);
        var openingChunkWidth = (long) lastOpeningChunk.X - firstOpeningChunk.X + 1L;
        var openingChunkHeight = (long) lastOpeningChunk.Y - firstOpeningChunk.Y + 1L;
        if (openingChunkWidth <= 0L ||
            openingChunkHeight <= 0L ||
            openingChunkWidth > MaxClassicOpeningSnapshotChunks ||
            openingChunkHeight > MaxClassicOpeningSnapshotChunks ||
            openingChunkWidth * openingChunkHeight > MaxClassicOpeningSnapshotChunks)
        {
            openingWorldBounds = default;
            return false;
        }

        var firstTile = firstOpeningChunk * openingChunkSize;
        var lastTile = (lastOpeningChunk + Vector2i.One) * openingChunkSize - Vector2i.One;
        var key = new ClassicBiomeOpeningScanKey(
            requiredFirstChunk,
            requiredLastChunk,
            firstTile,
            lastTile);
        var revision = _classicOpeningCache.GetRevision((gridUid, grid));

        ClassicBiomeOpeningScanState? scan = null;
        if (streaming.OpeningScans.TryGetValue(key, out var cachedScan))
        {
            if (cachedScan.Revision == revision)
                scan = cachedScan;
            else
                streaming.OpeningScans.Remove(key);
        }

        if (scan == null)
        {
            if (!biome.LoadedChunks.Contains(requiredFirstChunk * ChunkSize))
            {
                pending = true;
                openingWorldBounds = default;
                return false;
            }

            scan = new ClassicBiomeOpeningScanState(
                requiredFirstChunk,
                requiredLastChunk,
                firstTile,
                lastTile,
                revision,
                streaming.OpeningScanGeneration,
                _classicTiming.CurTime);
            scan.NextOpeningChunk = firstOpeningChunk;
            if (!TryAdmitClassicOpeningScan(streaming, key, scan))
            {
                pending = true;
                openingWorldBounds = default;
                return false;
            }
        }

        scan.LastRequestGeneration = streaming.OpeningScanGeneration;
        scan.LastRequestTime = _classicTiming.CurTime;
        if (scan.Phase == ClassicBiomeOpeningScanPhase.Complete)
            return TryGetClassicOpeningSnapshotBounds(
                gridUid,
                grid,
                scan,
                exactFirstTile,
                exactLastTile,
                openingChunkSize,
                out openingWorldBounds);

        pending = true;

        if (scan.Phase == ClassicBiomeOpeningScanPhase.WaitingForChunks &&
            scan.NextChunk.X <= scan.RequiredLastChunk.X &&
            !biome.LoadedChunks.Contains(scan.NextChunk * ChunkSize))
        {
            openingWorldBounds = default;
            return false;
        }

        if (!TryBeginClassicOpeningOperation(out var forced))
        {
            openingWorldBounds = default;
            return false;
        }

        var maxCells = Math.Clamp(streaming.OpeningCellsPerSlice, 1, ClassicChunkCells);
        var processed = 0;

        while (scan.Phase == ClassicBiomeOpeningScanPhase.WaitingForChunks &&
               scan.NextChunk.X <= scan.RequiredLastChunk.X)
        {
            var chunkOrigin = scan.NextChunk * ChunkSize;
            if (!biome.LoadedChunks.Contains(chunkOrigin))
            {
                openingWorldBounds = default;
                return false;
            }

            var hasNext = AdvanceClassicOpeningCursor(
                ref scan.NextChunk,
                scan.RequiredFirstChunk,
                scan.RequiredLastChunk);
            processed++;
            if (!hasNext)
                scan.Phase = ClassicBiomeOpeningScanPhase.Scanning;

            if (processed >= maxCells ||
                forced ||
                ClassicStreamingBudgetExpired())
            {
                openingWorldBounds = default;
                return false;
            }
        }

        if (scan.Phase != ClassicBiomeOpeningScanPhase.Scanning ||
            scan.NextOpeningChunk.X > lastOpeningChunk.X)
        {
            openingWorldBounds = default;
            return false;
        }

        while (scan.NextOpeningChunk.X <= lastOpeningChunk.X && processed < maxCells)
        {
            if (processed > 0 && (forced || ClassicStreamingBudgetExpired()))
                break;

            var openingChunk = scan.NextOpeningChunk;
            var openingTile = openingChunk * openingChunkSize;
            var guardedChunk = SharedMapSystem.GetChunkIndices(openingTile, ChunkSize) * ChunkSize;
            var openingMask = 0UL;
            var cached = true;
            if (biome.LoadedChunks.Contains(guardedChunk))
            {
                cached = _classicOpeningCache.TryGetCachedChunkOpeningMask(
                    (gridUid, grid),
                    openingChunk,
                    out openingMask);
                if (!cached)
                {
                    if (processed > 0)
                        break;

                    if (!_classicOpeningCache.TryGetChunkOpeningMask(
                            (gridUid, grid),
                            openingChunk,
                            _mapSystem,
                            TileDefManager,
                            out openingMask))
                    {
                        streaming.OpeningScans.Remove(key);
                        openingWorldBounds = default;
                        return false;
                    }
                }
            }

            scan.OpeningChunkMasks[openingChunk] = openingMask;
            var hasNextOpeningChunk = AdvanceClassicOpeningCursor(
                ref scan.NextOpeningChunk,
                firstOpeningChunk,
                lastOpeningChunk);
            processed += cached ? 1 : maxCells;

            if (_classicOpeningCache.GetRevision((gridUid, grid)) != scan.Revision)
            {
                streaming.OpeningScans.Remove(key);
                openingWorldBounds = default;
                return false;
            }

            if (!hasNextOpeningChunk)
            {
                scan.Phase = ClassicBiomeOpeningScanPhase.Complete;
                pending = false;
                return TryGetClassicOpeningSnapshotBounds(
                    gridUid,
                    grid,
                    scan,
                    exactFirstTile,
                    exactLastTile,
                    openingChunkSize,
                    out openingWorldBounds);
            }
        }

        openingWorldBounds = default;
        return false;
    }

    private bool TryGetClassicOpeningSnapshotBounds(
        EntityUid gridUid,
        MapGridComponent grid,
        ClassicBiomeOpeningScanState scan,
        Vector2i exactFirstTile,
        Vector2i exactLastTile,
        int openingChunkSize,
        out Box2 openingWorldBounds)
    {
        var gridWorldMatrix = _transform.GetWorldMatrix(gridUid);
        var tileSize = grid.TileSize;
        var queryWorldBounds = gridWorldMatrix.TransformBox(new Box2(
            (Vector2) exactFirstTile * tileSize, (Vector2) (exactLastTile + Vector2i.One) * tileSize));
        var coveringGrids = _classicOpeningGrids;
        coveringGrids.Clear();
        _mapSystem.FindGridsIntersecting(Transform(gridUid).MapID, queryWorldBounds,
            ref coveringGrids, approx: true, includeMap: true);
        var checkCoverage = coveringGrids.Count > 1;

        if (!checkCoverage && scan.ExactQueryCached &&
            scan.ExactQueryFirstTile == exactFirstTile &&
            scan.ExactQueryLastTile == exactLastTile)
        {
            return TransformResult(out openingWorldBounds);
        }

        var firstOpeningChunk = SharedMapSystem.GetChunkIndices(exactFirstTile, openingChunkSize);
        var lastOpeningChunk = SharedMapSystem.GetChunkIndices(exactLastTile, openingChunkSize);
        var found = false;
        var firstOpeningTile = new Vector2i(int.MaxValue, int.MaxValue);
        var lastOpeningTile = new Vector2i(int.MinValue, int.MinValue);
        for (var x = firstOpeningChunk.X; x <= lastOpeningChunk.X; x++)
        for (var y = firstOpeningChunk.Y; y <= lastOpeningChunk.Y; y++)
        {
            var chunk = new Vector2i(x, y);
            if (!scan.OpeningChunkMasks.TryGetValue(chunk, out var openingMask) || openingMask == 0)
                continue;

            var chunkOrigin = chunk * openingChunkSize;
            var from = Vector2i.ComponentMax(exactFirstTile, chunkOrigin);
            var to = Vector2i.ComponentMin(
                exactLastTile,
                chunkOrigin + new Vector2i(openingChunkSize - 1, openingChunkSize - 1));
            var width = to.X - from.X + 1;
            var rowMask = ((1UL << width) - 1UL) << (from.X - chunkOrigin.X);
            var queryMask = 0UL;
            for (var tileY = from.Y; tileY <= to.Y; tileY++)
                queryMask |= rowMask << ((tileY - chunkOrigin.Y) * openingChunkSize);

            var matching = openingMask & queryMask;
            while (matching != 0)
            {
                var bit = BitOperations.TrailingZeroCount(matching);
                var tile = chunkOrigin + new Vector2i(bit % openingChunkSize, bit / openingChunkSize);
                matching &= matching - 1UL;
                if (checkCoverage && ClassicZLevelOpeningCache.IsOpeningCovered(
                        (gridUid, grid), tile, gridWorldMatrix, coveringGrids,
                        _mapSystem, _transform, TileDefManager))
                {
                    continue;
                }

                firstOpeningTile = Vector2i.ComponentMin(firstOpeningTile, tile);
                lastOpeningTile = Vector2i.ComponentMax(lastOpeningTile, tile);
                found = true;
            }
        }

        // The raw snapshot remains reusable. Coverage is checked against live grids, so a moved
        // or damaged prefab floor cannot leave a stale filtered result in this query cache.
        scan.ExactQueryCached = !checkCoverage;
        scan.ExactQueryFirstTile = exactFirstTile;
        scan.ExactQueryLastTile = exactLastTile;
        scan.ExactQueryHasOpening = found;
        scan.ExactQueryFirstOpeningTile = firstOpeningTile;
        scan.ExactQueryLastOpeningTile = lastOpeningTile;
        return TransformResult(out openingWorldBounds);

        bool TransformResult(out Box2 result)
        {
            if (!scan.ExactQueryHasOpening)
            {
                result = default;
                return false;
            }

            var localMetres = new Box2(
                (Vector2) scan.ExactQueryFirstOpeningTile * tileSize,
                (Vector2) (scan.ExactQueryLastOpeningTile + Vector2i.One) * tileSize);
            result = gridWorldMatrix.TransformBox(localMetres);
            return true;
        }
    }

    private static bool AdvanceClassicOpeningCursor(
        ref Vector2i cursor,
        Vector2i first,
        Vector2i last)
    {
        if (cursor.Y < last.Y)
        {
            cursor = new Vector2i(cursor.X, cursor.Y + 1);
            return true;
        }

        cursor = new Vector2i(cursor.X + 1, first.Y);
        return cursor.X <= last.X;
    }

    private bool TryGetClassicOpeningWorldBoundsAtomic(
        EntityUid gridUid,
        BiomeComponent biome,
        MapGridComponent grid,
        Box2 localBounds,
        out Box2 openingWorldBounds)
    {
        var requiredChunks = new ChunkIndicesEnumerator(localBounds, ChunkSize);
        while (requiredChunks.MoveNext(out var chunkOrigin))
        {
            if (!biome.LoadedChunks.Contains(chunkOrigin.Value * ChunkSize))
            {
                openingWorldBounds = default;
                return false;
            }
        }

        var firstTile = new Vector2i(
            (int) MathF.Floor(localBounds.Left) - 1,
            (int) MathF.Floor(localBounds.Bottom) - 1);
        var lastTile = new Vector2i(
            (int) MathF.Floor(localBounds.Right) + 1,
            (int) MathF.Floor(localBounds.Top) + 1);
        var gridWorldMatrix = _transform.GetWorldMatrix(gridUid);
        var worldBounds = gridWorldMatrix.TransformBox(new Box2(
            (Vector2) firstTile * grid.TileSize, (Vector2) (lastTile + Vector2i.One) * grid.TileSize));
        var mapId = Transform(gridUid).MapID;
        var coveringGrids = _classicOpeningGrids;
        coveringGrids.Clear();
        _mapSystem.FindGridsIntersecting(mapId, worldBounds, ref coveringGrids,
            approx: true, includeMap: true);
        Func<Vector2i, bool>? includeOpening = coveringGrids.Count > 1
            ? tile => !ClassicZLevelOpeningCache.IsOpeningCovered((gridUid, grid), tile,
                gridWorldMatrix, coveringGrids, _mapSystem, _transform, TileDefManager)
            : null;

        var firstChunk = SharedMapSystem.GetChunkIndices(firstTile, ChunkSize);
        var lastChunk = SharedMapSystem.GetChunkIndices(lastTile, ChunkSize);
        var found = false;
        var combinedLocalBounds = default(Box2);

        for (var chunkX = firstChunk.X; chunkX <= lastChunk.X; chunkX++)
        for (var chunkY = firstChunk.Y; chunkY <= lastChunk.Y; chunkY++)
        {
            var chunk = new Vector2i(chunkX, chunkY);
            var chunkOrigin = chunk * ChunkSize;
            if (!biome.LoadedChunks.Contains(chunkOrigin))
                continue;

            var chunkLast = chunkOrigin + new Vector2i(ChunkSize - 1, ChunkSize - 1);
            var searchStart = Vector2i.ComponentMax(firstTile, chunkOrigin);
            var searchEnd = Vector2i.ComponentMin(lastTile, chunkLast);
            if (!_classicOpeningCache.TryGetOpeningBounds(
                    (gridUid, grid),
                    searchStart,
                    searchEnd,
                    _mapSystem,
                    TileDefManager,
                    out var localOpeningBounds,
                    includeOpening))
            {
                continue;
            }

            combinedLocalBounds = found
                ? combinedLocalBounds.Union(localOpeningBounds)
                : localOpeningBounds;
            found = true;
        }

        if (!found)
        {
            openingWorldBounds = default;
            return false;
        }

        openingWorldBounds = _transform.GetWorldMatrix(gridUid).TransformBox(combinedLocalBounds);
        return true;
    }

    private bool TryGetClassicMapBiome(
        EntityUid mapUid,
        Vector2 worldPos,
        out EntityUid gridUid,
        [NotNullWhen(true)] out BiomeComponent? biome,
        [NotNullWhen(true)] out MapGridComponent? grid)
    {
        if (_biomeQuery.TryComp(mapUid, out biome) &&
            TryComp<MapGridComponent>(mapUid, out grid))
        {
            gridUid = mapUid;
            return true;
        }

        if (_mapSystem.TryFindGridAt(mapUid, worldPos, out var containingGridUid, out var containingGrid) &&
            _biomeQuery.TryComp(containingGridUid, out biome))
        {
            gridUid = containingGridUid;
            grid = containingGrid;
            return true;
        }

        if (TryComp<MapComponent>(mapUid, out var map))
        {
            foreach (var candidateGrid in _mapSystem.GetAllGrids(map.MapId))
            {
                if (_biomeQuery.TryComp(candidateGrid.Owner, out biome))
                {
                    gridUid = candidateGrid.Owner;
                    grid = candidateGrid.Comp;
                    return true;
                }
            }
        }

        gridUid = default;
        biome = null;
        grid = null;
        return false;
    }

    private void OnClassicTileChanged(ref TileChangedEvent args)
    {
        _classicOpeningCache.InvalidateTiles(args.Entity, args.Changes);
        if (_classicStreamingQuery.TryComp(args.Entity.Owner, out var streaming) &&
            streaming.OpeningScans.Count > 0)
        {
            if (args.Changes.Length == 0)
            {
                streaming.OpeningScans.Clear();
            }
            else
            {
                var revision = _classicOpeningCache.GetRevision(args.Entity);
                _classicStaleOpeningScans.Clear();
                foreach (var (key, scan) in streaming.OpeningScans)
                {
                    var invalid = false;
                    foreach (var change in args.Changes)
                    {
                        var tile = change.GridIndices;
                        if (tile.X < scan.FirstTile.X || tile.X > scan.LastTile.X ||
                            tile.Y < scan.FirstTile.Y || tile.Y > scan.LastTile.Y)
                        {
                            continue;
                        }

                        invalid = true;
                        break;
                    }

                    if (invalid)
                        _classicStaleOpeningScans.Add(key);
                    else
                        scan.Revision = revision;
                }

                foreach (var key in _classicStaleOpeningScans)
                    streaming.OpeningScans.Remove(key);
            }
        }

        if (!_biomeQuery.TryComp(args.Entity.Owner, out var biome))
            return;

        if (_classicBiomeTileWrites.Contains(args.Entity.Owner))
            return;

        foreach (var change in args.Changes)
        {
            var chunkOrigin = SharedMapSystem.GetChunkIndices(change.GridIndices, ChunkSize) * ChunkSize;
            biome.ModifiedTiles.GetOrNew(chunkOrigin).Add(change.GridIndices);
        }
    }

    private void SetClassicBiomeTile(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        Tile tile)
    {
        _classicBiomeTileWrites.Add(gridUid);
        try
        {
            _mapSystem.SetTile(gridUid, grid, indices, tile);
        }
        finally
        {
            _classicBiomeTileWrites.Remove(gridUid);
        }
    }

    private void SetClassicBiomeTiles(
        EntityUid gridUid,
        MapGridComponent grid,
        List<(Vector2i Index, Tile Tile)> tiles)
    {
        _classicBiomeTileWrites.Add(gridUid);
        try
        {
            _mapSystem.SetTiles(gridUid, grid, tiles);
        }
        finally
        {
            _classicBiomeTileWrites.Remove(gridUid);
        }
    }

    private bool ClassicBiomeLoadsEntities(EntityUid gridUid)
    {
        return !_classicUndergroundQuery.TryComp(gridUid, out var underground) || underground.LoadEntities;
    }

    private bool ClassicBiomeLoadsDecals(EntityUid gridUid)
    {
        return !_classicUndergroundQuery.TryComp(gridUid, out var underground) || underground.LoadDecals;
    }

    private int ClassicBiomeDepth(EntityUid gridUid)
    {
        return _classicZMapQuery.TryComp(Transform(gridUid).MapUid, out var zMap) ? zMap.Depth : 0;
    }

    private bool ClassicHasBlockingAnchoredEntity(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var entity))
        {
            if (Deleted(entity.Value) || EntityManager.IsQueuedForDeletion(entity.Value) ||
                HasComp<TimedDespawnComponent>(entity.Value))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool ClassicCanUnloadTrackedBiomeEntity(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        EntityUid uid,
        int depth)
    {
        var metadata = MetaData(uid);
        var rootAnchor = metadata.EntityPrototype is { } prototype &&
                         prototype.Components.TryGetValue("Transform", out var transformEntry) &&
                         transformEntry.Component is TransformComponent prototypeTransform &&
                         !prototypeTransform.Anchored && Transform(uid).Anchored &&
                         TryGetEntity(tile, biome, (gridUid, grid), out var rootPrototype) &&
                         prototype.ID == rootPrototype;
        return ClassicCanUnloadBiomeEntity(gridUid, uid, depth, rootAnchor);
    }

    private bool ClassicCanUnloadMarkerEntity(
        ClassicBiomeMarkerCellState marker,
        EntityUid gridUid,
        EntityUid uid,
        int depth)
    {
        if (marker.Root != uid || marker.BaselineComponents == null)
            return ClassicCanUnloadBiomeEntity(gridUid, uid, depth, strippedGhostRole: uid == marker.Root);

        if (marker.BaselineComponentNames == null || marker.BaselineChildren == null ||
            marker.BaselineFactions == null || marker.BaselineTags == null ||
            marker.BaselineSolutionContainers == null)
        {
            return false;
        }

        if (!TryComp<ClassicBiomeAmbientMarkerComponent>(uid, out var ambient) || ambient.PlayerTouched ||
            HasComp<ActorComponent>(uid) ||
            TryComp<MindContainerComponent>(uid, out var mind) && (mind.HasMind || mind.Mind != null) ||
            _classicDamageableQuery.TryComp(uid, out var damageable) && damageable.TotalDamage != 0 ||
            TryComp<MobStateComponent>(uid, out var mobState) && mobState.CurrentState != MobState.Alive)
        {
            return false;
        }

        var metadata = MetaData(uid);
        if (metadata.EntityPrototype?.ID != marker.Prototype ||
            metadata.EntityName != marker.BaselineName ||
            metadata.EntityDescription != marker.BaselineDescription)
        {
            return false;
        }

        CollectClassicMarkerChildren(uid, _classicMarkerChildren);
        if (!_classicMarkerChildren.SetEquals(marker.BaselineChildren))
            return false;
        CaptureClassicMarkerFactions(uid, _classicMarkerFactions);
        if (!_classicMarkerFactions.SetEquals(marker.BaselineFactions))
            return false;
        CaptureClassicMarkerTags(uid, _classicMarkerTags);
        if (!_classicMarkerTags.SetEquals(marker.BaselineTags))
            return false;
        _classicMarkerSolutionContainers.Clear();
        if (TryComp<SolutionContainerManagerComponent>(uid, out var solutions))
        {
            _classicMarkerSolutionContainers.UnionWith(solutions.Containers);
            if (solutions.Solutions != null)
                return false;
        }
        if (!_classicMarkerSolutionContainers.SetEquals(marker.BaselineSolutionContainers))
            return false;

        var baseline = marker.BaselineComponents;
        var componentNames = marker.BaselineComponentNames;
        var remaining = componentNames.Count;
        foreach (var component in AllComps(uid))
        {
            if (component.Deleted)
                return false;
            if (component is MetaDataComponent or TransformComponent or ClassicBiomeAmbientMarkerComponent)
                continue;

            var type = component.GetType();
            var name = EntityManager.ComponentFactory.GetRegistration(type).Name;
            if (name == "ActiveNPC")
                continue;
            if (!componentNames.Contains(name))
                return false;

            remaining--;
            if (!baseline.TryGetValue(name, out var expected))
                continue;
            if (component is TemperatureComponent temperature && expected is TemperatureComponent initialTemperature)
            {
                if (TryComp<TemperatureDamageComponent>(uid, out var temperatureDamage) &&
                    (temperature.CurrentTemperature < temperatureDamage.ColdDamageThreshold ||
                     temperature.CurrentTemperature > temperatureDamage.HeatDamageThreshold))
                    return false;

                initialTemperature.CurrentTemperature = temperature.CurrentTemperature;
            }
            else if (component is TemperatureDamageComponent temperatureDamage &&
                     expected is TemperatureDamageComponent initialTemperatureDamage)
            {
                if (temperatureDamage.TakingDamage)
                    return false;

                initialTemperatureDamage.LastUpdate = temperatureDamage.LastUpdate;
            }
            else if (component is FlammableComponent flammable && expected is FlammableComponent initialFlammable)
            {
                initialFlammable.NextUpdate = flammable.NextUpdate;
            }
            else if (component is PerishableComponent perishable &&
                     (perishable.RotAccumulator != TimeSpan.Zero || perishable.Stage != 0 || perishable.ForceRotProgression))
            {
                return false;
            }

            ClassicSynchronizeMarkerSnapshotClocks(component, expected);
            if (!_classicSerialization.DataFieldEquals(type, component, expected))
                return false;
        }

        return remaining == 0;
    }

#pragma warning disable RA0002
    private static void ClassicSynchronizeMarkerSnapshotClocks(IComponent component, IComponent snapshot)
    {
        switch (component, snapshot)
        {
            case (ThermalRegulatorComponent current, ThermalRegulatorComponent baseline):
                baseline.NextUpdate = current.NextUpdate;
                break;
            case (BloodstreamComponent current, BloodstreamComponent baseline):
                baseline.NextUpdate = current.NextUpdate;
                break;
            case (PerishableComponent current, PerishableComponent baseline):
                baseline.RotNextUpdate = current.RotNextUpdate;
                break;
        }
    }
#pragma warning restore RA0002

    private bool ClassicCanUnloadBiomeEntity(
        EntityUid gridUid,
        EntityUid uid,
        int depth,
        bool rootAnchor = false,
        bool strippedGhostRole = false)
    {
        if (_classicAlwaysUnloadQuery.HasComponent(uid))
            return true;

        if (!_classicStreamingQuery.HasComp(gridUid))
            return EntityManager.IsDefault(uid);

        if (_classicDamageableQuery.TryComp(uid, out var damageable) && damageable.TotalDamage != 0)
            return false;

        var metadata = MetaData(uid);
        if (metadata.EntityPrototype is not { } prototype ||
            metadata.EntityName != prototype.Name || metadata.EntityDescription != prototype.Description)
            return false;

        if (prototype.Components.TryGetValue("Transform", out var transformEntry) &&
            transformEntry.Component is TransformComponent prototypeTransform &&
            Transform(uid).Anchored != prototypeTransform.Anchored &&
            !(rootAnchor && Transform(uid).Anchored && !prototypeTransform.Anchored))
        {
            return false;
        }

        var remaining = prototype.Components.Count;
        if (prototype.Components.ContainsKey("Transform"))
            remaining--;
        if (prototype.Components.ContainsKey("MetaData"))
            remaining--;
        if (strippedGhostRole && prototype.Components.ContainsKey("GhostRole"))
            remaining--;
        if (strippedGhostRole && prototype.Components.ContainsKey("GhostTakeoverAvailable"))
            remaining--;

        foreach (var component in AllComps(uid))
        {
            if (component.Deleted)
                return false;
            if (component is TransformComponent or MetaDataComponent)
                continue;

            var type = component.GetType();
            var name = EntityManager.ComponentFactory.GetRegistration(type).Name;
            if (!prototype.Components.TryGetValue(name, out var entry))
                return false;
            remaining--;

            if (component is TagComponent tags && entry.Component is TagComponent prototypeTags)
            {
                if (tags.Tags.Count != prototypeTags.Tags.Count || !_tags.HasAllTags(tags, prototypeTags.Tags))
                    return false;
                continue;
            }

            if (component is Content.Server.Gatherable.Components.GatherableComponent gatherable &&
                entry.Component is Content.Server.Gatherable.Components.GatherableComponent prototypeGatherable)
            {
                if (gatherable.GatherOffset != prototypeGatherable.GatherOffset ||
                    !_classicSerialization.DataFieldEquals(gatherable.ToolWhitelist, prototypeGatherable.ToolWhitelist) ||
                    !_classicSerialization.DataFieldEquals(gatherable.Loot, prototypeGatherable.Loot) ||
                    !_classicSerialization.DataFieldEquals(gatherable.ToolQualities, prototypeGatherable.ToolQualities))
                {
                    return false;
                }

                continue;
            }

            if (component is RandomSpriteComponent randomSprite &&
                entry.Component is RandomSpriteComponent prototypeRandomSprite)
            {
                if (randomSprite.GetAllGroups != prototypeRandomSprite.GetAllGroups ||
                    !_classicSerialization.DataFieldEquals(randomSprite.Available, prototypeRandomSprite.Available) ||
                    !_classicSerialization.DataFieldEquals(randomSprite.HeldPrefixes, prototypeRandomSprite.HeldPrefixes) ||
                    prototypeRandomSprite.Selected.Count > 0 &&
                    !_classicSerialization.DataFieldEquals(randomSprite.Selected, prototypeRandomSprite.Selected))
                {
                    return false;
                }

                continue;
            }

            var expected = entry.Component;
            if (component is ClassicZPhysicsComponent && expected is ClassicZPhysicsComponent prototypePhysics)
            {
                var key = (prototype, depth);
                if (!_classicZPhysicsDefaults.TryGetValue(key, out var physics))
                {
                    physics = _classicSerialization.CreateCopy(prototypePhysics, notNullableOverride: true);
                    physics.CurrentZLevel = depth;
                    _classicZPhysicsDefaults[key] = physics;
                }
                expected = physics;
            }

            if (!_classicSerialization.DataFieldEquals(type, component, expected))
                return false;
        }

        return remaining == 0;
    }

    private bool ClassicCanUnloadBiomeEntity(EntityUid gridUid, EntityUid uid)
    {
        return ClassicCanUnloadBiomeEntity(gridUid, uid, ClassicBiomeDepth(gridUid));
    }

    private bool ClassicHasPersistentBiomeDependent(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        EntityUid biomeEntity)
    {
        if (!_classicGeyserOutletQuery.HasComp(biomeEntity))
            return false;

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var other))
        {
            if (other.Value != biomeEntity && _classicGeyserGeneratorQuery.HasComp(other.Value))
                return true;
        }

        return false;
    }

    private Dictionary<Vector2i, EntityUid> GetClassicReplaceableMarkerEntities(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Box2i bounds)
    {
        var result = new Dictionary<Vector2i, EntityUid>();
        var depth = ClassicBiomeDepth(gridUid);
        foreach (var loaded in biome.LoadedEntities.Values)
        {
            foreach (var (entity, tile) in loaded)
            {
                if (!bounds.Contains(tile) || Deleted(entity) ||
                    !_xformQuery.TryGetComponent(entity, out var xform) || !xform.Anchored ||
                    _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != tile ||
                    ClassicHasPersistentBiomeDependent(gridUid, grid, tile, entity) ||
                    !ClassicCanUnloadBiomeEntity(gridUid, entity, depth))
                {
                    continue;
                }

                result.TryAdd(tile, entity);
            }
        }

        return result;
    }

    private bool TryRemoveClassicMarkerBaseline(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        Vector2i node)
    {
        if (!biome.LoadedEntities.TryGetValue(chunk, out var loaded))
            return true;

        _classicUnloadEntities.Clear();
        foreach (var (entity, tile) in loaded)
        {
            if (tile != node)
                continue;
            _classicUnloadEntities.Add((entity, tile));
        }

        if (_classicUnloadEntities.Count == 0)
            return true;

        var depth = ClassicBiomeDepth(gridUid);
        foreach (var (baseline, _) in _classicUnloadEntities)
        {
            if (Deleted(baseline) || !_xformQuery.TryGetComponent(baseline, out var xform) ||
                _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != node ||
                ClassicHasPersistentBiomeDependent(gridUid, grid, node, baseline) ||
                !ClassicCanUnloadTrackedBiomeEntity(biome, gridUid, grid, node, baseline, depth))
            {
                return false;
            }
        }

        foreach (var (baseline, _) in _classicUnloadEntities)
        {
            Del(baseline);
            loaded.Remove(baseline);
        }

        if (loaded.Count == 0)
            biome.LoadedEntities.Remove(chunk);
        return true;
    }

    private bool TryGetActiveBiome(
        TransformComponent xform,
        Vector2 worldPos,
        out EntityUid gridUid,
        [NotNullWhen(true)] out BiomeComponent? biome,
        [NotNullWhen(true)] out MapGridComponent? grid)
    {
        if (xform.MapUid is { } mapUid &&
            _biomeQuery.TryGetComponent(mapUid, out biome) &&
            TryComp<MapGridComponent>(mapUid, out grid))
        {
            gridUid = mapUid;
            return true;
        }

        if (xform.GridUid is { } parentGridUid &&
            _biomeQuery.TryGetComponent(parentGridUid, out biome) &&
            TryComp<MapGridComponent>(parentGridUid, out grid))
        {
            gridUid = parentGridUid;
            return true;
        }

        if (xform.MapUid is { } fallbackMap &&
            TryGetClassicMapBiome(fallbackMap, worldPos, out gridUid, out biome, out grid))
        {
            return true;
        }

        gridUid = default;
        biome = null;
        grid = null;
        return false;
    }

    /// <summary>
    /// Compatibility overloads keep the upstream update fallback buildable. The Classic hook
    /// handles the frame first, but retaining the fallback makes future upstream merges local.
    /// </summary>
    private bool TryGetActiveBiome(
        TransformComponent xform,
        [NotNullWhen(true)] out BiomeComponent? biome)
    {
        return TryGetActiveBiome(
            xform,
            _transform.GetWorldPosition(xform),
            out _,
            out biome,
            out _);
    }

    private bool TryUpdateClassicBiomeStreaming(float frameTime)
    {
        var classicBiomes = EntityQueryEnumerator<ClassicBiomeStreamingComponent>();
        if (!classicBiomes.MoveNext(out _, out _))
            return false;

        var biomes = AllEntityQuery<BiomeComponent>();

        while (biomes.MoveNext(out var biome))
        {
            if (biome.LifeStage < ComponentLifeStage.Running)
                continue;

            _activeChunks.Add(biome, _tilePool.Get());
            if (_classicStreamingQuery.TryComp(biome.Owner, out var streaming))
            {
                streaming.ViewerChunks.Clear();
                streaming.ViewerCells.Clear();
                streaming.LandingChunks.Clear();
                streaming.AdjacentPrefetchChunks.Clear();
                streaming.ViewerCenters.Clear();
                unchecked
                {
                    streaming.OpeningScanGeneration++;
                    if (streaming.OpeningScanGeneration == 0)
                        streaming.OpeningScanGeneration++;
                }

            }
            if (biome.MarkerLayers.Count > 0 || biome.ForcedMarkerLayers.Count > 0)
                _markerChunks.GetOrNew(biome);
        }

        BeginClassicStreamingBudget();
        PrecollectClassicViewerStreamingBudgets();
        _classicStreamingWorkStarted = Stopwatch.GetTimestamp();
        _classicViewRequests.Clear();

        foreach (var pSession in Filter.GetAllPlayers(_playerManager))
        {
            if (pSession.Status != SessionStatus.InGame)
                continue;

            if (!_xformQuery.TryGetComponent(pSession.AttachedEntity, out var attachedXform) ||
                !CanLoad(pSession.AttachedEntity.Value))
            {
                continue;
            }

            var attachedBodyPos = _transform.GetWorldPosition(attachedXform);
            var attachedWorldPos = ClassicBiomeViewerPosition(
                pSession.AttachedEntity,
                pSession.AttachedEntity.Value,
                attachedXform);
            if (_handledEntities.Add(pSession.AttachedEntity.Value) &&
                TryGetActiveBiome(
                    attachedXform,
                    attachedBodyPos,
                    out var attachedGridUid,
                    out var attachedBiome,
                    out var attachedGrid) &&
                attachedBiome.Enabled)
            {
                AddChunksInRange(
                    attachedGridUid,
                    attachedGrid,
                    attachedBiome,
                    attachedWorldPos,
                    ClassicBiomeViewerScale(pSession.AttachedEntity.Value),
                    physicalViewer: pSession.AttachedEntity.Value,
                    physicalViewerXform: attachedXform,
                    physicalWorldPos: attachedBodyPos);

                foreach (var layer in attachedBiome.MarkerLayers)
                {
                    var layerProto = ProtoManager.Index(layer);
                    AddMarkerChunksInRange(
                        attachedGridUid,
                        attachedGrid,
                        attachedBiome,
                        attachedWorldPos,
                        layerProto);
                }
            }

            AddClassicAdjacentLandingChunks(pSession.AttachedEntity.Value, attachedXform, attachedBodyPos);
            AddClassicVehicleExitSafety(pSession.AttachedEntity.Value);

            foreach (var viewer in pSession.ViewSubscriptions)
                _classicViewRequests.Add((pSession, viewer, attachedXform));
        }

        var viewRequestCount = _classicViewRequests.Count;
        var viewRequestStart = viewRequestCount == 0
            ? 0
            : _classicViewRequestStartIndex % viewRequestCount;
        for (var i = 0; i < viewRequestCount; i++)
        {
            var (pSession, viewer, attachedXform) =
                _classicViewRequests[(viewRequestStart + i) % viewRequestCount];
            if (!_handledEntities.Add(viewer) ||
                !_xformQuery.TryGetComponent(viewer, out var viewerXform) ||
                !CanLoad(viewer))
            {
                continue;
            }

            var worldPos = ClassicBiomeViewerPosition(pSession.AttachedEntity, viewer, viewerXform);
            if (!TryGetActiveBiome(
                    viewerXform,
                    worldPos,
                    out var viewerGridUid,
                    out var viewerBiome,
                    out var viewerGrid) ||
                !viewerBiome.Enabled)
            {
                continue;
            }

            var pvsScale = ClassicBiomeViewerScale(viewer);
            if (!ClassicTryGetViewerRange(
                    pSession.AttachedEntity,
                    viewer,
                    attachedXform,
                    viewerXform,
                    worldPos,
                    pvsScale,
                    out _))
            {
                continue;
            }

            var activeLocalBounds = AddChunksInRange(
                viewerGridUid,
                viewerGrid,
                viewerBiome,
                worldPos,
                pvsScale);

            foreach (var layer in viewerBiome.MarkerLayers)
            {
                var layerProto = ProtoManager.Index(layer);
                AddMarkerChunksInBounds(viewerBiome, activeLocalBounds, layerProto);
            }
        }

        if (viewRequestCount > 0)
            _classicViewRequestStartIndex = (viewRequestStart + 1) % viewRequestCount;

        var loadBiomes = AllEntityQuery<BiomeComponent, MapGridComponent>();
        _classicStreamingBiomes.Clear();

        while (loadBiomes.MoveNext(out var gridUid, out var biome, out var grid))
        {
            if (biome.LifeStage < ComponentLifeStage.Running)
                continue;

            if (!biome.Enabled)
                continue;

            if (_classicStreamingQuery.TryComp(gridUid, out var streaming))
            {
                PruneClassicOpeningScans(streaming);
                _classicStreamingBiomes.Add((gridUid, biome, grid, streaming));
                continue;
            }

            LoadChunks(biome, gridUid, grid, biome.Seed);
            UnloadChunks(biome, gridUid, grid, biome.Seed);
        }

        ProcessClassicStreamingBiomes();

        _handledEntities.Clear();
        _classicHandledVehicleAnchors.Clear();
        _classicVehicleExitCoordinates.Clear();

        foreach (var tiles in _activeChunks.Values)
        {
            _tilePool.Return(tiles);
        }

        _activeChunks.Clear();
        _markerChunks.Clear();
        return true;
    }


    private Box2 AddChunksInRange(
        EntityUid gridUid,
        MapGridComponent grid,
        BiomeComponent biome,
        Vector2 worldPos,
        float pvsScale = 1f,
        EntityUid? physicalViewer = null,
        TransformComponent? physicalViewerXform = null,
        Vector2? physicalWorldPos = null,
        Box2? worldClip = null)
    {
        ClassicBiomeStreamingComponent? streaming = null;
        var loadArea = GetLocalBiomeViewBounds(gridUid, grid, worldPos, pvsScale, worldClip);

        if (_classicStreamingQuery.TryComp(biome.Owner, out streaming))
        {
            IncludeClassicStreamingBudget(streaming);
            var priorityTile = loadArea.Center.Floored();
            var viewerChunk = SharedMapSystem.GetChunkIndices(priorityTile, ChunkSize) * ChunkSize;
            if (!streaming.ViewerCenters.Contains(viewerChunk))
                streaming.ViewerCenters.Add(viewerChunk);
            if (physicalViewer is { } viewerUid && physicalViewerXform != null)
            {
                var tile = _mapSystem.WorldToTile(gridUid, grid, physicalWorldPos ?? worldPos);
                var physicalChunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
                streaming.ViewerChunks.Add(physicalChunk);
                AddClassicViewerSafetyCells(
                    viewerUid,
                    physicalViewerXform,
                    gridUid,
                    grid,
                    streaming,
                    tile);
            }
        }

        var enumerator = new ChunkIndicesEnumerator(loadArea, ChunkSize);

        while (enumerator.MoveNext(out var chunkOrigin))
        {
            var chunk = chunkOrigin.Value * ChunkSize;
            streaming?.AdjacentPrefetchChunks.Remove(chunk);
            _activeChunks[biome].Add(chunk);
        }

        return loadArea;
    }

    /// <summary>
    /// Materializes the physical cells a body can occupy before the next physics solve. A fixed
    /// 3x3 square made a stationary player synchronously initialize nine wall entities; the hard
    /// fixture footprint plus a short relative-velocity sweep normally needs only one or two.
    /// </summary>
    private void AddClassicViewerSafetyCells(
        EntityUid viewerUid,
        TransformComponent viewerXform,
        EntityUid gridUid,
        MapGridComponent grid,
        ClassicBiomeStreamingComponent streaming,
        Vector2i tile)
    {
        var safetyRadius = Math.Clamp(streaming.ViewerSafetyRadius, 0, 2);
        if (!_physicsQuery.TryComp(viewerUid, out var body) ||
            !_fixturesQuery.TryComp(viewerUid, out var fixtures))
        {
            AddFallback();
            return;
        }

        var relativeTransform = _physics.GetRelativePhysicsTransform(
            (viewerUid, viewerXform),
            (gridUid, (TransformComponent?) null));
        var foundHardFixture = false;
        var bounds = Box2.Empty;
        var fixtureRadius = 0f;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            for (var child = 0; child < fixture.Shape.ChildCount; child++)
            {
                var childBounds = fixture.Shape.ComputeAABB(relativeTransform, child);
                if (!childBounds.IsValid() || childBounds.HasNan())
                {
                    AddFallback();
                    return;
                }

                bounds = foundHardFixture ? bounds.Union(childBounds) : childBounds;
                foundHardFixture = true;
                fixtureRadius = MathF.Max(fixtureRadius, Vector2.Distance(relativeTransform.Position, childBounds.BottomLeft));
                fixtureRadius = MathF.Max(fixtureRadius, Vector2.Distance(relativeTransform.Position, childBounds.BottomRight));
                fixtureRadius = MathF.Max(fixtureRadius, Vector2.Distance(relativeTransform.Position, childBounds.TopLeft));
                fixtureRadius = MathF.Max(fixtureRadius, Vector2.Distance(relativeTransform.Position, childBounds.TopRight));
            }
        }

        if (!foundHardFixture)
        {
            AddFallback();
            return;
        }

        var predictionSeconds = (float) _classicTiming.TickPeriod.TotalSeconds *
                                Math.Clamp(streaming.ViewerPredictionTicks, 0, 4);
        var viewerVelocity = _physics.GetMapLinearVelocity(viewerUid, body, viewerXform);
        var gridVelocity = _physics.GetMapLinearVelocity(
            new EntityCoordinates(gridUid, relativeTransform.Position));
        var localVelocity = Vector2.TransformNormal(
            viewerVelocity - gridVelocity,
            _transform.GetInvWorldMatrix(gridUid));
        bounds = bounds.Union(bounds.Translated(localVelocity * predictionSeconds));

        var relativeAngularVelocity = _physics.GetMapAngularVelocity(viewerUid, body, viewerXform) -
                                      _physics.GetMapAngularVelocity(gridUid);
        var rotationMargin = fixtureRadius * MathF.Abs(relativeAngularVelocity) * predictionSeconds;
        var numericAndAccelerationMargin = Math.Clamp(streaming.ViewerSafetyMargin, 0f, grid.TileSize);
        bounds = bounds.Enlarged(rotationMargin + numericAndAccelerationMargin);

        const float tileEdgeEpsilon = 0.0001f;
        var firstX = (int) MathF.Floor((bounds.Left - tileEdgeEpsilon) / grid.TileSize);
        var firstY = (int) MathF.Floor((bounds.Bottom - tileEdgeEpsilon) / grid.TileSize);
        var lastX = (int) MathF.Ceiling((bounds.Right + tileEdgeEpsilon) / grid.TileSize) - 1;
        var lastY = (int) MathF.Ceiling((bounds.Top + tileEdgeEpsilon) / grid.TileSize) - 1;

        firstX = Math.Clamp(firstX, tile.X - safetyRadius, tile.X + safetyRadius);
        firstY = Math.Clamp(firstY, tile.Y - safetyRadius, tile.Y + safetyRadius);
        lastX = Math.Clamp(lastX, tile.X - safetyRadius, tile.X + safetyRadius);
        lastY = Math.Clamp(lastY, tile.Y - safetyRadius, tile.Y + safetyRadius);
        streaming.ViewerCells.Add(tile);
        for (var x = firstX; x <= lastX; x++)
        for (var y = firstY; y <= lastY; y++)
            streaming.ViewerCells.Add(new Vector2i(x, y));
        return;

        void AddFallback()
        {
            for (var x = -safetyRadius; x <= safetyRadius; x++)
            for (var y = -safetyRadius; y <= safetyRadius; y++)
                streaming.ViewerCells.Add(tile + new Vector2i(x, y));
        }
    }

    /// <summary>
    /// Matches PVS' grid handling: transform only the eye center into grid-local coordinates and
    /// build an axis-aligned square there. Transforming a world AABB would over-expand rotated
    /// grids and synchronously request chunks PVS itself cannot see.
    /// </summary>
    private Box2 GetLocalBiomeViewBounds(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2 worldPos,
        float pvsScale,
        Box2? worldClip = null)
    {
        var tileSize = MathF.Max(grid.TileSize, float.Epsilon);
        var inverse = _transform.GetInvWorldMatrix(gridUid);
        var localCenter = Vector2.Transform(worldPos, inverse) / tileSize;
        var range = _classicLoadRange * MathF.Max(pvsScale, 0.1f) / tileSize;
        var bounds = new Box2(-range, -range, range, range).Translated(localCenter);
        if (worldClip is not { } clip)
            return bounds;

        var localClipMetres = inverse.TransformBox(clip);
        var localClip = new Box2(
            localClipMetres.BottomLeft / tileSize,
            localClipMetres.TopRight / tileSize);
        return bounds.Intersect(localClip);
    }

    private void AddMarkerChunksInRange(
        EntityUid gridUid,
        MapGridComponent grid,
        BiomeComponent biome,
        Vector2 worldPos,
        IBiomeMarkerLayer layer)
    {
        var loadArea = new Box2(0, 0, layer.Size, layer.Size);
        var halfLayer = new Vector2(layer.Size / 2f);
        var tileSize = MathF.Max(grid.TileSize, float.Epsilon);
        var localPos = Vector2.Transform(worldPos, _transform.GetInvWorldMatrix(gridUid)) / tileSize;

        var enumerator = new ChunkIndicesEnumerator(loadArea.Translated(localPos - halfLayer), layer.Size);

        while (enumerator.MoveNext(out var chunkOrigin))
        {
            var lay = _markerChunks[biome].GetOrNew(layer.ID);
            lay.Add(chunkOrigin.Value * layer.Size);
        }
    }

    private void AddMarkerChunksInBounds(BiomeComponent biome, Box2 localBounds, IBiomeMarkerLayer layer)
    {
        var enumerator = new ChunkIndicesEnumerator(localBounds, layer.Size);
        while (enumerator.MoveNext(out var chunkOrigin))
        {
            var chunks = _markerChunks[biome].GetOrNew(layer.ID);
            chunks.Add(chunkOrigin.Value * layer.Size);
        }
    }

    private static bool ClassicMarkerRequestsComplete(
        Dictionary<string, HashSet<Vector2i>> requested,
        Dictionary<string, HashSet<Vector2i>> loaded,
        Dictionary<string, HashSet<Vector2i>> pinned)
    {
        foreach (var (layer, chunks) in requested)
        {
            loaded.TryGetValue(layer, out var completed);
            pinned.TryGetValue(layer, out var retained);
            foreach (var chunk in chunks)
            {
                if (completed?.Contains(chunk) != true && retained?.Contains(chunk) != true)
                    return false;
            }
        }

        return true;
    }

    private void PruneClassicMarkerAreas(
        BiomeComponent biome,
        ClassicBiomeStreamingComponent streaming,
        Dictionary<string, HashSet<Vector2i>>? requested)
    {
        _classicMarkerAreas.Clear();
        foreach (var (layer, markerChunks) in biome.LoadedMarkers)
        {
            var layerProto = ProtoManager.Index<BiomeMarkerLayerPrototype>(layer);
            foreach (var markerChunk in markerChunks)
            {
                if (requested != null && requested.TryGetValue(layer, out var active) &&
                    active.Contains(markerChunk))
                {
                    continue;
                }

                if (ClassicMarkerAreaHasResidentChunk(biome, streaming, markerChunk, layerProto.Size))
                    continue;

                _classicMarkerAreas.Add((layer, markerChunk));
            }
        }

        foreach (var (layer, markerChunk) in _classicMarkerAreas)
        {
            if (biome.LoadedMarkers.TryGetValue(layer, out var loaded))
            {
                loaded.Remove(markerChunk);
                if (loaded.Count == 0)
                    biome.LoadedMarkers.Remove(layer);
            }

            var layerProto = ProtoManager.Index<BiomeMarkerLayerPrototype>(layer);
            if (ClassicMarkerAreaHasModification(biome, markerChunk, layerProto.Size))
                streaming.PinnedMarkerAreas.GetOrNew(layer).Add(markerChunk);
            else
                RemoveClassicPendingMarkerArea(biome, layer, markerChunk, layerProto.Size);
        }
    }

    private static bool ClassicMarkerAreaHasModification(
        BiomeComponent biome,
        Vector2i markerChunk,
        int markerSize)
    {
        var first = SharedMapSystem.GetChunkIndices(markerChunk, ChunkSize) * ChunkSize;
        var lastTile = markerChunk + new Vector2i(markerSize - 1, markerSize - 1);
        var last = SharedMapSystem.GetChunkIndices(lastTile, ChunkSize) * ChunkSize;
        for (var x = first.X; x <= last.X; x += ChunkSize)
        for (var y = first.Y; y <= last.Y; y += ChunkSize)
        {
            if (!biome.ModifiedTiles.TryGetValue(new Vector2i(x, y), out var modified))
                continue;

            foreach (var tile in modified)
            {
                if (tile.X >= markerChunk.X && tile.Y >= markerChunk.Y &&
                    tile.X <= lastTile.X && tile.Y <= lastTile.Y)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ClassicMarkerAreaHasResidentChunk(
        BiomeComponent biome,
        ClassicBiomeStreamingComponent streaming,
        Vector2i markerChunk,
        int markerSize)
    {
        var first = SharedMapSystem.GetChunkIndices(markerChunk, ChunkSize) * ChunkSize;
        var lastTile = markerChunk + new Vector2i(markerSize - 1, markerSize - 1);
        var last = SharedMapSystem.GetChunkIndices(lastTile, ChunkSize) * ChunkSize;
        for (var x = first.X; x <= last.X; x += ChunkSize)
        for (var y = first.Y; y <= last.Y; y += ChunkSize)
        {
            var chunk = new Vector2i(x, y);
            if (biome.LoadedChunks.Contains(chunk) ||
                streaming.PartialLoads.ContainsKey(chunk) ||
                streaming.PartialUnloads.ContainsKey(chunk))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveClassicPendingMarkerArea(
        BiomeComponent biome,
        string layer,
        Vector2i markerChunk,
        int markerSize)
    {
        var first = SharedMapSystem.GetChunkIndices(markerChunk, ChunkSize) * ChunkSize;
        var lastTile = markerChunk + new Vector2i(markerSize - 1, markerSize - 1);
        var last = SharedMapSystem.GetChunkIndices(lastTile, ChunkSize) * ChunkSize;
        for (var x = first.X; x <= last.X; x += ChunkSize)
        for (var y = first.Y; y <= last.Y; y += ChunkSize)
        {
            var chunk = new Vector2i(x, y);
            if (!biome.PendingMarkers.TryGetValue(chunk, out var layers) ||
                !layers.TryGetValue(layer, out var nodes))
            {
                continue;
            }

            nodes.RemoveAll(node =>
                SharedMapSystem.GetChunkIndices(node, markerSize) * markerSize == markerChunk);
            if (nodes.Count == 0)
                layers.Remove(layer);
            if (layers.Count == 0)
                biome.PendingMarkers.Remove(chunk);
        }
    }

    /// <summary>
    /// Advances one deterministic Classic marker-area snapshot. The generic biome path above
    /// intentionally retains its original parallel, atomic <see cref="GetMarkerNodes"/> call.
    /// </summary>
    private void BuildClassicMarkerChunk(
        BiomeComponent component,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        Dictionary<string, HashSet<Vector2i>> markers,
        ClassicBiomeStreamingComponent streaming)
    {
        var scan = streaming.MarkerAreaScan;
        if (scan != null && !ClassicMarkerRequestExists(component, markers, scan, streaming))
        {
            DisposeClassicMarkerScan(scan);
            streaming.MarkerAreaScan = null;
            scan = null;
        }

        var continuation = scan != null;
        if (!TryBeginClassicBoundedOperation(continuation))
            return;

        if (scan == null)
        {
            var layerIndex = 0;
            foreach (var (layer, chunks) in markers)
            {
                layerIndex++;
                var found = false;
                var markerChunk = default(Vector2i);
                foreach (var candidate in chunks)
                {
                    if (component.LoadedMarkers.TryGetValue(layer, out var completed) && completed.Contains(candidate) ||
                        streaming.PinnedMarkerAreas.TryGetValue(layer, out var pinned) && pinned.Contains(candidate) ||
                        found && CompareClassicMarkerChunks(candidate, markerChunk) >= 0)
                    {
                        continue;
                    }

                    markerChunk = candidate;
                    found = true;
                }

                if (!found)
                    continue;

                var layerProto = ProtoManager.Index<BiomeMarkerLayerPrototype>(layer);
                var buffer = (int) (layerProto.Radius / 2f);
                var bounds = new Box2i(markerChunk + buffer, markerChunk + layerProto.Size - buffer);
                var count = (int) (bounds.Area / (layerProto.Radius * layerProto.Radius));
                count = Math.Min(count, layerProto.MaxCount);
                DebugTools.Assert(count > 0);

                scan = new ClassicBiomeMarkerAreaScanState(
                    layer,
                    markerChunk,
                    component.ForcedMarkerLayers.Contains(layer),
                    bounds,
                    count,
                    seed + markerChunk.X * ChunkSize + markerChunk.Y + layerIndex);
                streaming.MarkerAreaScan = scan;
                break;
            }
        }

        if (scan == null)
            return;

        var layerPrototype = ProtoManager.Index<BiomeMarkerLayerPrototype>(scan.Layer);
        var cellsPerSlice = Math.Clamp(streaming.MarkerAreaCellsPerSlice, 1, ClassicChunkCells);
        var totalCells = scan.Bounds.Width * scan.Bounds.Height;
        var processed = 0;
        var depth = ClassicBiomeDepth(gridUid);

        while (scan.Phase == ClassicBiomeMarkerAreaScanPhase.Scanning &&
               scan.NextCell < totalCells &&
               processed < cellsPerSlice)
        {
            var x = scan.NextCell / scan.Bounds.Height;
            var y = scan.NextCell % scan.Bounds.Height;
            var node = new Vector2i(scan.Bounds.Left + x, scan.Bounds.Bottom + y);
            ScanClassicMarkerNode(component, gridUid, grid, layerPrototype, scan, node, depth);
            scan.NextCell++;
            processed++;

            if (ClassicStreamingBudgetExpired())
                return;
        }

        if (scan.Phase == ClassicBiomeMarkerAreaScanPhase.Scanning)
        {
            if (scan.NextCell < totalCells)
                return;

            scan.Phase = ClassicBiomeMarkerAreaScanPhase.Selecting;
            if (processed >= cellsPerSlice || ClassicStreamingBudgetExpired())
                return;
        }

        if (!AdvanceClassicMarkerSelection(scan, layerPrototype, cellsPerSlice, ref processed))
            return;

        PublishClassicMarkerAreaScan(component, scan);
        DisposeClassicMarkerScan(scan);
        streaming.MarkerAreaScan = null;
    }

    private static void DisposeClassicMarkerScan(ClassicBiomeMarkerAreaScanState? scan)
    {
        if (scan is not { SeedPickActive: true })
            return;

        scan.SeedEnumerator.Dispose();
        scan.SeedPickActive = false;
    }

    private static bool ClassicMarkerRequestExists(
        BiomeComponent component,
        Dictionary<string, HashSet<Vector2i>> markers,
        ClassicBiomeMarkerAreaScanState scan,
        ClassicBiomeStreamingComponent streaming)
    {
        return markers.TryGetValue(scan.Layer, out var requested) &&
               requested.Contains(scan.MarkerChunk) &&
               (!streaming.PinnedMarkerAreas.TryGetValue(scan.Layer, out var pinned) ||
                !pinned.Contains(scan.MarkerChunk)) &&
               (!component.LoadedMarkers.TryGetValue(scan.Layer, out var completed) ||
                !completed.Contains(scan.MarkerChunk)) &&
               component.ForcedMarkerLayers.Contains(scan.Layer) == scan.Forced;
    }

    private static int CompareClassicMarkerChunks(Vector2i a, Vector2i b)
    {
        var result = a.X.CompareTo(b.X);
        return result != 0 ? result : a.Y.CompareTo(b.Y);
    }

    private void ScanClassicMarkerNode(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        BiomeMarkerLayerPrototype layerProto,
        ClassicBiomeMarkerAreaScanState scan,
        Vector2i node,
        int depth)
    {
        var replaceable = GetClassicReplaceableMarkerEntity(biome, gridUid, grid, node, depth);
        ClassicBiomeMarkerCellState? marker = null;
        var chunk = SharedMapSystem.GetChunkIndices(node, ChunkSize) * ChunkSize;
        if (_classicStreamingQuery.TryComp(gridUid, out var streaming) &&
            streaming.MarkerCells.TryGetValue(chunk, out var markerCells) &&
            markerCells.TryGetValue(node, out var candidateMarker) &&
            candidateMarker.Layer == scan.Layer && candidateMarker.MarkerChunk == scan.MarkerChunk)
        {
            marker = candidateMarker;
        }

        var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, node);
        EntityUid? existing = null;
        while (enumerator.MoveNext(out var anchored))
        {
            if (marker != null && marker.Entities.Contains(anchored.Value))
            {
                replaceable = anchored.Value;
                existing = anchored.Value;
                continue;
            }

            if (anchored == replaceable)
            {
                existing = anchored;
                continue;
            }

            existing = anchored;
            break;
        }

        if (!scan.Forced && existing != null && existing != replaceable)
            return;

        TryGetEntity(node, biome, (gridUid, grid), out var proto);
        if (layerProto.EntityMask.Count > 0 &&
            (proto == null || !layerProto.EntityMask.ContainsKey(proto)))
        {
            return;
        }

        if (proto != null && layerProto.Prototype != null)
            return;

        DebugTools.Assert(layerProto.EntityMask.Count == 0 || !string.IsNullOrEmpty(proto));
        scan.RemainingTiles.Add(node);
        scan.NodeEntities.Add(node, existing);
        scan.NodeMasks.Add(node, proto);
        if (replaceable != EntityUid.Invalid)
            scan.ReplaceableEntities.Add(node, replaceable);
    }

    private EntityUid GetClassicReplaceableMarkerEntity(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i node,
        int depth)
    {
        var chunk = SharedMapSystem.GetChunkIndices(node, ChunkSize) * ChunkSize;
        if (!biome.LoadedEntities.TryGetValue(chunk, out var loaded))
            return EntityUid.Invalid;

        foreach (var (entity, tile) in loaded)
        {
            if (tile != node || Deleted(entity) ||
                !_xformQuery.TryGetComponent(entity, out var xform) || !xform.Anchored ||
                _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != node ||
                ClassicHasPersistentBiomeDependent(gridUid, grid, node, entity) ||
                !ClassicCanUnloadBiomeEntity(gridUid, entity, depth))
            {
                continue;
            }

            return entity;
        }

        return EntityUid.Invalid;
    }

    /// <summary>
    /// Advances the seeded selection used by <see cref="GetMarkerNodes"/> without changing its
    /// random draw order. HashSet seed lookup is deliberately resumed through its enumerator:
    /// Enumerable-style random selection can otherwise hide an O(area) walk in a single tick.
    /// </summary>
    private bool AdvanceClassicMarkerSelection(
        ClassicBiomeMarkerAreaScanState scan,
        BiomeMarkerLayerPrototype layerProto,
        int maxOperations,
        ref int processed)
    {
        while (scan.GroupIndex < scan.Count)
        {
            if (processed >= maxOperations)
                return false;

            if (!scan.GroupStarted)
            {
                scan.GroupRemaining = scan.Random.Next(layerProto.MinGroupSize, layerProto.MaxGroupSize + 1);
                scan.GroupStarted = true;
                processed++;

                if (processed >= maxOperations || ClassicStreamingBudgetExpired())
                    return false;
            }

            if (scan.GroupRemaining <= 0 ||
                scan.Frontier.Count == 0 && scan.RemainingTiles.Count == 0)
            {
                if (scan.GroupRemaining > 0)
                    Log.Warning("Found remaining group size for ore veins!");

                scan.Frontier.Clear();
                scan.GroupRemaining = 0;
                scan.GroupStarted = false;
                scan.GroupIndex++;
                processed++;

                if (processed >= maxOperations || ClassicStreamingBudgetExpired())
                    return scan.GroupIndex >= scan.Count;

                continue;
            }

            if (scan.Frontier.Count == 0)
            {
                if (!scan.SeedPickActive)
                {
                    scan.SeedPickTarget = scan.Random.Next(scan.RemainingTiles.Count);
                    scan.SeedPickCursor = 0;
                    scan.SeedEnumerator = scan.RemainingTiles.GetEnumerator();
                    scan.SeedPickActive = true;
                }

                if (!scan.SeedEnumerator.MoveNext())
                {
                    DisposeClassicMarkerScan(scan);
                    throw new InvalidOperationException("Classic marker seed enumeration ended before its selected index.");
                }

                var startNode = scan.SeedEnumerator.Current;
                var selected = scan.SeedPickCursor++ == scan.SeedPickTarget;
                processed++;
                if (selected)
                {
                    DisposeClassicMarkerScan(scan);
                    scan.RemainingTiles.Remove(startNode);
                    scan.Frontier.Clear();
                    scan.Frontier.Add(startNode);
                }

                if (processed >= maxOperations || ClassicStreamingBudgetExpired())
                    return false;

                continue;
            }

            var frontierIndex = scan.Random.Next(scan.Frontier.Count);
            var node = scan.Frontier[frontierIndex];
            scan.Frontier.RemoveSwap(frontierIndex);
            scan.RemainingTiles.Remove(node);

            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                var neighbor = new Vector2i(node.X + x, node.Y + y);
                if (scan.Frontier.Contains(neighbor) || !scan.RemainingTiles.Contains(neighbor))
                    continue;

                scan.Frontier.Add(neighbor);
            }

            var chunkOrigin = SharedMapSystem.GetChunkIndices(node, ChunkSize) * ChunkSize;
            scan.PendingMarkers.GetOrNew(chunkOrigin).GetOrNew(scan.Layer).Add(node);
            scan.GroupRemaining--;

            if (scan.NodeEntities.TryGetValue(node, out var existing) &&
                existing is { } existingUid &&
                (!scan.ReplaceableEntities.TryGetValue(node, out var replaceable) || existingUid != replaceable))
            {
                scan.EntitiesToDelete.Add(existingUid);
            }

            processed++;
            if (processed >= maxOperations || ClassicStreamingBudgetExpired())
                return false;
        }

        return true;
    }

    /// <summary>
    /// Publishes a completed marker snapshot atomically. Until this point cancellation has no
    /// gameplay-visible effects, which is important because view requests are rebuilt each tick.
    /// </summary>
    private void PublishClassicMarkerAreaScan(
        BiomeComponent component,
        ClassicBiomeMarkerAreaScanState scan)
    {
        foreach (var entity in scan.EntitiesToDelete)
        {
            if (!Deleted(entity))
                Del(entity);
        }

        if (!component.LoadedMarkers.TryGetValue(scan.Layer, out var completed))
        {
            completed = new HashSet<Vector2i>();
            component.LoadedMarkers.Add(scan.Layer, completed);
        }
        completed.Add(scan.MarkerChunk);

        foreach (var (chunkOrigin, layers) in scan.PendingMarkers)
        {
            var destination = component.PendingMarkers.GetOrNew(chunkOrigin);
            foreach (var (layer, nodes) in layers)
                destination[layer] = nodes;
        }
    }

    /// <summary>
    /// Gets the marker nodes for the specified area.
    /// </summary>
    /// <param name="emptyTiles">Should we include empty tiles when determine markers (e.g. if they are yet to be loaded)</param>

    /// <summary>
    /// Applies at most <paramref name="maxNodes"/> precomputed marker nodes. Classic streaming
    /// uses the bounded form for already-loaded and background chunks; generic biomes retain the
    /// original all-at-once behavior through <see cref="LoadChunkMarkers"/>.
    /// </summary>
    private int LoadChunkMarkerNodes(
        BiomeComponent component,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        int seed,
        int maxNodes)
    {
        if (maxNodes <= 0 || !component.PendingMarkers.TryGetValue(chunk, out var layers))
            return 0;

        if (!component.ModifiedTiles.TryGetValue(chunk, out var modified))
        {
            modified = _tilePool.Get();
            component.ModifiedTiles.Add(chunk, modified);
        }

        var processed = 0;
        var classicStreaming = _classicStreamingQuery.TryComp(gridUid, out var streaming);
        _classicMarkerLayers.Clear();
        foreach (var (layer, nodes) in layers)
        {
            var layerProto = ProtoManager.Index<BiomeMarkerLayerPrototype>(layer);
            var count = Math.Min(nodes.Count, maxNodes - processed);
            var consumed = 0;

            while (consumed < count)
            {
                var node = nodes[consumed];
                if (!modified.Contains(node))
                {
                    var hasMarker = classicStreaming &&
                                    streaming!.MarkerCells.TryGetValue(chunk, out var existingMarkers) &&
                                    existingMarkers.ContainsKey(node);
                    if (!hasMarker)
                    {
                        if (classicStreaming &&
                            !TryRemoveClassicMarkerBaseline(component, gridUid, grid, chunk, node))
                        {
                            modified.Add(node);
                        }
                        else
                        {
                            if (TryGetBiomeTile(node, component.Layers, seed, (gridUid, grid), out var tile))
                            {
                                SetClassicBiomeTile(gridUid, grid, node, tile.Value);
                            }

                            string? prototype;

                            if (TryGetEntity(node, component, (gridUid, grid), out var proto) &&
                                layerProto.EntityMask.TryGetValue(proto, out var maskedProto))
                            {
                                prototype = maskedProto;
                            }
                            else
                            {
                                prototype = layerProto.Prototype;
                            }

                            if (classicStreaming && prototype != null)
                            {
                                var marker = new ClassicBiomeMarkerCellState
                                {
                                    Layer = layer,
                                    Prototype = prototype,
                                    MarkerChunk = SharedMapSystem.GetChunkIndices(node, layerProto.Size) * layerProto.Size,
                                };
                                var markerCells = streaming!.MarkerCells.GetOrNew(chunk);
                                markerCells[node] = marker;
                                component.LoadedEntities.TryGetValue(chunk, out var loaded);
                                try
                                {
                                    loaded = SpawnClassicBiomeMarkerEntity(
                                        prototype,
                                        gridUid,
                                        grid,
                                        node,
                                        loaded,
                                        marker);
                                }
                                catch
                                {
                                    markerCells.Remove(node);
                                    if (markerCells.Count == 0)
                                        streaming.MarkerCells.Remove(chunk);
                                    throw;
                                }

                                if (loaded is { Count: > 0 })
                                    component.LoadedEntities[chunk] = loaded;
                                else
                                    component.LoadedEntities.Remove(chunk);
                            }
                        }
                    }
                }

                consumed++;
                if (classicStreaming && ClassicStreamingBudgetExpired())
                    break;
            }

            if (consumed > 0)
            {
                nodes.RemoveRange(0, consumed);
                processed += consumed;
            }

            if (nodes.Count == 0)
                _classicMarkerLayers.Add(layer);
            if (processed >= maxNodes || classicStreaming && ClassicStreamingBudgetExpired())
                break;
        }

        foreach (var layer in _classicMarkerLayers)
            layers.Remove(layer);

        if (modified.Count == 0)
        {
            component.ModifiedTiles.Remove(chunk);
            _tilePool.Return(modified);
        }

        if (layers.Count == 0)
            component.PendingMarkers.Remove(chunk);

        return processed;
    }


    /// <summary>
    /// Loads a contiguous range of cells in a biome chunk. Classic background streaming uses
    /// this to keep a single 8x8 wall chunk from monopolizing one server tick.
    /// </summary>
    private int LoadChunkCells(
        BiomeComponent component,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        int seed,
        int startCell,
        int cellCount,
        bool stopWhenClassicBudgetExpires = false,
        int classicEntitySpawnLimit = int.MaxValue)
    {
        var totalCells = ChunkSize * ChunkSize;
        startCell = Math.Clamp(startCell, 0, totalCells);
        var endCell = Math.Min(totalCells, startCell + Math.Clamp(cellCount, 0, totalCells));
        if (startCell >= endCell)
            return startCell;

        component.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();
        _tiles.Clear();
        Array.Clear(_chunkBiomeTiles);
        var cacheClassicTiles = _classicStreamingQuery.HasComp(gridUid);
        var appliedTiles = false;

        using (_classicProfiler.Value("ClassicBiome.TileSlice"))
        {
            for (var cell = startCell; cell < endCell; cell++)
            {
                var x = cell / ChunkSize;
                var y = cell % ChunkSize;
                var indices = new Vector2i(x + chunk.X, y + chunk.Y);

                if (modified.Contains(indices))
                    continue;

                if (_mapSystem.TryGetTileRef(gridUid, grid, indices, out var tileRef) && !tileRef.Tile.IsEmpty)
                    continue;

                if (!TryGetTile(indices, component.Layers, seed, (gridUid, grid), out var biomeTile))
                    continue;

                if (cacheClassicTiles)
                    _chunkBiomeTiles[cell] = biomeTile.Value;
                _tiles.Add((indices, biomeTile.Value));
            }

            appliedTiles = _tiles.Count > 0;
            using (_classicProfiler.Value("ClassicBiome.TileApply"))
                SetClassicBiomeTiles(gridUid, grid, _tiles);
            _tiles.Clear();
        }

        if (stopWhenClassicBudgetExpires && appliedTiles && ClassicStreamingBudgetExpired())
            return startCell;

        var processedEnd = endCell;
        if (ClassicBiomeLoadsEntities(gridUid))
        {
            using (_classicProfiler.Value("ClassicBiome.EntitySlice"))
            {
                component.LoadedEntities.TryGetValue(chunk, out var loadedEntities);
                processedEnd = startCell;

                for (var cell = startCell; cell < endCell; cell++)
                {
                    var x = cell / ChunkSize;
                    var y = cell % ChunkSize;
                    var indices = new Vector2i(x + chunk.X, y + chunk.Y);

                    if (modified.Contains(indices))
                        goto CellComplete;

                    if (loadedEntities != null && loadedEntities.ContainsValue(indices))
                        goto CellComplete;

                    var biomeTile = _chunkBiomeTiles[cell];
                    var hasEntity = biomeTile is { } tile
                        ? TryGetEntity(indices, component.Layers, tile, seed, (gridUid, grid), out var entPrototype)
                        : TryGetEntity(indices, component, (gridUid, grid), out entPrototype);
                    if (!hasEntity || entPrototype == null)
                        goto CellComplete;

                    var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
                    if (anchored.MoveNext(out _))
                        goto CellComplete;

                    if (_classicBackgroundEntitySpawns >= classicEntitySpawnLimit)
                    {
                        break;
                    }

                    using (_classicProfiler.Value("ClassicBiome.EntitySpawn"))
                        loadedEntities = SpawnClassicBiomeEntity(
                            entPrototype,
                            gridUid,
                            grid,
                            indices,
                            loadedEntities);
                    if (classicEntitySpawnLimit != int.MaxValue)
                        RecordClassicBackgroundEntitySpawns(gridUid, 1);

                    if (loadedEntities != null)
                        component.LoadedEntities[chunk] = loadedEntities;

                    CellComplete:
                    processedEnd = cell + 1;
                    if (stopWhenClassicBudgetExpires && ClassicStreamingBudgetExpired())
                        break;
                }
            }
        }

        if (ClassicBiomeLoadsDecals(gridUid))
        {
            using (_classicProfiler.Value("ClassicBiome.DecalSlice"))
            {
                component.LoadedDecals.TryGetValue(chunk, out var loadedDecals);

                for (var cell = startCell; cell < processedEnd; cell++)
                {
                    var x = cell / ChunkSize;
                    var y = cell % ChunkSize;
                    var indices = new Vector2i(x + chunk.X, y + chunk.Y);

                    if (modified.Contains(indices))
                        continue;

                    if (loadedDecals != null && loadedDecals.ContainsValue(indices))
                        continue;

                    var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);

                    if (anchored.MoveNext(out _) || !TryGetDecals(indices, component.Layers, seed, (gridUid, grid), out var decals))
                        continue;

                    foreach (var decal in decals)
                    {
                        if (!_decals.TryAddDecal(decal.ID, new EntityCoordinates(gridUid, decal.Position), out var dec))
                            continue;

                        if (loadedDecals == null)
                        {
                            loadedDecals = new Dictionary<uint, Vector2i>();
                            component.LoadedDecals.Add(chunk, loadedDecals);
                        }
                        loadedDecals.Add(dec, indices);
                    }
                }
            }
        }

        if (modified.Count == 0)
        {
            _tilePool.Return(modified);
            component.ModifiedTiles.Remove(chunk);
        }
        else
        {
            component.ModifiedTiles[chunk] = modified;
        }

        return processedEnd;
    }


}
