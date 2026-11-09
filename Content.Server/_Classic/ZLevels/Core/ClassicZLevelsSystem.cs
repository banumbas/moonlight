/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Classic.ZLevels.Core.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Content.Shared.Gravity;
using Content.Shared.Station.Components;
using Content.Shared._Classic.Station.Components;

namespace Content.Server._Classic.ZLevels.Core;

public sealed partial class ClassicZLevelsSystem : ClassicSharedZLevelsSystem
{
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private StationSystem _station = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitView();

        SubscribeLocalEvent<ClassicStationZLevelsComponent, StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<ClassicStationZLevelsComponent, EntityTerminatingEvent>(OnStationTerminating);
        SubscribeLocalEvent<ClassicZMapComponent, EntityTerminatingEvent>(OnZMapTerminating);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ShutdownView();
    }

    private void OnStationTerminating(Entity<ClassicStationZLevelsComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.ZNetworkEntity is { } netUid && Exists(netUid))
        {
            QueueDel(netUid);
        }
    }

    private void OnZMapTerminating(Entity<ClassicZMapComponent> ent, ref EntityTerminatingEvent args)
    {
        var netUid = ent.Comp.NetworkUid;
        ent.Comp.MapAbove = null;
        ent.Comp.MapBelow = null;
        ent.Comp.NetworkUid = EntityUid.Invalid;

        if (netUid != EntityUid.Invalid && TryComp<ClassicZMapNetworkComponent>(netUid, out var netComp))
        {
            RemoveMapFromNetwork((netUid, netComp), ent.Owner);
        }
    }

    private void OnStationPostInit(Entity<ClassicStationZLevelsComponent> ent, ref StationPostInitEvent args)
    {
        if (ent.Comp.MapsAbove.Count == 0 && ent.Comp.MapsBelow.Count == 0)
            return;

        var stationName = MetaData(ent).EntityName;
        var stationNetwork = CreateMapNetwork(ent.Comp.ZLevelsComponentOverrides);

        ent.Comp.ZNetworkEntity = stationNetwork;

        _meta.SetEntityName(ent.Comp.ZNetworkEntity.Value, $"Station z-Network: {stationName}");

        var mainGrid = _station.GetLargestGrid(ent.Owner);
        if (mainGrid is null)
            throw new Exception("Station has no grids to base z-levels off of!");

        var mainMapEnt = Transform(mainGrid.Value).MapUid
            ?? throw new Exception("Station main grid has no parent map entity!");

        var hasGravity = TryComp<GravityComponent>(mainMapEnt, out var baseGravity);

        var dict = new Dictionary<EntityUid, int>()
        {
            { mainMapEnt, 0 }
        };

        // Loading maps below first
        var depth = ent.Comp.MapsBelow.Count * -1;
        foreach (var mapBelow in ent.Comp.MapsBelow)
        {
            if (!_mapLoader.TryLoadMap(mapBelow, out var mapEnt, out _))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            _map.InitializeMap(mapEnt.Value.Comp.MapId);

            if (hasGravity)
            {
                var grav = EnsureComp<GravityComponent>(mapEnt.Value);
                grav.Enabled = baseGravity!.Enabled;
                Dirty(mapEnt.Value, grav);
            }

            _meta.SetEntityName(mapEnt.Value, $"{stationName} [{depth}]");
            if (_mapGridQuery.HasComp(mapEnt.Value))
            {
                // Underground maps belong to the station for tracking/chat, but are not playable
                // station target grids. Keeping them out of StationData.Grids prevents procedural
                // exploration from affecting events, FTL destinations and station tile counts.
                var stationMember = EnsureComp<StationMemberComponent>(mapEnt.Value);
                stationMember.Station = ent.Owner;
                EnsureComp<StationAuxiliaryGridComponent>(mapEnt.Value);
                var stationData = Comp<StationDataComponent>(ent.Owner);
                stationData.AuxiliaryGrids.Add(mapEnt.Value);
                Dirty(ent.Owner, stationData);
                Dirty(mapEnt.Value, stationMember);
            }
            dict.Add(mapEnt.Value, depth);
            depth++;
        }

        // Loading maps above next
        depth = 1;
        foreach (var mapAbove in ent.Comp.MapsAbove)
        {
            if (!_mapLoader.TryLoadMap(mapAbove, out var mapEnt, out _))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            _map.InitializeMap(mapEnt.Value.Comp.MapId);

            if (hasGravity)
            {
                var grav = EnsureComp<GravityComponent>(mapEnt.Value);
                grav.Enabled = baseGravity!.Enabled;
                Dirty(mapEnt.Value, grav);
            }

            _meta.SetEntityName(mapEnt.Value, $"{stationName} [{depth}]");
            if (_mapGridQuery.HasComp(mapEnt.Value)) //Adding zlevel map to station, if it is planetmap
                _station.AddGridToStation(ent, mapEnt.Value);
            dict.Add(mapEnt.Value, depth);
            depth++;
        }

        TryAddMapsIntoNetwork(stationNetwork, dict);
    }

    /// <summary>
    /// Initializes all uninitialized maps in the z-network.
    /// </summary>
    [PublicAPI]
    public void InitializeZNetwork(Entity<ClassicZMapNetworkComponent> network)
    {
        foreach (var (_, mapUid) in network.Comp.ZLevels)
        {
            if (!TryComp<MapComponent>(mapUid, out var mapComp))
            {
                Log.Error($"Map entity {mapUid} does not have MapComponent.");
                continue;
            }

            if (!_map.MapExists(mapComp.MapId))
            {
                Log.Error($"Map with ID {mapComp.MapId} does not exist.");
                continue;
            }

            if (_map.IsInitialized(mapComp.MapId))
            {
                Log.Debug($"Map with ID {mapComp.MapId} is already initialized.");
                continue;
            }

            _map.InitializeMap(mapComp.MapId);
            Log.Info($"Map with ID {mapComp.MapId} initialized.");
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateView(frameTime);
    }
}
