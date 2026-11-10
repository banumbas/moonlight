// Namespace does not match folder structure
#pragma warning disable IDE0130
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Stopwatch = System.Diagnostics.Stopwatch;
using Content.Server._Moonlight.Geyser;
using Content.Server._Moonlight.Station;
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
using Content.Server._Moonlight.ZLevels.Core;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Shared._Moonlight.Vehicles;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
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
/// Moonlight-specific biome streaming fast paths.
/// </summary>
public sealed partial class BiomeSystem
{
    [Dependency] private EntityQuery<MoonlightUndergroundBiomeComponent> _moonlightUndergroundQuery = default!;
    [Dependency] private EntityQuery<MoonlightBiomeAlwaysUnloadComponent> _moonlightAlwaysUnloadQuery = default!;
    [Dependency] private EntityQuery<MoonlightGeyserGeneratorComponent> _moonlightGeyserGeneratorQuery = default!;
    [Dependency] private EntityQuery<MoonlightGeyserOutletComponent> _moonlightGeyserOutletQuery = default!;
    [Dependency] private EntityQuery<MoonlightBiomeStreamingComponent> _moonlightStreamingQuery = default!;
    [Dependency] private IGameTiming _moonlightTiming = default!;
    [Dependency] private ISerializationManager _moonlightSerialization = default!;
    [Dependency] private EntityQuery<MoonlightZMapComponent> _moonlightZMapQuery = default!;
    [Dependency] private EntityQuery<MoonlightZPhysicsComponent> _moonlightZPhysicsQuery = default!;
    [Dependency] private EntityQuery<MoonlightZLevelHighGroundComponent> _moonlightHighGroundQuery = default!;
    [Dependency] private MoonlightZLevelsSystem _moonlightZLevels = default!;
    [Dependency] private MoonlightVehicleSystem _moonlightVehicles = default!;
    [Dependency] private EntityQuery<DamageableComponent> _moonlightDamageableQuery = default!;
    [Dependency] private ProfManager _moonlightProfiler = default!;

    private readonly HashSet<EntityUid> _moonlightBiomeTileWrites = new();
    private readonly Dictionary<(EntityPrototype Prototype, int Depth), MoonlightZPhysicsComponent> _moonlightZPhysicsDefaults = new();
    private readonly List<(EntityUid GridUid, BiomeComponent Biome, MapGridComponent Grid, MoonlightBiomeStreamingComponent Streaming)> _moonlightStreamingBiomes = new();
    private readonly Dictionary<Vector2i, ulong> _moonlightViewerCellMasks = new();
    private readonly List<Vector2i> _moonlightViewerCellChunks = new();
    private readonly List<Vector2i> _moonlightStaleUnloadChunks = new();
    private readonly HashSet<Vector2i> _moonlightRearmedUnloadChunks = new();
    private readonly MoonlightZLevelOpeningCache _moonlightOpeningCache = new();
    private readonly List<Entity<MapGridComponent>> _moonlightOpeningGrids = new();
    private readonly List<Box2> _moonlightOpeningBounds = new();
    private readonly List<MoonlightBiomeOpeningScanKey> _moonlightStaleOpeningScans = new();
    private readonly HashSet<Vector2i> _moonlightZCacheBatch = new();
    private readonly HashSet<EntityUid> _moonlightHandledVehicleAnchors = new();
    private readonly List<MapCoordinates> _moonlightVehicleExitCoordinates = new();
    private readonly List<(ICommonSession Session, EntityUid Viewer, TransformComponent AttachedXform)> _moonlightViewRequests = new();
    private readonly Dictionary<EntityUid, TimeSpan> _moonlightPendingOpeningCacheRefreshes = new();
    private readonly List<EntityUid> _moonlightStalePendingOpeningCacheViewers = new();
    private readonly HashSet<EntityUid> _moonlightForegroundStreamingBiomes = new();
    private EntityQuery<EyeComponent> _eyeQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private readonly Tile?[] _chunkBiomeTiles = new Tile?[ChunkSize * ChunkSize];
    private readonly List<Vector2i> _unloadChunks = new();
    private readonly List<(Vector2i, Tile)> _unloadTiles = new(ChunkSize * ChunkSize);
    private readonly List<(EntityUid Entity, Vector2i Tile)> _moonlightUnloadEntities = new(ChunkSize * ChunkSize);
    private readonly List<Vector2i> _moonlightRestoreCells = new(ChunkSize * ChunkSize);
    private readonly List<string> _moonlightMarkerLayers = new();
    private readonly List<(string Layer, Vector2i Chunk)> _moonlightMarkerAreas = new();
    private float _moonlightLoadRange = DefaultLoadRange;
    private const int MaxMoonlightBackgroundChunksPerTick = 64;
    private const int MoonlightChunkCells = ChunkSize * ChunkSize;
    private const int MaxMoonlightBackgroundEntitySpawnsPerTick = MoonlightChunkCells;
    private const int MaxMoonlightUnloadEntitiesPerTick = MoonlightChunkCells * 16;
    private const int MaxMoonlightForcedUnloadOperationsPerTick = 8;
    private const int MaxMoonlightPressureUnloadScan = MoonlightChunkCells;
    private const int MaxMoonlightZLevelCachedChunks = MoonlightChunkCells * 4;
    private const int MaxMoonlightOpeningSnapshotChunks = MoonlightChunkCells * 4;
    private long _moonlightStreamingWorkStarted;
    private TimeSpan _moonlightStreamingWorkBudget;
    private bool _moonlightStreamingBudgetConfigured;
    private int _moonlightBackgroundEntitySpawns;
    private int _moonlightBackgroundEntitySpawnLimit;
    private int _moonlightUnloadEntitiesProcessed;
    private int _moonlightUnloadEntityLimit;
    private int _moonlightStreamingStartIndex;
    private int _moonlightViewRequestStartIndex;
    private bool _moonlightForcedProgressUsed;
    private bool _moonlightOpeningForcedProgressUsed;
    private int _moonlightForcedUnloadOperations;
    private bool _moonlightUnloadFirst;
    private TimeSpan _moonlightNextPendingOpeningCachePrune;
    private readonly Dictionary<EntityUid, int> _moonlightBackgroundEntitySpawnsByGrid = new();
    private readonly List<EntityUid> _moonlightBiomeSpawnedEntities = new();
    private readonly List<EntityUid> _moonlightMarkerTraversal = new();
    private readonly HashSet<EntityUid> _moonlightMarkerChildren = new();
    private readonly HashSet<string> _moonlightMarkerFactions = new();
    private readonly HashSet<string> _moonlightMarkerTags = new();
    private readonly HashSet<string> _moonlightMarkerSolutionContainers = new();
    private bool _moonlightBiomeSpawnTracking;

    private void BeginMoonlightStreamingBudget()
    {
        if (_moonlightTiming.CurTime >= _moonlightNextPendingOpeningCachePrune)
        {
            _moonlightNextPendingOpeningCachePrune = _moonlightTiming.CurTime + TimeSpan.FromSeconds(10);
            _moonlightStalePendingOpeningCacheViewers.Clear();
            foreach (var (viewer, refresh) in _moonlightPendingOpeningCacheRefreshes)
            {
                if (refresh <= _moonlightTiming.CurTime || !EntityManager.EntityExists(viewer))
                    _moonlightStalePendingOpeningCacheViewers.Add(viewer);
            }

            foreach (var viewer in _moonlightStalePendingOpeningCacheViewers)
                _moonlightPendingOpeningCacheRefreshes.Remove(viewer);
        }

        _moonlightStreamingWorkBudget = TimeSpan.Zero;
        _moonlightStreamingBudgetConfigured = false;
        _moonlightBackgroundEntitySpawns = 0;
        _moonlightBackgroundEntitySpawnsByGrid.Clear();
        _moonlightBackgroundEntitySpawnLimit = 1;
        _moonlightUnloadEntitiesProcessed = 0;
        _moonlightUnloadEntityLimit = 1;
        _moonlightStreamingWorkStarted = Stopwatch.GetTimestamp();
        _moonlightForcedProgressUsed = false;
        _moonlightOpeningForcedProgressUsed = false;
        _moonlightForcedUnloadOperations = 0;
    }

    private void IncludeMoonlightStreamingBudget(MoonlightBiomeStreamingComponent streaming)
    {
        var budget = streaming.WorkBudget < TimeSpan.Zero ? TimeSpan.Zero : streaming.WorkBudget;
        if (_moonlightStreamingBudgetConfigured && budget >= _moonlightStreamingWorkBudget)
            return;

        _moonlightStreamingWorkBudget = budget;
        _moonlightStreamingBudgetConfigured = true;
    }

    private void PrecollectMoonlightViewerStreamingBudgets()
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
                _moonlightStreamingQuery.TryComp(attachedGridUid, out var attachedStreaming))
            {
                IncludeMoonlightStreamingBudget(attachedStreaming);
            }

            foreach (var viewer in pSession.ViewSubscriptions)
            {
                if (!_xformQuery.TryGetComponent(viewer, out var viewerXform) || !CanLoad(viewer))
                    continue;

                var worldPos = MoonlightBiomeViewerPosition(pSession.AttachedEntity, viewer, viewerXform);
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

                if (_moonlightStreamingQuery.TryComp(viewerGridUid, out var viewerStreaming))
                    IncludeMoonlightStreamingBudget(viewerStreaming);
                PrecollectMoonlightOpeningStreamingBudgets(
                    pSession.AttachedEntity,
                    viewer,
                    attachedXform,
                    viewerXform,
                    worldPos);
            }
        }
    }

    private void PrecollectMoonlightOpeningStreamingBudgets(
        EntityUid? attached,
        EntityUid viewer,
        TransformComponent attachedXform,
        TransformComponent viewerXform,
        Vector2 worldPos)
    {
        if (!_moonlightZLevels.IsViewerEye(attached, viewer) ||
            attachedXform.MapUid is not { } attachedMap ||
            viewerXform.MapUid is not { } viewerMap ||
            !_moonlightZMapQuery.TryComp(attachedMap, out var attachedZ) ||
            !_moonlightZMapQuery.TryComp(viewerMap, out var viewerZ) ||
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
            else if (!_moonlightZLevels.TryMapOffset((attachedMap, attachedZ), -offset, out var intermediate))
            {
                return;
            }
            else
            {
                openingMap = intermediate.Owner;
            }

            if (TryGetMoonlightMapBiome(
                    openingMap,
                    worldPos,
                    out var openingGridUid,
                    out _,
                    out _) &&
                _moonlightStreamingQuery.TryComp(openingGridUid, out var openingStreaming))
            {
                IncludeMoonlightStreamingBudget(openingStreaming);
            }
        }
    }

    private void PruneMoonlightOpeningScans(MoonlightBiomeStreamingComponent streaming)
    {
        _moonlightStaleOpeningScans.Clear();
        foreach (var (key, scan) in streaming.OpeningScans)
        {
            if (scan.LastRequestGeneration != streaming.OpeningScanGeneration &&
                _moonlightTiming.CurTime - scan.LastRequestTime >= streaming.OpeningCacheDuration)
            {
                _moonlightStaleOpeningScans.Add(key);
            }
        }

        foreach (var key in _moonlightStaleOpeningScans)
            streaming.OpeningScans.Remove(key);

        var maxEntries = Math.Clamp(streaming.MaxOpeningCacheEntries, 1, 128);
        var excess = streaming.OpeningScans.Count - maxEntries;
        if (excess > 0)
        {
            _moonlightStaleOpeningScans.Clear();
            _moonlightStaleOpeningScans.AddRange(streaming.OpeningScans.Keys);
            _moonlightStaleOpeningScans.Sort((left, right) =>
            {
                var leftScan = streaming.OpeningScans[left];
                var rightScan = streaming.OpeningScans[right];
                var leftCurrent = leftScan.LastRequestGeneration == streaming.OpeningScanGeneration;
                var rightCurrent = rightScan.LastRequestGeneration == streaming.OpeningScanGeneration;
                var comparison = leftCurrent.CompareTo(rightCurrent);
                if (comparison != 0)
                    return comparison;

                var leftComplete = leftScan.Phase == MoonlightBiomeOpeningScanPhase.Complete;
                var rightComplete = rightScan.Phase == MoonlightBiomeOpeningScanPhase.Complete;
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
                return comparison != 0 ? comparison : CompareMoonlightOpeningScanKeys(left, right);
            });

            for (var i = 0; i < excess; i++)
                streaming.OpeningScans.Remove(_moonlightStaleOpeningScans[i]);
        }

        TrimMoonlightDictionary(streaming.OpeningScans, maxEntries);
    }

    private bool TryAdmitMoonlightOpeningScan(
        MoonlightBiomeStreamingComponent streaming,
        MoonlightBiomeOpeningScanKey key,
        MoonlightBiomeOpeningScanState scan)
    {
        var maxEntries = Math.Clamp(streaming.MaxOpeningCacheEntries, 1, 128);
        while (streaming.OpeningScans.Count >= maxEntries)
        {
            if (!TryGetMoonlightOpeningAdmissionCandidate(streaming, out var candidate))
                return false;

            streaming.OpeningScans.Remove(candidate);
        }

        streaming.OpeningScans.Add(key, scan);
        return true;
    }

    private static bool TryGetMoonlightOpeningAdmissionCandidate(
        MoonlightBiomeStreamingComponent streaming,
        out MoonlightBiomeOpeningScanKey candidate)
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
                         scan.Phase == MoonlightBiomeOpeningScanPhase.Complete;
            if (!stale && !served)
                continue;

            var rank = stale ? 0 : 1;
            if (found &&
                (rank > foundRank ||
                 rank == foundRank && CompareMoonlightOpeningScanKeys(key, candidate) >= 0))
            {
                continue;
            }

            found = true;
            foundRank = rank;
            candidate = key;
        }

        return found;
    }

    private static int CompareMoonlightOpeningScanKeys(
        MoonlightBiomeOpeningScanKey left,
        MoonlightBiomeOpeningScanKey right)
    {
        var comparison = CompareMoonlightChunks(left.RequiredFirstChunk, right.RequiredFirstChunk);
        if (comparison != 0)
            return comparison;

        comparison = CompareMoonlightChunks(left.RequiredLastChunk, right.RequiredLastChunk);
        if (comparison != 0)
            return comparison;

        comparison = CompareMoonlightChunks(left.FirstTile, right.FirstTile);
        return comparison != 0 ? comparison : CompareMoonlightChunks(left.LastTile, right.LastTile);
    }

    private void InvalidateMoonlightOpeningScansForChunk(
        MoonlightBiomeStreamingComponent streaming,
        Vector2i chunk)
    {
        if (streaming.OpeningScans.Count == 0)
            return;

        var chunkLast = chunk + new Vector2i(ChunkSize - 1, ChunkSize - 1);
        _moonlightStaleOpeningScans.Clear();
        foreach (var (key, scan) in streaming.OpeningScans)
        {
            if (chunk.X <= scan.LastTile.X &&
                chunkLast.X >= scan.FirstTile.X &&
                chunk.Y <= scan.LastTile.Y &&
                chunkLast.Y >= scan.FirstTile.Y)
            {
                _moonlightStaleOpeningScans.Add(key);
            }
        }

        foreach (var key in _moonlightStaleOpeningScans)
            streaming.OpeningScans.Remove(key);
    }

    private void InitializeMoonlightBiome()
    {
        _eyeQuery = GetEntityQuery<EyeComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        Subs.CVar(_configManager, CVars.NetMaxUpdateRange, SetMoonlightLoadRange, true);
        Subs.CVar(_configManager, CVars.NetPvsPriorityRange, SetMoonlightLoadRange, true);
        UpdatesBefore.Add(typeof(MoonlightZLevelsSystem));
        UpdatesBefore.Add(typeof(Robust.Server.GameObjects.PhysicsSystem));
        SubscribeLocalEvent<TileChangedEvent>(OnMoonlightTileChanged);
        SubscribeLocalEvent<GridRemovalEvent>(OnMoonlightOpeningGridRemoved);
        SubscribeLocalEvent<MoonlightVehiclePrepareExitEvent>(OnMoonlightVehiclePrepareExit);
        SubscribeLocalEvent<MoonlightBiomeAmbientMarkerComponent, PullStartedMessage>(OnMoonlightAmbientMarkerTouched);
        SubscribeLocalEvent<MoonlightBiomeAmbientMarkerComponent, InteractHandEvent>(OnMoonlightAmbientMarkerTouched);
        SubscribeLocalEvent<MoonlightBiomeAmbientMarkerComponent, InteractUsingEvent>(OnMoonlightAmbientMarkerTouched);
        EntityManager.EntityAdded += OnMoonlightBiomeEntityAdded;
    }

    public override void Shutdown()
    {
        EntityManager.EntityAdded -= OnMoonlightBiomeEntityAdded;
        base.Shutdown();
    }

    private void OnMoonlightBiomeEntityAdded(Entity<MetaDataComponent> entity)
    {
        if (_moonlightBiomeSpawnTracking)
            _moonlightBiomeSpawnedEntities.Add(entity);
    }

    private void OnMoonlightAmbientMarkerTouched(
        Entity<MoonlightBiomeAmbientMarkerComponent> entity,
        ref PullStartedMessage args)
    {
        entity.Comp.PlayerTouched = true;
    }

    private void OnMoonlightAmbientMarkerTouched(
        Entity<MoonlightBiomeAmbientMarkerComponent> entity,
        ref InteractHandEvent args)
    {
        entity.Comp.PlayerTouched = true;
    }

    private void OnMoonlightAmbientMarkerTouched(
        Entity<MoonlightBiomeAmbientMarkerComponent> entity,
        ref InteractUsingEvent args)
    {
        entity.Comp.PlayerTouched = true;
    }

    private static bool MoonlightMarkerUncomparableComponent(string name)
    {
        return name is "NpcFactionMember" or "MobState" or "Tag" or "Body" or "Internals" or
            "Actions" or "CustomLimbVisualizer" or "HealthExaminable" or "MovedByPressure" or
            "SolutionContainerManager";
    }

    private void CollectMoonlightMarkerChildren(EntityUid root, HashSet<EntityUid> children)
    {
        children.Clear();
        _moonlightMarkerTraversal.Clear();
        _moonlightMarkerTraversal.Add(root);
        for (var i = 0; i < _moonlightMarkerTraversal.Count; i++)
        {
            if (!_xformQuery.TryGetComponent(_moonlightMarkerTraversal[i], out var xform))
                continue;

            using var enumerator = xform.ChildEnumerator;
            while (enumerator.MoveNext(out var child))
            {
                if (children.Add(child))
                    _moonlightMarkerTraversal.Add(child);
            }
        }

        _moonlightMarkerTraversal.Clear();
    }

    private void CaptureMoonlightMarkerFactions(EntityUid root, HashSet<string> factions)
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

    private void CaptureMoonlightMarkerTags(EntityUid root, HashSet<string> tags)
    {
        tags.Clear();
        if (!TryComp<TagComponent>(root, out var component))
            return;

        foreach (var tag in component.Tags)
            tags.Add(tag.ToString());
    }

    private Dictionary<EntityUid, Vector2i>? SpawnMoonlightBiomeEntity(
        string prototype,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        Dictionary<EntityUid, Vector2i>? loaded)
    {
        _moonlightBiomeSpawnedEntities.Clear();
        _moonlightBiomeSpawnTracking = true;
        EntityUid root;
        try
        {
            root = Spawn(prototype, _mapSystem.GridTileToLocal(gridUid, grid, tile));
        }
        catch
        {
            _moonlightBiomeSpawnedEntities.Clear();
            throw;
        }
        finally
        {
            _moonlightBiomeSpawnTracking = false;
        }

        if (!Deleted(root) && !EntityManager.IsQueuedForDeletion(root) &&
            _xformQuery.TryGetComponent(root, out var rootXform) &&
            rootXform.ParentUid == gridUid && !rootXform.Anchored)
        {
            _transform.AnchorEntity((root, rootXform), (gridUid, grid), tile);
        }

        foreach (var spawned in _moonlightBiomeSpawnedEntities)
        {
            if (Deleted(spawned) || EntityManager.IsQueuedForDeletion(spawned) ||
                !_xformQuery.TryGetComponent(spawned, out var xform) || xform.ParentUid != gridUid)
            {
                continue;
            }

            loaded ??= new Dictionary<EntityUid, Vector2i>(ChunkSize * ChunkSize);
            loaded.TryAdd(spawned, tile);
        }

        _moonlightBiomeSpawnedEntities.Clear();
        return loaded;
    }

    private Dictionary<EntityUid, Vector2i>? SpawnMoonlightBiomeMarkerEntity(
        string prototype,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        Dictionary<EntityUid, Vector2i>? loaded,
        MoonlightBiomeMarkerCellState marker)
    {
        _moonlightBiomeSpawnedEntities.Clear();
        _moonlightBiomeSpawnTracking = true;
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
            _moonlightBiomeSpawnedEntities.Clear();
            throw;
        }
        finally
        {
            _moonlightBiomeSpawnTracking = false;
        }

        marker.Entities.Clear();
        marker.Root = root;
        foreach (var spawned in _moonlightBiomeSpawnedEntities)
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
            EnsureComp<MoonlightBiomeAmbientMarkerComponent>(root);
            var metadata = MetaData(root);
            marker.BaselineName = metadata.EntityName;
            marker.BaselineDescription = metadata.EntityDescription;
            var baseline = new Dictionary<string, IComponent>();
            var componentNames = new HashSet<string>();
            foreach (var component in AllComps(root))
            {
                if (component is MetaDataComponent or TransformComponent or MoonlightBiomeAmbientMarkerComponent)
                    continue;

                var name = EntityManager.ComponentFactory.GetRegistration(component.GetType()).Name;
                if (name == "ActiveNPC")
                    continue;
                componentNames.Add(name);
                if (component is HTNComponent or PhysicsComponent || MoonlightMarkerUncomparableComponent(name))
                    continue;
                baseline[name] = _moonlightSerialization.CreateCopy(component, notNullableOverride: true);
            }

            marker.BaselineComponents = baseline;
            marker.BaselineComponentNames = componentNames;
            var children = new HashSet<EntityUid>();
            CollectMoonlightMarkerChildren(root, children);
            marker.BaselineChildren = children;
            CaptureMoonlightMarkerFactions(root, _moonlightMarkerFactions);
            marker.BaselineFactions = new HashSet<string>(_moonlightMarkerFactions);
            CaptureMoonlightMarkerTags(root, _moonlightMarkerTags);
            marker.BaselineTags = new HashSet<string>(_moonlightMarkerTags);
            marker.BaselineSolutionContainers = TryComp<SolutionContainerManagerComponent>(root, out var solutions)
                ? new HashSet<string>(solutions.Containers)
                : new HashSet<string>();
        }

        _moonlightBiomeSpawnedEntities.Clear();
        return loaded;
    }

    private void SetMoonlightLoadRange(float _)
    {
        _moonlightLoadRange = Math.Max(
            16f,
            Math.Max(
                _configManager.GetCVar(CVars.NetMaxUpdateRange),
                _configManager.GetCVar(CVars.NetPvsPriorityRange))) / 2f;
    }

    private void ClearMoonlightPrototypeCaches()
    {
        _moonlightOpeningCache.Clear();
    }

    private void OnMoonlightOpeningGridRemoved(GridRemovalEvent args)
    {
        _moonlightOpeningCache.RemoveGrid(args.EntityUid);
    }

    private void OnMoonlightVehiclePrepareExit(MoonlightVehiclePrepareExitEvent args)
    {
        if (!_mapSystem.TryGetMap(args.Coordinates.MapId, out var mapUid))
            return;

        MaterializeMoonlightVehicleExitCell(mapUid.Value, args.Coordinates.Position);
        if (_moonlightZMapQuery.TryComp(mapUid, out var zMap) &&
            _moonlightZLevels.TryMapDown((mapUid.Value, zMap), out var below))
        {
            MaterializeMoonlightVehicleExitCell(below.Owner, args.Coordinates.Position);
        }
    }

    private void MaterializeMoonlightVehicleExitCell(EntityUid mapUid, Vector2 worldPos)
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
                !_moonlightStreamingQuery.TryComp(gridUid, out var streaming) ||
                !TryComp<MapGridComponent>(gridUid, out var grid))
            {
                return false;
            }

            var tile = _mapSystem.TileIndicesFor(
                gridUid,
                grid,
                new MapCoordinates(worldPos, map.MapId));
            MaterializeMoonlightBiomeCell(biome, gridUid, grid, streaming, tile);
            return true;
        }
    }

    private void MaterializeMoonlightBiomeCell(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        MoonlightBiomeStreamingComponent streaming,
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
            unloading.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
            RestoreMoonlightUnload(
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
            streaming.PendingUnloads[chunk] = GetMoonlightUnloadDeadline(streaming, chunk);
        }
    }

    private void LoadMoonlightViewerChunks(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        MoonlightBiomeStreamingComponent streaming)
    {
        _moonlightViewerCellMasks.Clear();
        foreach (var cell in streaming.ViewerCells)
        {
            var chunk = SharedMapSystem.GetChunkIndices(cell, ChunkSize) * ChunkSize;
            var local = cell - chunk;
            var cellIndex = local.X * ChunkSize + local.Y;
            var bit = 1UL << cellIndex;

            if (streaming.PartialUnloads.TryGetValue(chunk, out var unloading))
            {
                unloading.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
                RestoreMoonlightUnload(
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
                        using (_moonlightProfiler.Value("MoonlightBiome.UrgentLoad"))
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

            _moonlightViewerCellMasks[chunk] = _moonlightViewerCellMasks.GetValueOrDefault(chunk) | bit;
        }

        _moonlightViewerCellChunks.Clear();
        _moonlightViewerCellChunks.AddRange(_moonlightViewerCellMasks.Keys);
        _moonlightViewerCellChunks.Sort(CompareMoonlightChunks);
        if (_moonlightViewerCellChunks.Count == 0)
            return;

        using (_moonlightProfiler.Value("MoonlightBiome.UrgentBatch"))
        {
            foreach (var chunk in _moonlightViewerCellChunks)
            {
                var pending = _moonlightViewerCellMasks[chunk];
                var generated = streaming.PriorityLoadedCells.GetValueOrDefault(chunk);
                while (pending != 0)
                {
                    var start = BitOperations.TrailingZeroCount(pending);
                    var end = start + 1;
                    while (end < MoonlightChunkCells && (pending & (1UL << end)) != 0)
                        end++;

                    using (_moonlightProfiler.Value("MoonlightBiome.UrgentLoad"))
                        LoadChunkCells(biome, gridUid, grid, chunk, seed, start, end - start);

                    var length = end - start;
                    var run = length == MoonlightChunkCells
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
    /// Advances a Moonlight chunk teardown by one bounded operation. Entity comparison and entity
    /// deletion are the expensive part and are sliced; decal/tile finalization is kept as its own
    /// operation so it cannot be added to the last entity slice in the same tick.
    /// </summary>
    private void UnloadMoonlightChunks(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        MoonlightBiomeStreamingComponent streaming)
    {
        var active = _activeChunks[biome];
        _moonlightRearmedUnloadChunks.Clear();
        PruneMoonlightZLevelCache(streaming);
        var unloadEntityLimit = Math.Min(
            _moonlightUnloadEntityLimit,
            _moonlightUnloadEntitiesProcessed +
            Math.Clamp(streaming.UnloadEntitiesPerTick, 1, MaxMoonlightUnloadEntitiesPerTick));

        using (_moonlightProfiler.Value("MoonlightBiome.UnloadDiscover"))
        {
            if (!streaming.UnloadTrackingInitialized)
            {
                foreach (var chunk in biome.LoadedChunks)
                {
                    if (!IsMoonlightChunkRetained(active, streaming, chunk))
                        streaming.PendingUnloads.TryAdd(chunk, GetMoonlightUnloadDeadline(streaming, chunk));
                }

                foreach (var chunk in streaming.PartialLoads.Keys)
                {
                    if (!IsMoonlightChunkRetained(active, streaming, chunk))
                        streaming.PendingUnloads.TryAdd(chunk, GetMoonlightUnloadDeadline(streaming, chunk));
                }

                streaming.UnloadTrackingInitialized = true;
            }
            else
            {
                foreach (var chunk in streaming.PreviousActiveChunks)
                {
                    if (IsMoonlightChunkRetained(active, streaming, chunk) ||
                        !biome.LoadedChunks.Contains(chunk) && !streaming.PartialLoads.ContainsKey(chunk))
                    {
                        continue;
                    }

                    streaming.PendingUnloads.TryAdd(chunk, GetMoonlightUnloadDeadline(streaming, chunk));
                }
            }

            foreach (var chunk in active)
            {
                streaming.PendingUnloads.Remove(chunk);
                streaming.CachedZChunks.Remove(chunk);
                streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
                if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                    partial.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
            }
            foreach (var chunk in streaming.LandingChunks)
            {
                streaming.PendingUnloads.Remove(chunk);
                streaming.CachedZChunks.Remove(chunk);
                streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
                if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                    partial.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
            }
            foreach (var chunk in streaming.AdjacentPrefetchChunks)
            {
                streaming.PendingUnloads.Remove(chunk);
                streaming.CachedZChunks.Remove(chunk);
                streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
                if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                    partial.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
            }
            CacheMoonlightDepartedAdjacentPrefetchChunks(biome, active, streaming);
            foreach (var (chunk, partial) in streaming.PartialUnloads)
            {
                if (partial.Phase == MoonlightBiomePartialUnloadPhase.Restoring &&
                    !IsMoonlightChunkRetained(active, streaming, chunk) &&
                    !MoonlightZCacheAlive(streaming, chunk))
                {
                    partial.Phase = MoonlightBiomePartialUnloadPhase.Entities;
                }
            }

            streaming.PreviousActiveChunks.Clear();
            streaming.PreviousActiveChunks.UnionWith(active);
            streaming.PreviousActiveChunks.UnionWith(streaming.LandingChunks);
            streaming.PreviousActiveChunks.UnionWith(streaming.AdjacentPrefetchChunks);
            streaming.PreviousAdjacentPrefetchChunks.Clear();
            streaming.PreviousAdjacentPrefetchChunks.UnionWith(streaming.AdjacentPrefetchChunks);
        }

        var unloadPressure = MoonlightBiomeHasUnloadPressure(streaming);

        var configuredOperations = Math.Clamp(
            streaming.BackgroundChunksPerTick,
            0,
            MaxMoonlightBackgroundChunksPerTick);
        var operations = unloadPressure
            ? Math.Max(MaxMoonlightForcedUnloadOperationsPerTick, configuredOperations)
            : Math.Max(1, configuredOperations);
        var completedChunks = 0;
        var completionLimit = Math.Max(1, configuredOperations);
        var forcedSelectionUsed = false;
        var selectedChunk = default(Vector2i);
        MoonlightBiomePartialUnloadState? selectedState = null;

        for (var operation = 0; operation < operations; operation++)
        {
            if (unloadPressure && completedChunks >= completionLimit)
                break;

            var budgetExpired = MoonlightStreamingBudgetExpired();
            var forcedLimit = unloadPressure ? MaxMoonlightForcedUnloadOperationsPerTick : 1;
            if (budgetExpired && _moonlightForcedUnloadOperations >= forcedLimit)
                break;

            if (selectedState == null)
            {
                if (budgetExpired)
                {
                    if (forcedSelectionUsed)
                        break;
                    forcedSelectionUsed = true;
                }

                if (!TryGetMoonlightPartialUnload(streaming, out selectedChunk, out selectedState) &&
                    !TryStartMoonlightUnload(
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
                _moonlightForcedUnloadOperations++;

            var chunk = selectedChunk;
            var currentState = selectedState!;
            switch (currentState.Phase)
            {
                case MoonlightBiomePartialUnloadPhase.Entities:
                    if (_moonlightUnloadEntitiesProcessed >= unloadEntityLimit)
                    {
                        TrimMoonlightUnloadDictionaries(streaming);
                        return;
                    }

                    using (_moonlightProfiler.Value("MoonlightBiome.UnloadEntitySlice"))
                    {
                        var remaining = unloadEntityLimit - _moonlightUnloadEntitiesProcessed;
                        _moonlightUnloadEntitiesProcessed += UnloadMoonlightEntitySlice(
                            biome,
                            gridUid,
                            grid,
                            chunk,
                            streaming,
                            currentState,
                            Math.Min(
                                remaining,
                                Math.Clamp(streaming.UnloadEntitiesPerSlice, 1, MoonlightChunkCells)),
                            stopWhenBudgetExpires: !budgetExpired);
                    }
                    break;
                case MoonlightBiomePartialUnloadPhase.Finalize:
                    if (IsMoonlightChunkRetained(active, streaming, chunk))
                    {
                        currentState.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
                        goto case MoonlightBiomePartialUnloadPhase.Restoring;
                    }

                    using (_moonlightProfiler.Value("MoonlightBiome.UnloadFinalize"))
                    {
                        AdvanceMoonlightUnloadFinalize(
                            biome,
                            gridUid,
                            grid,
                            chunk,
                            seed,
                            currentState,
                            Math.Clamp(streaming.UnloadCellsPerSlice, 1, MoonlightChunkCells),
                            stopWhenBudgetExpires: !budgetExpired);
                    }
                    break;
                case MoonlightBiomePartialUnloadPhase.Commit:
                    if (IsMoonlightChunkRetained(active, streaming, chunk))
                    {
                        currentState.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
                        goto case MoonlightBiomePartialUnloadPhase.Restoring;
                    }

                    using (_moonlightProfiler.Value("MoonlightBiome.UnloadCommit"))
                    {
                        CommitMoonlightUnload(biome, gridUid, grid, chunk, streaming, currentState);
                    }
                    completedChunks++;
                    break;
                case MoonlightBiomePartialUnloadPhase.Restoring:
                    if (_moonlightUnloadEntitiesProcessed >= unloadEntityLimit)
                    {
                        TrimMoonlightUnloadDictionaries(streaming);
                        return;
                    }

                    using (_moonlightProfiler.Value("MoonlightBiome.UnloadRestore"))
                    {
                        var remaining = unloadEntityLimit - _moonlightUnloadEntitiesProcessed;
                        _moonlightUnloadEntitiesProcessed += RestoreMoonlightUnload(
                            biome,
                            gridUid,
                            grid,
                            chunk,
                            seed,
                            streaming,
                            currentState,
                            Math.Min(
                                remaining,
                                Math.Clamp(streaming.UnloadEntitiesPerSlice, 1, MoonlightChunkCells)),
                            stopWhenBudgetExpires: !budgetExpired);
                    }
                    break;
            }

            if (!streaming.PartialUnloads.TryGetValue(chunk, out var continuingState) ||
                !ReferenceEquals(currentState, continuingState))
            {
                selectedState = null;
            }

            if (MoonlightStreamingBudgetExpired() &&
                _moonlightForcedUnloadOperations >= forcedLimit)
            {
                break;
            }
        }

        TrimMoonlightUnloadDictionaries(streaming);
    }

    private static void TrimMoonlightUnloadDictionaries(MoonlightBiomeStreamingComponent streaming)
    {
        if (streaming.PendingUnloads.Count <= 256)
            TrimMoonlightDictionary(streaming.PendingUnloads, 16);
        if (streaming.PartialUnloads.Count <= 64)
            TrimMoonlightDictionary(streaming.PartialUnloads, 8);
    }

    private bool TryStartMoonlightUnload(
        BiomeComponent biome,
        HashSet<Vector2i> active,
        MoonlightBiomeStreamingComponent streaming,
        bool ignoreDelay,
        out Vector2i chunk,
        [NotNullWhen(true)] out MoonlightBiomePartialUnloadState? state)
    {
        chunk = default;
        state = null;
        var found = false;
        var foundPartial = false;
        var foundDeadline = TimeSpan.MaxValue;
        var pressureCandidates = 0;

        _moonlightStaleUnloadChunks.Clear();
        foreach (var (candidate, deadline) in streaming.PendingUnloads)
        {
            if (_moonlightRearmedUnloadChunks.Contains(candidate))
                continue;

            if (IsMoonlightChunkRetained(active, streaming, candidate))
            {
                _moonlightStaleUnloadChunks.Add(candidate);
                continue;
            }

            var partial = streaming.PartialLoads.ContainsKey(candidate);
            if (!partial && !biome.LoadedChunks.Contains(candidate))
            {
                _moonlightStaleUnloadChunks.Add(candidate);
                continue;
            }

            if (MoonlightZCacheAlive(streaming, candidate) ||
                !ignoreDelay && _moonlightTiming.CurTime < deadline)
            {
                continue;
            }

            if (ignoreDelay && pressureCandidates++ >= MaxMoonlightPressureUnloadScan)
                break;

            if (found && (foundPartial && !partial ||
                foundPartial == partial &&
                (deadline > foundDeadline ||
                 deadline == foundDeadline && CompareMoonlightChunks(candidate, chunk) >= 0)))
            {
                continue;
            }

            chunk = candidate;
            found = true;
            foundPartial = partial;
            foundDeadline = deadline;
        }

        foreach (var stale in _moonlightStaleUnloadChunks)
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
            materializedCells |= MoonlightCellMask(0, partialCursor) | priorityCells;
        }
        else if (!biome.LoadedChunks.Remove(chunk))
        {
            return false;
        }
        else
        {
            InvalidateMoonlightOpeningScansForChunk(streaming, chunk);
        }

        streaming.PendingUnloads.Remove(chunk);
        state = new MoonlightBiomePartialUnloadState
        {
            WasPartial = foundPartial,
            PartialCursor = Math.Clamp(partialCursor, 0, MoonlightChunkCells),
            PriorityCells = priorityCells,
            MaterializedCells = materializedCells,
        };
        streaming.PartialUnloads.Add(chunk, state);
        return true;
    }

    private static bool TryGetMoonlightPartialUnload(
        MoonlightBiomeStreamingComponent streaming,
        out Vector2i chunk,
        [NotNullWhen(true)] out MoonlightBiomePartialUnloadState? state)
    {
        chunk = default;
        state = null;
        var found = false;
        var foundRank = int.MaxValue;

        foreach (var (candidate, candidateState) in streaming.PartialUnloads)
        {
            var rank = candidateState.Phase == MoonlightBiomePartialUnloadPhase.Restoring
                ? 0
                : candidateState.WasPartial
                    ? 1
                    : 2;
            if (found && (rank > foundRank ||
                rank == foundRank && CompareMoonlightChunks(candidate, chunk) >= 0))
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

    private int UnloadMoonlightEntitySlice(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        MoonlightBiomeStreamingComponent streaming,
        MoonlightBiomePartialUnloadState state,
        int limit,
        bool stopWhenBudgetExpires = true)
    {
        if (!biome.LoadedEntities.TryGetValue(chunk, out var loaded) || loaded.Count == 0)
        {
            biome.LoadedEntities.Remove(chunk);
            state.Phase = MoonlightBiomePartialUnloadPhase.Finalize;
            return 0;
        }

        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();
        var depth = MoonlightBiomeDepth(gridUid);
        var processed = 0;
        while (loaded.Count > 0 && processed < limit)
        {
            var tile = default(Vector2i);
            foreach (var pair in loaded)
            {
                tile = pair.Value;
                break;
            }

            _moonlightUnloadEntities.Clear();
            foreach (var pair in loaded)
            {
                if (pair.Value == tile)
                    _moonlightUnloadEntities.Add((pair.Key, pair.Value));
            }

            MoonlightBiomeMarkerCellState? marker = null;
            if (streaming.MarkerCells.TryGetValue(chunk, out var markerCells))
                markerCells.TryGetValue(tile, out marker);

            var canUnload = true;
            foreach (var (entity, _) in _moonlightUnloadEntities)
            {
                if (marker != null && !marker.Entities.Contains(entity) ||
                    Deleted(entity) || !_xformQuery.TryGetComponent(entity, out var xform) ||
                    xform.ParentUid != gridUid ||
                    (marker == null || marker.Root != entity || marker.BaselineComponents == null) &&
                    _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != tile ||
                    MoonlightHasPersistentBiomeDependent(gridUid, grid, tile, entity) ||
                    !(marker == null
                        ? MoonlightCanUnloadTrackedBiomeEntity(biome, gridUid, grid, tile, entity, depth)
                        : MoonlightCanUnloadMarkerEntity(marker, gridUid, entity, depth)))
                {
                    canUnload = false;
                    break;
                }
            }
            if (marker != null && marker.Entities.Count != _moonlightUnloadEntities.Count)
                canUnload = false;

            if (canUnload)
            {
                foreach (var (entity, _) in _moonlightUnloadEntities)
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

            foreach (var (entity, _) in _moonlightUnloadEntities)
                loaded.Remove(entity);
            processed += _moonlightUnloadEntities.Count;

            if (stopWhenBudgetExpires && MoonlightStreamingBudgetExpired())
                break;
        }

        if (loaded.Count == 0)
        {
            biome.LoadedEntities.Remove(chunk);
            state.Phase = MoonlightBiomePartialUnloadPhase.Finalize;
        }

        StoreMoonlightModifiedTiles(biome, chunk, modified);
        return processed;
    }

    private void AdvanceMoonlightUnloadFinalize(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        int seed,
        MoonlightBiomePartialUnloadState state,
        int limit,
        bool stopWhenBudgetExpires = true)
    {
        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();

        var processed = 0;
        state.FinalizeCursor = Math.Clamp(state.FinalizeCursor, 0, MoonlightChunkCells);
        while (state.FinalizeCursor < MoonlightChunkCells && processed < Math.Clamp(limit, 1, MoonlightChunkCells))
        {
            if (processed > 0 && stopWhenBudgetExpires && MoonlightStreamingBudgetExpired())
                break;

            var remaining = state.MaterializedCells & (ulong.MaxValue << state.FinalizeCursor);
            if (remaining == 0)
            {
                state.FinalizeCursor = MoonlightChunkCells;
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

            if (MoonlightHasBlockingAnchoredEntity(gridUid, grid, indices))
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

            if (stopWhenBudgetExpires && MoonlightStreamingBudgetExpired())
                break;
        }

        if (state.FinalizeCursor < MoonlightChunkCells &&
            (state.MaterializedCells & (ulong.MaxValue << state.FinalizeCursor)) == 0)
            state.FinalizeCursor = MoonlightChunkCells;

        StoreMoonlightModifiedTiles(biome, chunk, modified);
        if (state.FinalizeCursor >= MoonlightChunkCells)
            state.Phase = MoonlightBiomePartialUnloadPhase.Commit;
    }

    private void CommitMoonlightUnload(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        MoonlightBiomeStreamingComponent streaming,
        MoonlightBiomePartialUnloadState state)
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
        for (var cell = 0; cell < MoonlightChunkCells; cell++)
        {
            if ((state.ClearableCells & (1UL << cell)) == 0)
                continue;

            var x = cell / ChunkSize;
            var y = cell % ChunkSize;
            var indices = new Vector2i(x + chunk.X, y + chunk.Y);
            if (modified.Contains(indices))
                continue;

            if (MoonlightHasBlockingAnchoredEntity(gridUid, grid, indices))
            {
                modified.Add(indices);
                continue;
            }

            _unloadTiles.Add((indices, Tile.Empty));
        }

        SetMoonlightBiomeTiles(gridUid, grid, _unloadTiles);
        _unloadTiles.Clear();

        if (streaming.MarkerCells.TryGetValue(chunk, out var markerCells))
        {
            foreach (var (tile, marker) in markerCells)
            {
                if (!Deleted(marker.Root))
                    RemComp<MoonlightBiomeAmbientMarkerComponent>(marker.Root);

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
        StoreMoonlightModifiedTiles(biome, chunk, modified);
    }

    private int RestoreMoonlightUnload(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        int seed,
        MoonlightBiomeStreamingComponent streaming,
        MoonlightBiomePartialUnloadState state,
        int limit,
        Vector2i? priorityCell = null,
        bool stopWhenBudgetExpires = true)
    {
        biome.ModifiedTiles.TryGetValue(chunk, out var modified);
        modified ??= _tilePool.Get();
        _moonlightRestoreCells.Clear();
        if (priorityCell is { } requested)
        {
            if (state.RemovedEntityCells.Contains(requested))
                _moonlightRestoreCells.Add(requested);
        }
        else
        {
            foreach (var tile in state.RemovedEntityCells)
                _moonlightRestoreCells.Add(tile);
            _moonlightRestoreCells.Sort(CompareMoonlightChunks);
            if (_moonlightRestoreCells.Count > limit)
                _moonlightRestoreCells.RemoveRange(limit, _moonlightRestoreCells.Count - limit);
        }

        biome.LoadedEntities.TryGetValue(chunk, out var loaded);
        var processed = 0;
        foreach (var tile in _moonlightRestoreCells)
        {
            MoonlightBiomeMarkerCellState? marker = null;
            if (streaming.MarkerCells.TryGetValue(chunk, out var markerCells))
                markerCells.TryGetValue(tile, out marker);

            if (!modified.Contains(tile))
            {
                _moonlightUnloadEntities.Clear();
                if (loaded != null)
                {
                    foreach (var pair in loaded)
                    {
                        if (pair.Value == tile)
                            _moonlightUnloadEntities.Add((pair.Key, pair.Value));
                    }
                }

                var canRestore = true;
                var depth = MoonlightBiomeDepth(gridUid);
                foreach (var (entity, _) in _moonlightUnloadEntities)
                {
                    if (marker != null && !marker.Entities.Contains(entity) ||
                        Deleted(entity) || !_xformQuery.TryGetComponent(entity, out var xform) ||
                        xform.ParentUid != gridUid ||
                        (marker == null || marker.Root != entity || marker.BaselineComponents == null) &&
                        _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != tile ||
                        MoonlightHasPersistentBiomeDependent(gridUid, grid, tile, entity) ||
                        !(marker == null
                            ? MoonlightCanUnloadTrackedBiomeEntity(biome, gridUid, grid, tile, entity, depth)
                            : MoonlightCanUnloadMarkerEntity(marker, gridUid, entity, depth)))
                    {
                        canRestore = false;
                        break;
                    }
                }

                if (canRestore)
                {
                    foreach (var (entity, _) in _moonlightUnloadEntities)
                    {
                        Del(entity);
                        loaded!.Remove(entity);
                    }

                    if (MoonlightHasBlockingAnchoredEntity(gridUid, grid, tile))
                    {
                        modified.Add(tile);
                        if (marker != null)
                            marker.Modified = true;
                    }
                    else if (marker is { Modified: false })
                    {
                        loaded = SpawnMoonlightBiomeMarkerEntity(
                            marker.Prototype,
                            gridUid,
                            grid,
                            tile,
                            loaded,
                            marker);
                    }
                    else if (marker == null && TryGetEntity(tile, biome, (gridUid, grid), out var prototype))
                    {
                        loaded = SpawnMoonlightBiomeEntity(prototype, gridUid, grid, tile, loaded);
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

            if (stopWhenBudgetExpires && MoonlightStreamingBudgetExpired())
                break;
        }

        if (loaded is { Count: > 0 })
            biome.LoadedEntities[chunk] = loaded;
        else
            biome.LoadedEntities.Remove(chunk);

        StoreMoonlightModifiedTiles(biome, chunk, modified);
        if (state.RemovedEntityCells.Count > 0)
            return processed;

        streaming.PartialUnloads.Remove(chunk);
        streaming.PartialLoads.Remove(chunk);
        streaming.PriorityLoadedCells.Remove(chunk);
        streaming.MaterializedCells.Remove(chunk);
        if (_activeChunks.TryGetValue(biome, out var active) &&
            IsMoonlightChunkRetained(active, streaming, chunk))
        {
            streaming.PendingUnloads.Remove(chunk);
        }
        else
        {
            streaming.PendingUnloads.TryAdd(chunk, GetMoonlightUnloadDeadline(streaming, chunk));
            _moonlightRearmedUnloadChunks.Add(chunk);
        }

        if (!state.WasPartial || state.PartialCursor >= MoonlightChunkCells)
        {
            if (biome.LoadedChunks.Add(chunk))
                InvalidateMoonlightOpeningScansForChunk(streaming, chunk);
            return processed;
        }

        var cursor = Math.Clamp(state.PartialCursor, 0, MoonlightChunkCells);
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

    private void StoreMoonlightModifiedTiles(BiomeComponent biome, Vector2i chunk, HashSet<Vector2i> modified)
    {
        if (modified.Count > 0)
        {
            biome.ModifiedTiles[chunk] = modified;
            return;
        }

        biome.ModifiedTiles.Remove(chunk);
        _tilePool.Return(modified);
    }

    private static int CompareMoonlightChunks(Vector2i a, Vector2i b)
    {
        var result = a.X.CompareTo(b.X);
        return result != 0 ? result : a.Y.CompareTo(b.Y);
    }

    private static bool IsMoonlightChunkRetained(
        HashSet<Vector2i> active,
        MoonlightBiomeStreamingComponent streaming,
        Vector2i chunk)
    {
        return active.Contains(chunk) ||
               streaming.LandingChunks.Contains(chunk) ||
               streaming.AdjacentPrefetchChunks.Contains(chunk);
    }

    private static ulong MoonlightCellMask(int start, int end)
    {
        start = Math.Clamp(start, 0, MoonlightChunkCells);
        end = Math.Clamp(end, start, MoonlightChunkCells);
        var belowEnd = end == MoonlightChunkCells ? ulong.MaxValue : (1UL << end) - 1UL;
        var belowStart = start == 0 ? 0UL : (1UL << start) - 1UL;
        return belowEnd & ~belowStart;
    }

    private int GetMoonlightBackgroundEntitySpawnLimit(
        EntityUid gridUid,
        MoonlightBiomeStreamingComponent streaming)
    {
        var gridLimit = Math.Clamp(
            streaming.BackgroundEntitySpawnsPerTick,
            1,
            MaxMoonlightBackgroundEntitySpawnsPerTick);
        var gridSpawns = _moonlightBackgroundEntitySpawnsByGrid.GetValueOrDefault(gridUid);
        var remaining = Math.Min(
            _moonlightBackgroundEntitySpawnLimit - _moonlightBackgroundEntitySpawns,
            gridLimit - gridSpawns);
        return _moonlightBackgroundEntitySpawns + Math.Max(0, remaining);
    }

    private void RecordMoonlightBackgroundEntitySpawns(EntityUid gridUid, int count)
    {
        if (count <= 0)
            return;

        _moonlightBackgroundEntitySpawns += count;
        _moonlightBackgroundEntitySpawnsByGrid[gridUid] =
            _moonlightBackgroundEntitySpawnsByGrid.GetValueOrDefault(gridUid) + count;
    }

    private void LoadMoonlightBackgroundChunks(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        MoonlightBiomeStreamingComponent streaming,
        bool includeAdjacentPrefetch)
    {
        var unloadPressure = MoonlightBiomeHasUnloadPressure(streaming);
        var entitySpawnLimit = GetMoonlightBackgroundEntitySpawnLimit(gridUid, streaming);

        var loadSlots = Math.Clamp(streaming.BackgroundChunksPerTick, 0, MaxMoonlightBackgroundChunksPerTick);
        if (loadSlots == 0 && streaming.PartialLoads.Count > 0)
            loadSlots = 1;
        if (loadSlots == 0)
            return;

        var cellsPerSlice = Math.Clamp(streaming.BackgroundCellsPerSlice, 1, MoonlightChunkCells);
        for (var slot = 0; slot < loadSlots; slot++)
        {
            if (MoonlightBiomeLoadsEntities(gridUid) &&
                _moonlightBackgroundEntitySpawns >= entitySpawnLimit)
            {
                break;
            }

            Vector2i chunk;
            bool continuation;
            if (MoonlightStreamingBudgetExpired())
            {
                if (_moonlightForcedProgressUsed ||
                    !(unloadPressure
                        ? TrySelectMoonlightActivePartialLoad(
                            biome,
                            streaming,
                            out chunk,
                            true,
                            false)
                        : _moonlightStreamingWorkBudget > TimeSpan.Zero
                            ? TrySelectMoonlightBackgroundChunk(biome, streaming, out chunk, false)
                            : TrySelectMoonlightActivePartialLoad(biome, streaming, out chunk, false, false)))
                {
                    break;
                }

                continuation = streaming.PartialLoads.ContainsKey(chunk);
            }
            else
            {
                if (unloadPressure
                        ? !TrySelectMoonlightActivePartialLoad(
                            biome,
                            streaming,
                            out chunk,
                            includeAdjacentPrefetch: includeAdjacentPrefetch)
                        : !TrySelectMoonlightBackgroundChunk(
                            biome,
                            streaming,
                            out chunk,
                            includeAdjacentPrefetch))
                {
                    break;
                }

                continuation = streaming.PartialLoads.ContainsKey(chunk);
            }

            if (!TryBeginMoonlightBoundedOperation(continuation || unloadPressure || MoonlightStreamingBudgetExpired()))
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
                var remainingSpawns = entitySpawnLimit - _moonlightBackgroundEntitySpawns;
                if (remainingSpawns <= 0)
                    break;

                using (_moonlightProfiler.Value("MoonlightBiome.MarkerNodeSlice"))
                {
                    var processed = LoadChunkMarkerNodes(
                        biome,
                        gridUid,
                        grid,
                        chunk,
                        seed,
                        Math.Min(
                            remainingSpawns,
                            Math.Clamp(streaming.MarkerNodesPerSlice, 1, MoonlightChunkCells)));
                    RecordMoonlightBackgroundEntitySpawns(gridUid, processed);
                }

                if (MoonlightStreamingBudgetExpired())
                    _moonlightForcedProgressUsed = true;

                continue;
            }

            var previousCell = Math.Clamp(streaming.PartialLoads[chunk], 0, MoonlightChunkCells);
            var nextCell = previousCell;
            var scheduledEnd = Math.Min(MoonlightChunkCells, previousCell + cellsPerSlice);
            using (_moonlightProfiler.Value("MoonlightBiome.BackgroundSlice"))
            {
                nextCell = LoadChunkCells(
                    biome,
                    gridUid,
                    grid,
                    chunk,
                    seed,
                    nextCell,
                    cellsPerSlice,
                    stopWhenMoonlightBudgetExpires: !MoonlightStreamingBudgetExpired(),
                    moonlightEntitySpawnLimit: entitySpawnLimit);
            }

            if (MoonlightStreamingBudgetExpired())
                _moonlightForcedProgressUsed = true;

            streaming.MaterializedCells[chunk] =
                streaming.MaterializedCells.GetValueOrDefault(chunk) |
                MoonlightCellMask(previousCell, scheduledEnd);

            if (nextCell < MoonlightChunkCells)
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
                    _moonlightBackgroundEntitySpawns >= entitySpawnLimit)
                {
                    break;
                }
                continue;
            }

            streaming.PartialLoads.Remove(chunk);
            streaming.PriorityLoadedCells.Remove(chunk);
            streaming.MaterializedCells.Remove(chunk);
            if (biome.LoadedChunks.Add(chunk))
                InvalidateMoonlightOpeningScansForChunk(streaming, chunk);
        }
    }

    private bool MoonlightBiomeHasUnloadPressure(MoonlightBiomeStreamingComponent streaming)
    {
        var limit = Math.Clamp(streaming.MaxUnloadBacklogBeforeThrottling, 0, 4096);
        var backlog = streaming.PartialUnloads.Count + streaming.PendingUnloads.Count;

        foreach (var chunk in streaming.CachedZChunks.Keys)
        {
            if (!MoonlightZCacheAlive(streaming, chunk))
                continue;
            if (streaming.PartialUnloads.ContainsKey(chunk))
                backlog--;
            if (streaming.PendingUnloads.ContainsKey(chunk))
                backlog--;
        }

        return backlog > limit;
    }

    private bool MoonlightBiomeHasReadyUnload(MoonlightBiomeStreamingComponent streaming)
    {
        if (streaming.PendingUnloads.Count == 0)
            return false;
        if (MoonlightBiomeHasUnloadPressure(streaming))
            return true;

        foreach (var (chunk, deadline) in streaming.PendingUnloads)
        {
            if (!MoonlightZCacheAlive(streaming, chunk) && deadline <= _moonlightTiming.CurTime)
                return true;
        }

        return false;
    }

    private static bool MoonlightBiomeHasInactivePreviousChunks(
        BiomeComponent biome,
        HashSet<Vector2i> active,
        MoonlightBiomeStreamingComponent streaming)
    {
        foreach (var chunk in streaming.PreviousActiveChunks)
        {
            if (!IsMoonlightChunkRetained(active, streaming, chunk) &&
                (biome.LoadedChunks.Contains(chunk) || streaming.PartialLoads.ContainsKey(chunk)))
            {
                return true;
            }
        }

        return false;
    }

    private bool MoonlightZCacheAlive(MoonlightBiomeStreamingComponent streaming, Vector2i chunk)
    {
        return streaming.CachedZChunks.TryGetValue(chunk, out var deadline) &&
               deadline > _moonlightTiming.CurTime;
    }

    private bool MoonlightBiomeHasForegroundStreamingWork(
        BiomeComponent biome,
        MoonlightBiomeStreamingComponent streaming)
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

    private void LoadMoonlightPendingMarkerNodes(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        MoonlightBiomeStreamingComponent streaming)
    {
        var entitySpawnLimit = GetMoonlightBackgroundEntitySpawnLimit(gridUid, streaming);
        var remainingSpawns = entitySpawnLimit - _moonlightBackgroundEntitySpawns;
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
                          rank == foundRank && CompareMoonlightChunks(candidate, chunk) >= 0))
            {
                continue;
            }

            chunk = candidate;
            found = true;
            foundRank = rank;
        }

        if (!found)
            return;

        if (!TryBeginMoonlightBoundedOperation(true))
            return;

        using (_moonlightProfiler.Value("MoonlightBiome.MarkerNodeSlice"))
        {
            var processed = LoadChunkMarkerNodes(
                biome,
                gridUid,
                grid,
                chunk,
                seed,
                Math.Min(
                    remainingSpawns,
                    Math.Clamp(streaming.MarkerNodesPerSlice, 1, MoonlightChunkCells)));
            RecordMoonlightBackgroundEntitySpawns(gridUid, processed);
        }

        if (MoonlightStreamingBudgetExpired())
            _moonlightForcedProgressUsed = true;
    }

    private bool TrySelectMoonlightBackgroundChunk(
        BiomeComponent biome,
        MoonlightBiomeStreamingComponent streaming,
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
                    candidateDistance.Equals(distance) && CompareMoonlightChunks(candidate, chunk) >= 0))
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
                foundPartial && CompareMoonlightChunks(candidate, chunk) >= 0)
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
    private bool TrySelectMoonlightActivePartialLoad(
        BiomeComponent biome,
        MoonlightBiomeStreamingComponent streaming,
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
                if (found && CompareMoonlightChunks(candidate, chunk) >= 0)
                    continue;
            }
            else
            {
                var delta = candidate - viewer;
                var candidateDistance = (float) delta.X * delta.X + (float) delta.Y * delta.Y;
                if (found && (candidateDistance > distance ||
                    candidateDistance.Equals(distance) && CompareMoonlightChunks(candidate, chunk) >= 0))
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

    private bool MoonlightStreamingBudgetExpired()
    {
        return Stopwatch.GetElapsedTime(_moonlightStreamingWorkStarted) >= _moonlightStreamingWorkBudget;
    }

    private bool MoonlightStreamingBudgetExpired(MoonlightBiomeStreamingComponent streaming)
    {
        return MoonlightStreamingBudgetExpired();
    }

    private bool TryBeginMoonlightBoundedOperation(bool continuation)
    {
        if (!MoonlightStreamingBudgetExpired())
            return true;
        if (!continuation || _moonlightForcedProgressUsed)
            return false;

        _moonlightForcedProgressUsed = true;
        return true;
    }

    private bool TryBeginMoonlightOpeningOperation(out bool forced)
    {
        forced = false;
        if (!MoonlightStreamingBudgetExpired())
            return true;
        if (_moonlightOpeningForcedProgressUsed)
            return false;

        _moonlightOpeningForcedProgressUsed = true;
        forced = true;
        return true;
    }

    /// <summary>
    /// Processes every procedural Z biome under one server-tick budget. Rotating the first biome
    /// prevents a busy surface (or any fixed entity-query order) from starving deeper levels.
    /// Viewer chunks remain synchronous because Z movement needs their landing tile this tick.
    /// </summary>
    private void ProcessMoonlightStreamingBiomes()
    {
        var count = _moonlightStreamingBiomes.Count;
        if (count == 0)
            return;

        ConfigureMoonlightStreamingLimits();

        var start = _moonlightStreamingStartIndex % count;
        for (var i = 0; i < count; i++)
        {
            var entry = _moonlightStreamingBiomes[(start + i) % count];
            LoadMoonlightViewerChunks(entry.Biome, entry.GridUid, entry.Grid, entry.Biome.Seed, entry.Streaming);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var unloadPass = _moonlightUnloadFirst ? pass == 0 : pass == 1;
            if (unloadPass)
            {
                for (var i = 0; i < count; i++)
                {
                    var entry = _moonlightStreamingBiomes[(start + i) % count];
                    UnloadChunks(entry.Biome, entry.GridUid, entry.Grid, entry.Biome.Seed);
                }
                continue;
            }

            _moonlightForegroundStreamingBiomes.Clear();
            foreach (var entry in _moonlightStreamingBiomes)
            {
                if (MoonlightBiomeHasForegroundStreamingWork(entry.Biome, entry.Streaming))
                    _moonlightForegroundStreamingBiomes.Add(entry.GridUid);
            }

            for (var priority = 0; priority < 2; priority++)
            for (var i = 0; i < count; i++)
            {
                var entry = _moonlightStreamingBiomes[(start + i) % count];
                var foreground = _moonlightForegroundStreamingBiomes.Contains(entry.GridUid);
                if (foreground != (priority == 0))
                    continue;

                _markerChunks.TryGetValue(entry.Biome, out var markerRequests);
                PruneMoonlightMarkerAreas(entry.Biome, entry.Streaming, markerRequests);
                BuildMarkerChunks(entry.Biome, entry.GridUid, entry.Grid, entry.Biome.Seed);
                LoadMoonlightPendingMarkerNodes(
                    entry.Biome,
                    entry.GridUid,
                    entry.Grid,
                    entry.Biome.Seed,
                    entry.Streaming);
                LoadMoonlightBackgroundChunks(
                    entry.Biome,
                    entry.GridUid,
                    entry.Grid,
                    entry.Biome.Seed,
                    entry.Streaming,
                    !foreground);
            }
        }

        _moonlightUnloadFirst = !_moonlightUnloadFirst;
        _moonlightStreamingStartIndex = (start + 1) % count;
        _moonlightForegroundStreamingBiomes.Clear();
        _moonlightStreamingBiomes.Clear();
    }

    private void ConfigureMoonlightStreamingLimits()
    {
        var hasBudget = false;
        var budget = TimeSpan.Zero;
        foreach (var entry in _moonlightStreamingBiomes)
        {
            var active = _activeChunks[entry.Biome];
            var runnable = active.Count > 0 ||
                           entry.Streaming.PartialUnloads.Count > 0 ||
                           MoonlightBiomeHasInactivePreviousChunks(entry.Biome, active, entry.Streaming) ||
                           !entry.Streaming.UnloadTrackingInitialized &&
                           (entry.Streaming.PartialLoads.Count > 0 || entry.Biome.LoadedChunks.Count > 0) ||
                           MoonlightBiomeHasReadyUnload(entry.Streaming);
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
                _moonlightBackgroundEntitySpawnLimit = Math.Max(
                    _moonlightBackgroundEntitySpawnLimit,
                    Math.Clamp(
                        entry.Streaming.BackgroundEntitySpawnsPerTick,
                        1,
                        MaxMoonlightBackgroundEntitySpawnsPerTick));
            }

            _moonlightUnloadEntityLimit = Math.Max(
                _moonlightUnloadEntityLimit,
                Math.Clamp(
                    entry.Streaming.UnloadEntitiesPerTick,
                    1,
                    MaxMoonlightUnloadEntitiesPerTick));
        }

        _moonlightStreamingWorkBudget = hasBudget ? budget : TimeSpan.Zero;
        _moonlightStreamingBudgetConfigured = hasBudget;
    }

    private void AddMoonlightAdjacentLandingChunks(EntityUid viewer, TransformComponent xform, Vector2 worldPos)
    {
        if (xform.MapUid is not { } mapUid ||
            !_moonlightZMapQuery.TryComp(mapUid, out var zMap) ||
            !_moonlightZPhysicsQuery.TryComp(viewer, out var zPhysics) ||
            zPhysics.Disabled)
        {
            return;
        }

        var hasBelow = _moonlightZLevels.TryMapDown((mapUid, zMap), out var below);
        var hasAbove = _moonlightZLevels.TryMapUp((mapUid, zMap), out var above);
        if (!hasBelow && !hasAbove)
            return;

        var hasCurrentGrid = TryGetMoonlightViewerGrid(
            xform,
            worldPos,
            out var currentGridUid,
            out var currentGrid,
            out var currentTile);

        if (hasBelow)
        {
            AddMoonlightAdjacentPrefetchChunks(below.Owner, worldPos);
            var hasCurrentFloor = hasCurrentGrid &&
                                  MoonlightCurrentCellHasFloor(currentGridUid, currentGrid!, currentTile);
            if (!hasCurrentFloor || zPhysics.Velocity < 0f || zPhysics.LocalPosition < 0f)
                AddMoonlightLandingChunk(below.Owner, worldPos);
        }

        if (!hasAbove)
            return;

        var upper = above;
        for (var i = 0; i < MoonlightSharedZLevelsSystem.MaxZLevelsAboveRendering; i++)
        {
            AddMoonlightAdjacentPrefetchChunks(upper.Owner, worldPos);
            if (i == 0 &&
                (zPhysics.Velocity > 0f ||
                 zPhysics.LocalPosition > 0f ||
                 hasCurrentGrid && MoonlightCurrentCellHasHighGround(currentGridUid, currentGrid!, currentTile)))
            {
                AddMoonlightLandingChunk(upper.Owner, worldPos);
            }

            if (!_moonlightZLevels.TryMapUp(upper.AsNullable(), out var nextUpper))
                break;

            upper = nextUpper;
        }
    }

    private void AddMoonlightVehicleExitSafety(EntityUid viewer)
    {
        if (HasComp<GhostComponent>(viewer) ||
            !_moonlightVehicles.TryResolveControlledVehicle(viewer, out var vehicle) ||
            !_moonlightHandledVehicleAnchors.Add(vehicle) ||
            !_xformQuery.TryGetComponent(vehicle, out var vehicleXform) ||
            vehicleXform.MapUid is not { } mapUid ||
            !_moonlightZMapQuery.TryComp(mapUid, out var zMap))
        {
            return;
        }

        var vehiclePos = _transform.GetWorldPosition(vehicleXform);
        var hasBelow = _moonlightZLevels.TryMapDown((mapUid, zMap), out var below);
        if (hasBelow)
            AddMoonlightAdjacentPrefetchChunks(below.Owner, vehiclePos);
        if (_moonlightZLevels.TryMapUp((mapUid, zMap), out var above))
        {
            for (var i = 0; i < MoonlightSharedZLevelsSystem.MaxZLevelsAboveRendering; i++)
            {
                AddMoonlightAdjacentPrefetchChunks(above.Owner, vehiclePos);
                if (!_moonlightZLevels.TryMapUp(above.AsNullable(), out var nextAbove))
                    break;

                above = nextAbove;
            }
        }

        _moonlightVehicles.GetExteriorExitCoordinates(vehicle, _moonlightVehicleExitCoordinates);
        foreach (var exit in _moonlightVehicleExitCoordinates)
        {
            if (exit.MapId != vehicleXform.MapID)
                continue;

            AddMoonlightLandingChunk(mapUid, exit.Position);
            if (hasBelow)
                AddMoonlightLandingChunk(below.Owner, exit.Position);
        }
    }

    private void AddMoonlightAdjacentPrefetchChunks(EntityUid mapUid, Vector2 worldPos)
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
                !_moonlightStreamingQuery.TryComp(gridUid, out var streaming) ||
                !TryComp<MapGridComponent>(gridUid, out var grid) ||
                !_activeChunks.TryGetValue(biome, out var active))
            {
                return false;
            }

            var radius = Math.Clamp(streaming.AdjacentLevelPrefetchRadius, 0, ChunkSize / 2);
            if (radius == 0)
                return true;

            IncludeMoonlightStreamingBudget(streaming);
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

    private bool TryGetMoonlightViewerGrid(
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

    private bool MoonlightCurrentCellHasFloor(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
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

    public bool MoonlightIsTileOpaque(EntityUid mapUid, Vector2 worldPos)
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
            return !MoonlightZLevelOpeningCache.IsOpeningTile(tileRef.Tile, TileDefManager);

        if (!_biomeQuery.TryComp(gridUid, out var biome) || !biome.Enabled)
            return false;

        var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
        if (biome.ModifiedTiles.TryGetValue(chunk, out var modified) && modified.Contains(tile))
            return false;

        return TryGetBiomeTile(tile, biome.Layers, biome.Seed, (gridUid, grid), out var predicted) &&
               !MoonlightZLevelOpeningCache.IsOpeningTile(predicted.Value, TileDefManager);
    }

    private bool MoonlightCurrentCellHasHighGround(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (_moonlightHighGroundQuery.HasComp(uid.Value))
                return true;
        }

        return false;
    }

    private void AddMoonlightLandingChunk(EntityUid mapUid, Vector2 worldPos)
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
                !_moonlightStreamingQuery.TryComp(gridUid, out var streaming) ||
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

    private Vector2 MoonlightBiomeViewerPosition(EntityUid? attached, EntityUid viewer, TransformComponent xform)
    {
        if (_moonlightZLevels.IsViewerEye(attached, viewer) &&
            _xformQuery.TryGetComponent(attached, out var ownerXform))
        {
            var ownerPosition = _transform.GetWorldPosition(ownerXform);
            return _eyeQuery.TryComp(viewer, out var zEye) ? ownerPosition + zEye.Offset : ownerPosition;
        }

        var position = _transform.GetWorldPosition(xform);
        return _eyeQuery.TryComp(viewer, out var eye) ? position + eye.Offset : position;
    }

    private float MoonlightBiomeViewerScale(EntityUid viewer)
    {
        return _eyeQuery.TryComp(viewer, out var eye) ? MathF.Max(eye.PvsScale, 0.1f) : 1f;
    }

    /// <summary>
    /// Lower Z-eyes are useful only through transparent/open tiles. Wait until each intervening
    /// procedural level has finished its own PVS range, then mirror the client's opening test.
    /// This prevents an opaque surface from generating a complete hidden -Z1 range (and, with a
    /// larger render setting, all deeper ranges) while retaining exact fall landing cells.
    /// </summary>
    private bool MoonlightTryGetViewerRange(
        EntityUid? attached,
        EntityUid viewer,
        TransformComponent attachedXform,
        TransformComponent viewerXform,
        Vector2 worldPos,
        float pvsScale,
        out Box2? worldOpeningClip)
    {
        var range = _moonlightLoadRange * MathF.Max(pvsScale, 0.1f);
        worldOpeningClip = null;
        if (!_moonlightZLevels.IsViewerEye(attached, viewer) ||
            attachedXform.MapUid is not { } attachedMap ||
            viewerXform.MapUid is not { } viewerMap ||
            !_moonlightZMapQuery.TryComp(attachedMap, out var attachedZ) ||
            !_moonlightZMapQuery.TryComp(viewerMap, out var viewerZ) ||
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
            else if (!_moonlightZLevels.TryMapOffset((attachedMap, attachedZ), -offset, out var intermediate))
            {
                return false;
            }
            else
            {
                openingMap = intermediate.Owner;
            }

            if (TryGetMoonlightMapBiome(
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
                using (_moonlightProfiler.Value("MoonlightBiome.OpeningSlice"))
                {
                    if (!TryGetMoonlightOpeningWorldBoundsState(
                            openingGridUid,
                            openingBiome,
                            openingGrid,
                            requiredLocalBounds,
                            out openingBounds,
                            out var pending))
                    {
                        if (pending)
                            CacheMoonlightPendingZLevel(viewer);
                        else
                            _moonlightPendingOpeningCacheRefreshes.Remove(viewer);
                        return false;
                    }

                    _moonlightPendingOpeningCacheRefreshes.Remove(viewer);
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

            _moonlightOpeningBounds.Clear();
            if (!_moonlightOpeningCache.TryFindOpeningBounds(
                    mapId,
                    requiredWorldBounds,
                    _moonlightOpeningBounds,
                    out var combinedOpeningBounds,
                    maxOpeningBounds: 4096,
                    exactOpeningBounds: true,
                    _moonlightOpeningGrids,
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
    public void CacheMoonlightZLevel(EntityUid eye)
    {
        _moonlightPendingOpeningCacheRefreshes.Remove(eye);
        CacheMoonlightZLevelInternal(eye);
    }

    private void CacheMoonlightPendingZLevel(EntityUid eye)
    {
        if (_moonlightPendingOpeningCacheRefreshes.TryGetValue(eye, out var refresh) &&
            refresh > _moonlightTiming.CurTime)
        {
            return;
        }

        var duration = CacheMoonlightZLevelInternal(eye);
        if (duration is not { } cacheDuration || cacheDuration <= TimeSpan.Zero)
        {
            _moonlightPendingOpeningCacheRefreshes.Remove(eye);
            return;
        }

        var interval = TimeSpan.FromTicks(Math.Max(1L, cacheDuration.Ticks / 2L));
        if (interval > TimeSpan.FromSeconds(1))
            interval = TimeSpan.FromSeconds(1);
        var minimumTicks = Math.Max(1L, interval.Ticks / 2L);
        var jitterTicks = interval.Ticks - minimumTicks;
        var hash = unchecked((uint) eye.Id * 2654435761U);
        var staggerTicks = jitterTicks == 0L ? 0L : hash % (ulong) (jitterTicks + 1L);
        _moonlightPendingOpeningCacheRefreshes[eye] =
            _moonlightTiming.CurTime + TimeSpan.FromTicks(minimumTicks + (long) staggerTicks);
    }

    private TimeSpan? CacheMoonlightZLevelInternal(EntityUid eye)
    {
        if (!_xformQuery.TryComp(eye, out var eyeXform) || eyeXform.MapUid is not { } mapUid)
            return null;

        var worldPos = MoonlightBiomeViewerPosition(null, eye, eyeXform);
        if (!TryGetMoonlightMapBiome(
                mapUid,
                worldPos,
                out var gridUid,
                out var biome,
                out var grid) ||
            !_moonlightStreamingQuery.TryComp(gridUid, out var streaming))
        {
            return null;
        }

        PruneMoonlightZLevelCache(streaming);
        var maxChunks = Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxMoonlightZLevelCachedChunks);
        if (maxChunks == 0)
            return TimeSpan.Zero;

        var duration = streaming.ZLevelCacheDuration < TimeSpan.Zero
            ? TimeSpan.Zero
            : streaming.ZLevelCacheDuration;
        if (duration <= TimeSpan.Zero)
            return duration;
        var deadline = _moonlightTiming.CurTime + duration;
        var bounds = GetLocalBiomeViewBounds(
            gridUid,
            grid,
            worldPos,
            MoonlightBiomeViewerScale(eye));
        var chunks = new ChunkIndicesEnumerator(bounds, ChunkSize);
        _moonlightZCacheBatch.Clear();
        while (chunks.MoveNext(out var chunkIndices))
        {
            if (_moonlightZCacheBatch.Count >= maxChunks)
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
                !TryEvictMoonlightZLevelCache(streaming, _moonlightZCacheBatch))
            {
                break;
            }

            streaming.CachedZChunks[chunk] = deadline;
            streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
            _moonlightZCacheBatch.Add(chunk);
            if (streaming.PendingUnloads.ContainsKey(chunk))
            {
                var ordinaryDeadline = GetMoonlightOrdinaryUnloadDeadline(streaming);
                streaming.PendingUnloads[chunk] = ordinaryDeadline > deadline ? ordinaryDeadline : deadline;
            }
            if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                partial.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
        }

        _moonlightZCacheBatch.Clear();
        return duration;
    }

    private void CacheMoonlightDepartedAdjacentPrefetchChunks(
        BiomeComponent biome,
        HashSet<Vector2i> active,
        MoonlightBiomeStreamingComponent streaming)
    {
        if (streaming.PreviousAdjacentPrefetchChunks.Count == 0)
            return;

        var totalMax = Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxMoonlightZLevelCachedChunks);
        var maxChunks = Math.Min(
            Math.Clamp(streaming.MaxAdjacentPrefetchCachedChunks, 0, MaxMoonlightZLevelCachedChunks),
            totalMax);
        if (maxChunks == 0 || streaming.AdjacentPrefetchCacheDuration <= TimeSpan.Zero)
            return;

        var deadline = _moonlightTiming.CurTime + streaming.AdjacentPrefetchCacheDuration;
        _moonlightZCacheBatch.Clear();
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
                if (!TryEvictMoonlightAdjacentPrefetchCache(streaming, _moonlightZCacheBatch))
                    break;
            }

            if (needsEntry &&
                (streaming.CachedAdjacentPrefetchChunks.Count >= maxChunks ||
                 streaming.CachedZChunks.Count >= totalMax))
                continue;

            streaming.CachedZChunks[chunk] = deadline;
            streaming.CachedAdjacentPrefetchChunks.Add(chunk);
            _moonlightZCacheBatch.Add(chunk);
            if (streaming.PendingUnloads.TryGetValue(chunk, out var pending) && pending < deadline)
                streaming.PendingUnloads[chunk] = deadline;
            if (streaming.PartialUnloads.TryGetValue(chunk, out var partial))
                partial.Phase = MoonlightBiomePartialUnloadPhase.Restoring;
        }

        _moonlightZCacheBatch.Clear();
    }

    private bool TryEvictMoonlightAdjacentPrefetchCache(
        MoonlightBiomeStreamingComponent streaming,
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
                          deadline == oldestDeadline && CompareMoonlightChunks(chunk, oldestChunk) >= 0))
            {
                continue;
            }

            found = true;
            oldestChunk = chunk;
            oldestDeadline = deadline;
        }

        if (!found)
            return false;

        RemoveMoonlightZLevelCache(streaming, oldestChunk);
        return true;
    }

    private TimeSpan GetMoonlightUnloadDeadline(MoonlightBiomeStreamingComponent streaming, Vector2i chunk)
    {
        var deadline = GetMoonlightOrdinaryUnloadDeadline(streaming);
        return streaming.CachedZChunks.TryGetValue(chunk, out var cached) && cached > deadline
            ? cached
            : deadline;
    }

    private TimeSpan GetMoonlightOrdinaryUnloadDeadline(MoonlightBiomeStreamingComponent streaming)
    {
        var delay = streaming.UnloadDelay < TimeSpan.Zero ? TimeSpan.Zero : streaming.UnloadDelay;
        return _moonlightTiming.CurTime + delay;
    }

    private void PruneMoonlightZLevelCache(MoonlightBiomeStreamingComponent streaming)
    {
        _moonlightStaleUnloadChunks.Clear();
        foreach (var (chunk, deadline) in streaming.CachedZChunks)
        {
            if (deadline <= _moonlightTiming.CurTime)
                _moonlightStaleUnloadChunks.Add(chunk);
        }

        foreach (var chunk in _moonlightStaleUnloadChunks)
            RemoveMoonlightZLevelCache(streaming, chunk);

        var maxAdjacent = streaming.AdjacentPrefetchCacheDuration <= TimeSpan.Zero
            ? 0
            : Math.Min(
                Math.Clamp(streaming.MaxAdjacentPrefetchCachedChunks, 0, MaxMoonlightZLevelCachedChunks),
                Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxMoonlightZLevelCachedChunks));
        _moonlightZCacheBatch.Clear();
        while (streaming.CachedAdjacentPrefetchChunks.Count > maxAdjacent &&
               TryEvictMoonlightAdjacentPrefetchCache(streaming, _moonlightZCacheBatch))
        {
        }
        _moonlightZCacheBatch.Clear();
        TrimMoonlightZLevelCache(streaming);
    }

    private void TrimMoonlightZLevelCache(MoonlightBiomeStreamingComponent streaming)
    {
        var maxChunks = Math.Clamp(streaming.MaxZLevelCachedChunks, 0, MaxMoonlightZLevelCachedChunks);
        while (streaming.CachedZChunks.Count > maxChunks)
        {
            if (!TryEvictMoonlightZLevelCache(streaming))
                break;
        }

        TrimMoonlightDictionary(streaming.CachedZChunks, maxChunks);
    }

    private bool TryEvictMoonlightZLevelCache(
        MoonlightBiomeStreamingComponent streaming,
        HashSet<Vector2i>? retained = null)
    {
        var found = false;
        var oldestChunk = default(Vector2i);
        var oldestDeadline = TimeSpan.MaxValue;
        foreach (var (chunk, deadline) in streaming.CachedZChunks)
        {
            if (retained?.Contains(chunk) == true ||
                found && (deadline > oldestDeadline ||
                          deadline == oldestDeadline && CompareMoonlightChunks(chunk, oldestChunk) >= 0))
            {
                continue;
            }

            found = true;
            oldestChunk = chunk;
            oldestDeadline = deadline;
        }

        if (!found)
            return false;

        RemoveMoonlightZLevelCache(streaming, oldestChunk);
        return true;
    }

    private void RemoveMoonlightZLevelCache(MoonlightBiomeStreamingComponent streaming, Vector2i chunk)
    {
        streaming.CachedAdjacentPrefetchChunks.Remove(chunk);
        if (!streaming.CachedZChunks.Remove(chunk, out var cacheDeadline) ||
            !streaming.PendingUnloads.TryGetValue(chunk, out var pending) ||
            pending != cacheDeadline)
        {
            return;
        }

        var ordinaryDeadline = GetMoonlightOrdinaryUnloadDeadline(streaming);
        if (pending > ordinaryDeadline)
            streaming.PendingUnloads[chunk] = ordinaryDeadline;
    }

    private static void TrimMoonlightDictionary<TKey, TValue>(
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

    private bool TryGetMoonlightOpeningWorldBounds(
        EntityUid gridUid,
        BiomeComponent biome,
        MapGridComponent grid,
        Box2 localBounds,
        out Box2 openingWorldBounds)
    {
        return TryGetMoonlightOpeningWorldBoundsState(
            gridUid,
            biome,
            grid,
            localBounds,
            out openingWorldBounds,
            out _);
    }

    private bool TryGetMoonlightOpeningWorldBoundsState(
        EntityUid gridUid,
        BiomeComponent biome,
        MapGridComponent grid,
        Box2 localBounds,
        out Box2 openingWorldBounds,
        out bool pending)
    {
        if (!_moonlightStreamingQuery.TryComp(gridUid, out var streaming))
        {
            pending = false;
            return TryGetMoonlightOpeningWorldBoundsAtomic(gridUid, biome, grid, localBounds, out openingWorldBounds);
        }

        IncludeMoonlightStreamingBudget(streaming);
        pending = false;

        var requiredFirstChunk = (localBounds.BottomLeft / ChunkSize).Floored();
        var requiredLastChunk = (localBounds.TopRight / ChunkSize).Floored();
        var exactFirstTile = new Vector2i(
            (int) MathF.Floor(localBounds.Left) - 1,
            (int) MathF.Floor(localBounds.Bottom) - 1);
        var exactLastTile = new Vector2i(
            (int) MathF.Floor(localBounds.Right) + 1,
            (int) MathF.Floor(localBounds.Top) + 1);
        var openingChunkSize = _moonlightOpeningCache.ChunkSize;
        var firstOpeningChunk = SharedMapSystem.GetChunkIndices(exactFirstTile, openingChunkSize);
        var lastOpeningChunk = SharedMapSystem.GetChunkIndices(exactLastTile, openingChunkSize);
        var openingChunkWidth = (long) lastOpeningChunk.X - firstOpeningChunk.X + 1L;
        var openingChunkHeight = (long) lastOpeningChunk.Y - firstOpeningChunk.Y + 1L;
        if (openingChunkWidth <= 0L ||
            openingChunkHeight <= 0L ||
            openingChunkWidth > MaxMoonlightOpeningSnapshotChunks ||
            openingChunkHeight > MaxMoonlightOpeningSnapshotChunks ||
            openingChunkWidth * openingChunkHeight > MaxMoonlightOpeningSnapshotChunks)
        {
            openingWorldBounds = default;
            return false;
        }

        var firstTile = firstOpeningChunk * openingChunkSize;
        var lastTile = (lastOpeningChunk + Vector2i.One) * openingChunkSize - Vector2i.One;
        var key = new MoonlightBiomeOpeningScanKey(
            requiredFirstChunk,
            requiredLastChunk,
            firstTile,
            lastTile);
        var revision = _moonlightOpeningCache.GetRevision((gridUid, grid));

        MoonlightBiomeOpeningScanState? scan = null;
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

            scan = new MoonlightBiomeOpeningScanState(
                requiredFirstChunk,
                requiredLastChunk,
                firstTile,
                lastTile,
                revision,
                streaming.OpeningScanGeneration,
                _moonlightTiming.CurTime);
            scan.NextOpeningChunk = firstOpeningChunk;
            if (!TryAdmitMoonlightOpeningScan(streaming, key, scan))
            {
                pending = true;
                openingWorldBounds = default;
                return false;
            }
        }

        scan.LastRequestGeneration = streaming.OpeningScanGeneration;
        scan.LastRequestTime = _moonlightTiming.CurTime;
        if (scan.Phase == MoonlightBiomeOpeningScanPhase.Complete)
            return TryGetMoonlightOpeningSnapshotBounds(
                gridUid,
                grid,
                scan,
                exactFirstTile,
                exactLastTile,
                openingChunkSize,
                out openingWorldBounds);

        pending = true;

        if (scan.Phase == MoonlightBiomeOpeningScanPhase.WaitingForChunks &&
            scan.NextChunk.X <= scan.RequiredLastChunk.X &&
            !biome.LoadedChunks.Contains(scan.NextChunk * ChunkSize))
        {
            openingWorldBounds = default;
            return false;
        }

        if (!TryBeginMoonlightOpeningOperation(out var forced))
        {
            openingWorldBounds = default;
            return false;
        }

        var maxCells = Math.Clamp(streaming.OpeningCellsPerSlice, 1, MoonlightChunkCells);
        var processed = 0;

        while (scan.Phase == MoonlightBiomeOpeningScanPhase.WaitingForChunks &&
               scan.NextChunk.X <= scan.RequiredLastChunk.X)
        {
            var chunkOrigin = scan.NextChunk * ChunkSize;
            if (!biome.LoadedChunks.Contains(chunkOrigin))
            {
                openingWorldBounds = default;
                return false;
            }

            var hasNext = AdvanceMoonlightOpeningCursor(
                ref scan.NextChunk,
                scan.RequiredFirstChunk,
                scan.RequiredLastChunk);
            processed++;
            if (!hasNext)
                scan.Phase = MoonlightBiomeOpeningScanPhase.Scanning;

            if (processed >= maxCells ||
                forced ||
                MoonlightStreamingBudgetExpired())
            {
                openingWorldBounds = default;
                return false;
            }
        }

        if (scan.Phase != MoonlightBiomeOpeningScanPhase.Scanning ||
            scan.NextOpeningChunk.X > lastOpeningChunk.X)
        {
            openingWorldBounds = default;
            return false;
        }

        while (scan.NextOpeningChunk.X <= lastOpeningChunk.X && processed < maxCells)
        {
            if (processed > 0 && (forced || MoonlightStreamingBudgetExpired()))
                break;

            var openingChunk = scan.NextOpeningChunk;
            var openingTile = openingChunk * openingChunkSize;
            var guardedChunk = SharedMapSystem.GetChunkIndices(openingTile, ChunkSize) * ChunkSize;
            var openingMask = 0UL;
            var cached = true;
            if (biome.LoadedChunks.Contains(guardedChunk))
            {
                cached = _moonlightOpeningCache.TryGetCachedChunkOpeningMask(
                    (gridUid, grid),
                    openingChunk,
                    out openingMask);
                if (!cached)
                {
                    if (processed > 0)
                        break;

                    if (!_moonlightOpeningCache.TryGetChunkOpeningMask(
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
            var hasNextOpeningChunk = AdvanceMoonlightOpeningCursor(
                ref scan.NextOpeningChunk,
                firstOpeningChunk,
                lastOpeningChunk);
            processed += cached ? 1 : maxCells;

            if (_moonlightOpeningCache.GetRevision((gridUid, grid)) != scan.Revision)
            {
                streaming.OpeningScans.Remove(key);
                openingWorldBounds = default;
                return false;
            }

            if (!hasNextOpeningChunk)
            {
                scan.Phase = MoonlightBiomeOpeningScanPhase.Complete;
                pending = false;
                return TryGetMoonlightOpeningSnapshotBounds(
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

    private bool TryGetMoonlightOpeningSnapshotBounds(
        EntityUid gridUid,
        MapGridComponent grid,
        MoonlightBiomeOpeningScanState scan,
        Vector2i exactFirstTile,
        Vector2i exactLastTile,
        int openingChunkSize,
        out Box2 openingWorldBounds)
    {
        var gridWorldMatrix = _transform.GetWorldMatrix(gridUid);
        var tileSize = grid.TileSize;
        var queryWorldBounds = gridWorldMatrix.TransformBox(new Box2(
            (Vector2) exactFirstTile * tileSize, (Vector2) (exactLastTile + Vector2i.One) * tileSize));
        var coveringGrids = _moonlightOpeningGrids;
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
                if (checkCoverage && MoonlightZLevelOpeningCache.IsOpeningCovered(
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

    private static bool AdvanceMoonlightOpeningCursor(
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

    private bool TryGetMoonlightOpeningWorldBoundsAtomic(
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
        var coveringGrids = _moonlightOpeningGrids;
        coveringGrids.Clear();
        _mapSystem.FindGridsIntersecting(mapId, worldBounds, ref coveringGrids,
            approx: true, includeMap: true);
        Func<Vector2i, bool>? includeOpening = coveringGrids.Count > 1
            ? tile => !MoonlightZLevelOpeningCache.IsOpeningCovered((gridUid, grid), tile,
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
            if (!_moonlightOpeningCache.TryGetOpeningBounds(
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

    private bool TryGetMoonlightMapBiome(
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

    private void OnMoonlightTileChanged(ref TileChangedEvent args)
    {
        _moonlightOpeningCache.InvalidateTiles(args.Entity, args.Changes);
        if (_moonlightStreamingQuery.TryComp(args.Entity.Owner, out var streaming) &&
            streaming.OpeningScans.Count > 0)
        {
            if (args.Changes.Length == 0)
            {
                streaming.OpeningScans.Clear();
            }
            else
            {
                var revision = _moonlightOpeningCache.GetRevision(args.Entity);
                _moonlightStaleOpeningScans.Clear();
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
                        _moonlightStaleOpeningScans.Add(key);
                    else
                        scan.Revision = revision;
                }

                foreach (var key in _moonlightStaleOpeningScans)
                    streaming.OpeningScans.Remove(key);
            }
        }

        if (!_biomeQuery.TryComp(args.Entity.Owner, out var biome))
            return;

        if (_moonlightBiomeTileWrites.Contains(args.Entity.Owner))
            return;

        foreach (var change in args.Changes)
        {
            var chunkOrigin = SharedMapSystem.GetChunkIndices(change.GridIndices, ChunkSize) * ChunkSize;
            biome.ModifiedTiles.GetOrNew(chunkOrigin).Add(change.GridIndices);
        }
    }

    private void SetMoonlightBiomeTile(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        Tile tile)
    {
        _moonlightBiomeTileWrites.Add(gridUid);
        try
        {
            _mapSystem.SetTile(gridUid, grid, indices, tile);
        }
        finally
        {
            _moonlightBiomeTileWrites.Remove(gridUid);
        }
    }

    private void SetMoonlightBiomeTiles(
        EntityUid gridUid,
        MapGridComponent grid,
        List<(Vector2i Index, Tile Tile)> tiles)
    {
        _moonlightBiomeTileWrites.Add(gridUid);
        try
        {
            _mapSystem.SetTiles(gridUid, grid, tiles);
        }
        finally
        {
            _moonlightBiomeTileWrites.Remove(gridUid);
        }
    }

    private bool MoonlightBiomeLoadsEntities(EntityUid gridUid)
    {
        return !_moonlightUndergroundQuery.TryComp(gridUid, out var underground) || underground.LoadEntities;
    }

    private bool MoonlightBiomeLoadsDecals(EntityUid gridUid)
    {
        return !_moonlightUndergroundQuery.TryComp(gridUid, out var underground) || underground.LoadDecals;
    }

    private int MoonlightBiomeDepth(EntityUid gridUid)
    {
        return _moonlightZMapQuery.TryComp(Transform(gridUid).MapUid, out var zMap) ? zMap.Depth : 0;
    }

    private bool MoonlightHasBlockingAnchoredEntity(
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

    private bool MoonlightCanUnloadTrackedBiomeEntity(
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
        return MoonlightCanUnloadBiomeEntity(gridUid, uid, depth, rootAnchor);
    }

    private bool MoonlightCanUnloadMarkerEntity(
        MoonlightBiomeMarkerCellState marker,
        EntityUid gridUid,
        EntityUid uid,
        int depth)
    {
        if (marker.Root != uid || marker.BaselineComponents == null)
            return MoonlightCanUnloadBiomeEntity(gridUid, uid, depth, strippedGhostRole: uid == marker.Root);

        if (marker.BaselineComponentNames == null || marker.BaselineChildren == null ||
            marker.BaselineFactions == null || marker.BaselineTags == null ||
            marker.BaselineSolutionContainers == null)
        {
            return false;
        }

        if (!TryComp<MoonlightBiomeAmbientMarkerComponent>(uid, out var ambient) || ambient.PlayerTouched ||
            HasComp<ActorComponent>(uid) ||
            TryComp<MindContainerComponent>(uid, out var mind) && (mind.HasMind || mind.Mind != null) ||
            _moonlightDamageableQuery.TryComp(uid, out var damageable) && damageable.TotalDamage != 0 ||
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

        CollectMoonlightMarkerChildren(uid, _moonlightMarkerChildren);
        if (!_moonlightMarkerChildren.SetEquals(marker.BaselineChildren))
            return false;
        CaptureMoonlightMarkerFactions(uid, _moonlightMarkerFactions);
        if (!_moonlightMarkerFactions.SetEquals(marker.BaselineFactions))
            return false;
        CaptureMoonlightMarkerTags(uid, _moonlightMarkerTags);
        if (!_moonlightMarkerTags.SetEquals(marker.BaselineTags))
            return false;
        _moonlightMarkerSolutionContainers.Clear();
        if (TryComp<SolutionContainerManagerComponent>(uid, out var solutions))
        {
            _moonlightMarkerSolutionContainers.UnionWith(solutions.Containers);
            if (solutions.Solutions != null)
                return false;
        }
        if (!_moonlightMarkerSolutionContainers.SetEquals(marker.BaselineSolutionContainers))
            return false;

        var baseline = marker.BaselineComponents;
        var componentNames = marker.BaselineComponentNames;
        var remaining = componentNames.Count;
        foreach (var component in AllComps(uid))
        {
            if (component.Deleted)
                return false;
            if (component is MetaDataComponent or TransformComponent or MoonlightBiomeAmbientMarkerComponent)
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

            MoonlightSynchronizeMarkerSnapshotClocks(component, expected);
            if (!_moonlightSerialization.DataFieldEquals(type, component, expected))
                return false;
        }

        return remaining == 0;
    }

#pragma warning disable RA0002
    private static void MoonlightSynchronizeMarkerSnapshotClocks(IComponent component, IComponent snapshot)
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

    private bool MoonlightCanUnloadBiomeEntity(
        EntityUid gridUid,
        EntityUid uid,
        int depth,
        bool rootAnchor = false,
        bool strippedGhostRole = false)
    {
        if (_moonlightAlwaysUnloadQuery.HasComponent(uid))
            return true;

        if (!_moonlightStreamingQuery.HasComp(gridUid))
            return EntityManager.IsDefault(uid);

        if (_moonlightDamageableQuery.TryComp(uid, out var damageable) && damageable.TotalDamage != 0)
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
                    !_moonlightSerialization.DataFieldEquals(gatherable.ToolWhitelist, prototypeGatherable.ToolWhitelist) ||
                    !_moonlightSerialization.DataFieldEquals(gatherable.Loot, prototypeGatherable.Loot) ||
                    !_moonlightSerialization.DataFieldEquals(gatherable.ToolQualities, prototypeGatherable.ToolQualities))
                {
                    return false;
                }

                continue;
            }

            if (component is RandomSpriteComponent randomSprite &&
                entry.Component is RandomSpriteComponent prototypeRandomSprite)
            {
                if (randomSprite.GetAllGroups != prototypeRandomSprite.GetAllGroups ||
                    !_moonlightSerialization.DataFieldEquals(randomSprite.Available, prototypeRandomSprite.Available) ||
                    !_moonlightSerialization.DataFieldEquals(randomSprite.HeldPrefixes, prototypeRandomSprite.HeldPrefixes) ||
                    prototypeRandomSprite.Selected.Count > 0 &&
                    !_moonlightSerialization.DataFieldEquals(randomSprite.Selected, prototypeRandomSprite.Selected))
                {
                    return false;
                }

                continue;
            }

            var expected = entry.Component;
            if (component is MoonlightZPhysicsComponent && expected is MoonlightZPhysicsComponent prototypePhysics)
            {
                var key = (prototype, depth);
                if (!_moonlightZPhysicsDefaults.TryGetValue(key, out var physics))
                {
                    physics = _moonlightSerialization.CreateCopy(prototypePhysics, notNullableOverride: true);
                    physics.CurrentZLevel = depth;
                    _moonlightZPhysicsDefaults[key] = physics;
                }
                expected = physics;
            }

            if (!_moonlightSerialization.DataFieldEquals(type, component, expected))
                return false;
        }

        return remaining == 0;
    }

    private bool MoonlightCanUnloadBiomeEntity(EntityUid gridUid, EntityUid uid)
    {
        return MoonlightCanUnloadBiomeEntity(gridUid, uid, MoonlightBiomeDepth(gridUid));
    }

    private bool MoonlightHasPersistentBiomeDependent(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        EntityUid biomeEntity)
    {
        if (!_moonlightGeyserOutletQuery.HasComp(biomeEntity))
            return false;

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var other))
        {
            if (other.Value != biomeEntity && _moonlightGeyserGeneratorQuery.HasComp(other.Value))
                return true;
        }

        return false;
    }

    private Dictionary<Vector2i, EntityUid> GetMoonlightReplaceableMarkerEntities(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Box2i bounds)
    {
        var result = new Dictionary<Vector2i, EntityUid>();
        var depth = MoonlightBiomeDepth(gridUid);
        foreach (var loaded in biome.LoadedEntities.Values)
        {
            foreach (var (entity, tile) in loaded)
            {
                if (!bounds.Contains(tile) || Deleted(entity) ||
                    !_xformQuery.TryGetComponent(entity, out var xform) || !xform.Anchored ||
                    _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != tile ||
                    MoonlightHasPersistentBiomeDependent(gridUid, grid, tile, entity) ||
                    !MoonlightCanUnloadBiomeEntity(gridUid, entity, depth))
                {
                    continue;
                }

                result.TryAdd(tile, entity);
            }
        }

        return result;
    }

    private bool TryRemoveMoonlightMarkerBaseline(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i chunk,
        Vector2i node)
    {
        if (!biome.LoadedEntities.TryGetValue(chunk, out var loaded))
            return true;

        _moonlightUnloadEntities.Clear();
        foreach (var (entity, tile) in loaded)
        {
            if (tile != node)
                continue;
            _moonlightUnloadEntities.Add((entity, tile));
        }

        if (_moonlightUnloadEntities.Count == 0)
            return true;

        var depth = MoonlightBiomeDepth(gridUid);
        foreach (var (baseline, _) in _moonlightUnloadEntities)
        {
            if (Deleted(baseline) || !_xformQuery.TryGetComponent(baseline, out var xform) ||
                _mapSystem.LocalToTile(gridUid, grid, xform.Coordinates) != node ||
                MoonlightHasPersistentBiomeDependent(gridUid, grid, node, baseline) ||
                !MoonlightCanUnloadTrackedBiomeEntity(biome, gridUid, grid, node, baseline, depth))
            {
                return false;
            }
        }

        foreach (var (baseline, _) in _moonlightUnloadEntities)
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
            TryGetMoonlightMapBiome(fallbackMap, worldPos, out gridUid, out biome, out grid))
        {
            return true;
        }

        gridUid = default;
        biome = null;
        grid = null;
        return false;
    }

    /// <summary>
    /// Compatibility overloads keep the upstream update fallback buildable. The Moonlight hook
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

    private bool TryUpdateMoonlightBiomeStreaming(float frameTime)
    {
        var moonlightBiomes = EntityQueryEnumerator<MoonlightBiomeStreamingComponent>();
        if (!moonlightBiomes.MoveNext(out _, out _))
            return false;

        var biomes = AllEntityQuery<BiomeComponent>();

        while (biomes.MoveNext(out var biome))
        {
            if (biome.LifeStage < ComponentLifeStage.Running)
                continue;

            _activeChunks.Add(biome, _tilePool.Get());
            if (_moonlightStreamingQuery.TryComp(biome.Owner, out var streaming))
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

        BeginMoonlightStreamingBudget();
        PrecollectMoonlightViewerStreamingBudgets();
        _moonlightStreamingWorkStarted = Stopwatch.GetTimestamp();
        _moonlightViewRequests.Clear();

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
            var attachedWorldPos = MoonlightBiomeViewerPosition(
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
                    MoonlightBiomeViewerScale(pSession.AttachedEntity.Value),
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

            AddMoonlightAdjacentLandingChunks(pSession.AttachedEntity.Value, attachedXform, attachedBodyPos);
            AddMoonlightVehicleExitSafety(pSession.AttachedEntity.Value);

            foreach (var viewer in pSession.ViewSubscriptions)
                _moonlightViewRequests.Add((pSession, viewer, attachedXform));
        }

        var viewRequestCount = _moonlightViewRequests.Count;
        var viewRequestStart = viewRequestCount == 0
            ? 0
            : _moonlightViewRequestStartIndex % viewRequestCount;
        for (var i = 0; i < viewRequestCount; i++)
        {
            var (pSession, viewer, attachedXform) =
                _moonlightViewRequests[(viewRequestStart + i) % viewRequestCount];
            if (!_handledEntities.Add(viewer) ||
                !_xformQuery.TryGetComponent(viewer, out var viewerXform) ||
                !CanLoad(viewer))
            {
                continue;
            }

            var worldPos = MoonlightBiomeViewerPosition(pSession.AttachedEntity, viewer, viewerXform);
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

            var pvsScale = MoonlightBiomeViewerScale(viewer);
            if (!MoonlightTryGetViewerRange(
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
            _moonlightViewRequestStartIndex = (viewRequestStart + 1) % viewRequestCount;

        var loadBiomes = AllEntityQuery<BiomeComponent, MapGridComponent>();
        _moonlightStreamingBiomes.Clear();

        while (loadBiomes.MoveNext(out var gridUid, out var biome, out var grid))
        {
            if (biome.LifeStage < ComponentLifeStage.Running)
                continue;

            if (!biome.Enabled)
                continue;

            if (_moonlightStreamingQuery.TryComp(gridUid, out var streaming))
            {
                PruneMoonlightOpeningScans(streaming);
                _moonlightStreamingBiomes.Add((gridUid, biome, grid, streaming));
                continue;
            }

            LoadChunks(biome, gridUid, grid, biome.Seed);
            UnloadChunks(biome, gridUid, grid, biome.Seed);
        }

        ProcessMoonlightStreamingBiomes();

        _handledEntities.Clear();
        _moonlightHandledVehicleAnchors.Clear();
        _moonlightVehicleExitCoordinates.Clear();

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
        MoonlightBiomeStreamingComponent? streaming = null;
        var loadArea = GetLocalBiomeViewBounds(gridUid, grid, worldPos, pvsScale, worldClip);

        if (_moonlightStreamingQuery.TryComp(biome.Owner, out streaming))
        {
            IncludeMoonlightStreamingBudget(streaming);
            var priorityTile = loadArea.Center.Floored();
            var viewerChunk = SharedMapSystem.GetChunkIndices(priorityTile, ChunkSize) * ChunkSize;
            if (!streaming.ViewerCenters.Contains(viewerChunk))
                streaming.ViewerCenters.Add(viewerChunk);
            if (physicalViewer is { } viewerUid && physicalViewerXform != null)
            {
                var tile = _mapSystem.WorldToTile(gridUid, grid, physicalWorldPos ?? worldPos);
                var physicalChunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
                streaming.ViewerChunks.Add(physicalChunk);
                AddMoonlightViewerSafetyCells(
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
    private void AddMoonlightViewerSafetyCells(
        EntityUid viewerUid,
        TransformComponent viewerXform,
        EntityUid gridUid,
        MapGridComponent grid,
        MoonlightBiomeStreamingComponent streaming,
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

        var predictionSeconds = (float) _moonlightTiming.TickPeriod.TotalSeconds *
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
        var range = _moonlightLoadRange * MathF.Max(pvsScale, 0.1f) / tileSize;
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

    private static bool MoonlightMarkerRequestsComplete(
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

    private void PruneMoonlightMarkerAreas(
        BiomeComponent biome,
        MoonlightBiomeStreamingComponent streaming,
        Dictionary<string, HashSet<Vector2i>>? requested)
    {
        _moonlightMarkerAreas.Clear();
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

                if (MoonlightMarkerAreaHasResidentChunk(biome, streaming, markerChunk, layerProto.Size))
                    continue;

                _moonlightMarkerAreas.Add((layer, markerChunk));
            }
        }

        foreach (var (layer, markerChunk) in _moonlightMarkerAreas)
        {
            if (biome.LoadedMarkers.TryGetValue(layer, out var loaded))
            {
                loaded.Remove(markerChunk);
                if (loaded.Count == 0)
                    biome.LoadedMarkers.Remove(layer);
            }

            var layerProto = ProtoManager.Index<BiomeMarkerLayerPrototype>(layer);
            if (MoonlightMarkerAreaHasModification(biome, markerChunk, layerProto.Size))
                streaming.PinnedMarkerAreas.GetOrNew(layer).Add(markerChunk);
            else
                RemoveMoonlightPendingMarkerArea(biome, layer, markerChunk, layerProto.Size);
        }
    }

    private static bool MoonlightMarkerAreaHasModification(
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

    private static bool MoonlightMarkerAreaHasResidentChunk(
        BiomeComponent biome,
        MoonlightBiomeStreamingComponent streaming,
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

    private static void RemoveMoonlightPendingMarkerArea(
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
    /// Advances one deterministic Moonlight marker-area snapshot. The generic biome path above
    /// intentionally retains its original parallel, atomic <see cref="GetMarkerNodes"/> call.
    /// </summary>
    private void BuildMoonlightMarkerChunk(
        BiomeComponent component,
        EntityUid gridUid,
        MapGridComponent grid,
        int seed,
        Dictionary<string, HashSet<Vector2i>> markers,
        MoonlightBiomeStreamingComponent streaming)
    {
        var scan = streaming.MarkerAreaScan;
        if (scan != null && !MoonlightMarkerRequestExists(component, markers, scan, streaming))
        {
            DisposeMoonlightMarkerScan(scan);
            streaming.MarkerAreaScan = null;
            scan = null;
        }

        var continuation = scan != null;
        if (!TryBeginMoonlightBoundedOperation(continuation))
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
                        found && CompareMoonlightMarkerChunks(candidate, markerChunk) >= 0)
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

                scan = new MoonlightBiomeMarkerAreaScanState(
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
        var cellsPerSlice = Math.Clamp(streaming.MarkerAreaCellsPerSlice, 1, MoonlightChunkCells);
        var totalCells = scan.Bounds.Width * scan.Bounds.Height;
        var processed = 0;
        var depth = MoonlightBiomeDepth(gridUid);

        while (scan.Phase == MoonlightBiomeMarkerAreaScanPhase.Scanning &&
               scan.NextCell < totalCells &&
               processed < cellsPerSlice)
        {
            var x = scan.NextCell / scan.Bounds.Height;
            var y = scan.NextCell % scan.Bounds.Height;
            var node = new Vector2i(scan.Bounds.Left + x, scan.Bounds.Bottom + y);
            ScanMoonlightMarkerNode(component, gridUid, grid, layerPrototype, scan, node, depth);
            scan.NextCell++;
            processed++;

            if (MoonlightStreamingBudgetExpired())
                return;
        }

        if (scan.Phase == MoonlightBiomeMarkerAreaScanPhase.Scanning)
        {
            if (scan.NextCell < totalCells)
                return;

            scan.Phase = MoonlightBiomeMarkerAreaScanPhase.Selecting;
            if (processed >= cellsPerSlice || MoonlightStreamingBudgetExpired())
                return;
        }

        if (!AdvanceMoonlightMarkerSelection(scan, layerPrototype, cellsPerSlice, ref processed))
            return;

        PublishMoonlightMarkerAreaScan(component, scan);
        DisposeMoonlightMarkerScan(scan);
        streaming.MarkerAreaScan = null;
    }

    private static void DisposeMoonlightMarkerScan(MoonlightBiomeMarkerAreaScanState? scan)
    {
        if (scan is not { SeedPickActive: true })
            return;

        scan.SeedEnumerator.Dispose();
        scan.SeedPickActive = false;
    }

    private static bool MoonlightMarkerRequestExists(
        BiomeComponent component,
        Dictionary<string, HashSet<Vector2i>> markers,
        MoonlightBiomeMarkerAreaScanState scan,
        MoonlightBiomeStreamingComponent streaming)
    {
        return markers.TryGetValue(scan.Layer, out var requested) &&
               requested.Contains(scan.MarkerChunk) &&
               (!streaming.PinnedMarkerAreas.TryGetValue(scan.Layer, out var pinned) ||
                !pinned.Contains(scan.MarkerChunk)) &&
               (!component.LoadedMarkers.TryGetValue(scan.Layer, out var completed) ||
                !completed.Contains(scan.MarkerChunk)) &&
               component.ForcedMarkerLayers.Contains(scan.Layer) == scan.Forced;
    }

    private static int CompareMoonlightMarkerChunks(Vector2i a, Vector2i b)
    {
        var result = a.X.CompareTo(b.X);
        return result != 0 ? result : a.Y.CompareTo(b.Y);
    }

    private void ScanMoonlightMarkerNode(
        BiomeComponent biome,
        EntityUid gridUid,
        MapGridComponent grid,
        BiomeMarkerLayerPrototype layerProto,
        MoonlightBiomeMarkerAreaScanState scan,
        Vector2i node,
        int depth)
    {
        var replaceable = GetMoonlightReplaceableMarkerEntity(biome, gridUid, grid, node, depth);
        MoonlightBiomeMarkerCellState? marker = null;
        var chunk = SharedMapSystem.GetChunkIndices(node, ChunkSize) * ChunkSize;
        if (_moonlightStreamingQuery.TryComp(gridUid, out var streaming) &&
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

    private EntityUid GetMoonlightReplaceableMarkerEntity(
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
                MoonlightHasPersistentBiomeDependent(gridUid, grid, node, entity) ||
                !MoonlightCanUnloadBiomeEntity(gridUid, entity, depth))
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
    private bool AdvanceMoonlightMarkerSelection(
        MoonlightBiomeMarkerAreaScanState scan,
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

                if (processed >= maxOperations || MoonlightStreamingBudgetExpired())
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

                if (processed >= maxOperations || MoonlightStreamingBudgetExpired())
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
                    DisposeMoonlightMarkerScan(scan);
                    throw new InvalidOperationException("Moonlight marker seed enumeration ended before its selected index.");
                }

                var startNode = scan.SeedEnumerator.Current;
                var selected = scan.SeedPickCursor++ == scan.SeedPickTarget;
                processed++;
                if (selected)
                {
                    DisposeMoonlightMarkerScan(scan);
                    scan.RemainingTiles.Remove(startNode);
                    scan.Frontier.Clear();
                    scan.Frontier.Add(startNode);
                }

                if (processed >= maxOperations || MoonlightStreamingBudgetExpired())
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
            if (processed >= maxOperations || MoonlightStreamingBudgetExpired())
                return false;
        }

        return true;
    }

    /// <summary>
    /// Publishes a completed marker snapshot atomically. Until this point cancellation has no
    /// gameplay-visible effects, which is important because view requests are rebuilt each tick.
    /// </summary>
    private void PublishMoonlightMarkerAreaScan(
        BiomeComponent component,
        MoonlightBiomeMarkerAreaScanState scan)
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
    /// Applies at most <paramref name="maxNodes"/> precomputed marker nodes. Moonlight streaming
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
        var moonlightStreaming = _moonlightStreamingQuery.TryComp(gridUid, out var streaming);
        _moonlightMarkerLayers.Clear();
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
                    var hasMarker = moonlightStreaming &&
                                    streaming!.MarkerCells.TryGetValue(chunk, out var existingMarkers) &&
                                    existingMarkers.ContainsKey(node);
                    if (!hasMarker)
                    {
                        if (moonlightStreaming &&
                            !TryRemoveMoonlightMarkerBaseline(component, gridUid, grid, chunk, node))
                        {
                            modified.Add(node);
                        }
                        else
                        {
                            if (TryGetBiomeTile(node, component.Layers, seed, (gridUid, grid), out var tile))
                            {
                                SetMoonlightBiomeTile(gridUid, grid, node, tile.Value);
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

                            if (moonlightStreaming && prototype != null)
                            {
                                var marker = new MoonlightBiomeMarkerCellState
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
                                    loaded = SpawnMoonlightBiomeMarkerEntity(
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
                if (moonlightStreaming && MoonlightStreamingBudgetExpired())
                    break;
            }

            if (consumed > 0)
            {
                nodes.RemoveRange(0, consumed);
                processed += consumed;
            }

            if (nodes.Count == 0)
                _moonlightMarkerLayers.Add(layer);
            if (processed >= maxNodes || moonlightStreaming && MoonlightStreamingBudgetExpired())
                break;
        }

        foreach (var layer in _moonlightMarkerLayers)
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
    /// Loads a contiguous range of cells in a biome chunk. Moonlight background streaming uses
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
        bool stopWhenMoonlightBudgetExpires = false,
        int moonlightEntitySpawnLimit = int.MaxValue)
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
        var cacheMoonlightTiles = _moonlightStreamingQuery.HasComp(gridUid);
        var appliedTiles = false;

        using (_moonlightProfiler.Value("MoonlightBiome.TileSlice"))
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

                if (cacheMoonlightTiles)
                    _chunkBiomeTiles[cell] = biomeTile.Value;
                _tiles.Add((indices, biomeTile.Value));
            }

            appliedTiles = _tiles.Count > 0;
            using (_moonlightProfiler.Value("MoonlightBiome.TileApply"))
                SetMoonlightBiomeTiles(gridUid, grid, _tiles);
            _tiles.Clear();
        }

        if (stopWhenMoonlightBudgetExpires && appliedTiles && MoonlightStreamingBudgetExpired())
            return startCell;

        var processedEnd = endCell;
        if (MoonlightBiomeLoadsEntities(gridUid))
        {
            using (_moonlightProfiler.Value("MoonlightBiome.EntitySlice"))
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

                    if (_moonlightBackgroundEntitySpawns >= moonlightEntitySpawnLimit)
                    {
                        break;
                    }

                    using (_moonlightProfiler.Value("MoonlightBiome.EntitySpawn"))
                        loadedEntities = SpawnMoonlightBiomeEntity(
                            entPrototype,
                            gridUid,
                            grid,
                            indices,
                            loadedEntities);
                    if (moonlightEntitySpawnLimit != int.MaxValue)
                        RecordMoonlightBackgroundEntitySpawns(gridUid, 1);

                    if (loadedEntities != null)
                        component.LoadedEntities[chunk] = loadedEntities;

                    CellComplete:
                    processedEnd = cell + 1;
                    if (stopWhenMoonlightBudgetExpires && MoonlightStreamingBudgetExpired())
                        break;
                }
            }
        }

        if (MoonlightBiomeLoadsDecals(gridUid))
        {
            using (_moonlightProfiler.Value("MoonlightBiome.DecalSlice"))
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
