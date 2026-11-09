using Content.Server._Classic.ZCollapse;
using Content.Server._Classic.ZLevels.Core;
using Content.Server._Classic.ZLevels.Mapping;
using Content.Server._Classic.ZLevels.Roof;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Station.Components;
using Content.Shared._Classic.Station.Components;
using Content.Shared._Classic.ZLevels.Roof;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Classic.Station;

/// <summary>
/// Configures the surface and data-driven underground terrain grids for Classic stations.
/// </summary>
public sealed partial class ClassicStationBiomeSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ClassicZLevelsSystem _zLevels = default!;
    [Dependency] private ClassicZLevelMappingSystem _zMapping = default!;
    [Dependency] private ClassicZLevelsRoofSystem _zLevelsRoof = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The Z network is loaded synchronously. Running afterwards lets terrain be attached once,
        // without a retrying Update loop or map-wide scans every tick.
        SubscribeLocalEvent<ClassicStationBiomeComponent, StationPostInitEvent>(OnStationPostInit,
            after: [typeof(ClassicZLevelsSystem)]);
    }

    private void OnStationPostInit(Entity<ClassicStationBiomeComponent> ent, ref StationPostInitEvent args)
    {
        var surfaceGridUid = _station.GetLargestGrid(args.Station.Owner);
        if (surfaceGridUid == null ||
            !TryComp<MapGridComponent>(surfaceGridUid.Value, out var surfaceGrid) ||
            Transform(surfaceGridUid.Value).MapUid is not { } surfaceMapUid)
        {
            return;
        }

        var seed = ent.Comp.Seed ?? _random.Next();

        SetupBiome(surfaceGridUid.Value, ent.Comp.Biome, seed);

        if (TryComp<MapComponent>(surfaceMapUid, out var surfaceMapComp))
        {
            foreach (var grid in _map.GetAllGrids(surfaceMapComp.MapId))
            {
                SetupTerrainGrid(grid.Owner, grid.Comp, ent.Comp.DisableGridSplitting);
            }
        }
        else
        {
            SetupTerrainGrid(surfaceGridUid.Value, surfaceGrid, ent.Comp.DisableGridSplitting);
        }

        SetupSurfaceMap(surfaceMapUid, ent.Comp);

        if (!_zLevels.TryGetMapNetwork(surfaceMapUid, out var network))
            return;

        SetupUpperConstructionLevel(surfaceMapUid, ent.Comp);

        ClassicZMapNetworkComponent? networkComponent = network.Comp;
        var configuredDepths = new HashSet<int>();
        foreach (var level in ent.Comp.UndergroundLevels)
        {
            if (level.Depth >= 0 || !configuredDepths.Add(level.Depth))
            {
                Log.Error($"Invalid or duplicate Classic underground depth {level.Depth} on {ToPrettyString(ent)}.");
                continue;
            }

            if (!_zLevels.TryGetMapAtDepth((network.Owner, networkComponent), level.Depth, out var mapUid) ||
                !TryGetTerrainGrid(mapUid, out var grid))
            {
                Log.Error($"Classic underground map/grid at depth {level.Depth} was not loaded.");
                continue;
            }

            SetupUndergroundLevel(mapUid, grid, level, seed, ent.Comp.DisableGridSplitting);
        }

        _zMapping.TrySynchronizePlanetaryLighting(network);
    }

    private void SetupUpperConstructionLevel(EntityUid surfaceMapUid, ClassicStationBiomeComponent component)
    {
        if (!_zLevels.TryMapUp(surfaceMapUid, out var upperMap))
            return;

        if (TryComp<MapComponent>(upperMap.Owner, out var map))
        {
            foreach (var upperGrid in _map.GetAllGrids(map.MapId))
            {
                SetupTerrainGrid(upperGrid.Owner, upperGrid.Comp, component.DisableGridSplitting);
            }
        }

        SetupSurfaceMap(upperMap.Owner, component);
    }

    private void SetupUndergroundLevel(
        EntityUid mapUid,
        Entity<MapGridComponent> grid,
        ClassicUndergroundLevelData level,
        int seed,
        bool disableGridSplitting)
    {
        var marker = EnsureComp<ClassicUndergroundBiomeComponent>(grid.Owner);
        EnsureComp<StationAuxiliaryGridComponent>(grid.Owner);
        marker.Depth = level.Depth;
        marker.LoadEntities = level.LoadEntities;
        marker.LoadDecals = level.LoadDecals;

        // Natural terrain is deliberately outside the construction-collapse network.
        RemComp<ClassicGridStabilityComponent>(grid.Owner);

        SetupBiome(grid.Owner, level.Biome, seed);
        SetupTerrainGrid(grid.Owner, grid.Comp, disableGridSplitting);
        SetupUndergroundMap(mapUid, level);
    }

    private bool TryGetTerrainGrid(EntityUid mapUid, out Entity<MapGridComponent> grid)
    {
        if (TryComp<MapGridComponent>(mapUid, out var mapGrid))
        {
            grid = (mapUid, mapGrid);
            return true;
        }

        grid = default;
        if (!TryComp<MapComponent>(mapUid, out var map))
            return false;

        var largestChunkCount = -1;
        foreach (var candidate in _map.GetAllGrids(map.MapId))
        {
            if (candidate.Comp.ChunkCount <= largestChunkCount)
                continue;

            largestChunkCount = candidate.Comp.ChunkCount;
            grid = candidate;
        }

        return grid.Owner != EntityUid.Invalid;
    }

    private void SetupBiome(EntityUid gridUid, ProtoId<BiomeTemplatePrototype> template, int seed)
    {
        EnsureComp<ClassicBiomeStreamingComponent>(gridUid);
        var biome = EnsureComp<BiomeComponent>(gridUid);
        _biome.SetSeed(gridUid, biome, seed, false);
        _biome.SetTemplate(gridUid, biome, _proto.Index(template));
    }

    private void SetupTerrainGrid(EntityUid gridUid, MapGridComponent grid, bool disableGridSplitting)
    {
        if (disableGridSplitting && grid.CanSplit)
        {
            grid.CanSplit = false;
            Dirty(gridUid, grid);
        }

        var gravity = EnsureComp<GravityComponent>(gridUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(gridUid, gravity);

        EnsureComp<RoofComponent>(gridUid);
        EnsureComp<ClassicZLevelRoofComponent>(gridUid);
        RemCompDeferred<ImplicitRoofComponent>(gridUid);

        EnsureComp<SunShadowComponent>(gridUid);
        EnsureComp<SunShadowCycleComponent>(gridUid);
    }

    private void SetupSurfaceMap(EntityUid mapUid, ClassicStationBiomeComponent component)
    {
        SetMapLight(mapUid, component.MapLightColor);

        var cycle = EnsureComp<LightCycleComponent>(mapUid);
        if (cycle.OriginalColor == Color.Transparent)
            cycle.OriginalColor = component.MapLightColor;
        cycle.InitialOffset = false;
        Dirty(mapUid, cycle);

        var parallax = component.Parallax;
        if (string.IsNullOrEmpty(parallax) && TryComp<ParallaxComponent>(mapUid, out var existingParallax) && !string.IsNullOrEmpty(existingParallax.Parallax))
            parallax = existingParallax.Parallax;
        if (string.IsNullOrEmpty(parallax))
            parallax = "Dirt";

        SetupMapEnvironment(mapUid, Atmospherics.T20C, parallax);
    }

    private void SetupUndergroundMap(EntityUid mapUid, ClassicUndergroundLevelData level)
    {
        _zLevelsRoof.SetMapRoofAmbient(mapUid, level.MapLightColor);
        SetupMapEnvironment(mapUid, level.Temperature, level.Parallax);
    }

    private void SetMapLight(EntityUid mapUid, Color color)
    {
        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = color;
        Dirty(mapUid, light);
    }

    private void SetupMapEnvironment(EntityUid mapUid, float temperature, string parallaxName = "Dirt")
    {
        var parallax = EnsureComp<ParallaxComponent>(mapUid);
        parallax.Parallax = parallaxName;
        Dirty(mapUid, parallax);

        _atmosphere.SetMapAtmosphere(mapUid, false, CreatePlanetAtmosphere(temperature));
    }

    private static GasMixture CreatePlanetAtmosphere(float temperature)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        return new GasMixture(moles, temperature);
    }
}
