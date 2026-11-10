/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Moonlight.ZLevels.Core;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Roof;
using Robust.Shared.Map.Components;

namespace Content.Server._Moonlight.ZLevels.Mapping;

public sealed partial class MoonlightZLevelMappingSystem : EntitySystem
{
    [Dependency] private MoonlightZLevelsSystem _zLevels = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializePlanetaryLighting();
        SubscribeLocalEvent<MoonlightZMapComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MoonlightZMapComponent, MoonlightMapAddedIntoZNetworkEvent>(OnAddedIntoZNetwork);
    }

    private void OnAddedIntoZNetwork(Entity<MoonlightZMapComponent> ent, ref MoonlightMapAddedIntoZNetworkEvent args)
    {
        if (_map.IsInitialized(ent))
        {
            EntityManager.AddComponents(ent, args.Network.Comp.Components);
            EnrollExistingPlanetaryGrids(ent.Owner, args.Network.Comp);
            TrySynchronizePlanetaryLighting(args.Network);
        }
        else
        {
            var hasInitializedMaps = false;
            foreach (var existingMapUid in args.Network.Comp.ZLevels.Values)
            {
                if (existingMapUid.HasValue && _map.IsInitialized(existingMapUid.Value))
                {
                    hasInitializedMaps = true;
                    break;
                }
            }

            if (hasInitializedMaps)
                _map.InitializeMap(ent.Owner);
        }
    }

    private void OnMapInit(Entity<MoonlightZMapComponent> ent, ref MapInitEvent args)
    {
        if (!_zLevels.TryGetMapNetwork(ent, out var network))
            return;

        EntityManager.AddComponents(ent, network.Comp.Components);
        EnrollExistingPlanetaryGrids(ent.Owner, network.Comp);
        TrySynchronizePlanetaryLighting(network);
    }

    private void EnrollExistingPlanetaryGrids(EntityUid mapUid, MoonlightZMapNetworkComponent network)
    {
        if (!IsPlanetaryNetwork(network) ||
            !TryComp<MapComponent>(mapUid, out var map))
        {
            return;
        }

        foreach (var grid in _map.GetAllGrids(map.MapId))
            EnsureComp<MoonlightZLevelRoofComponent>(grid.Owner);
    }

    /// <summary>
    /// Enrolls grids which already existed when a completed map batch joined a planetary network.
    /// This second pass makes enrollment independent of the maps' insertion order.
    /// </summary>
    public void EnrollExistingPlanetaryGrids(Entity<MoonlightZMapNetworkComponent> network)
    {
        if (!IsPlanetaryNetwork(network.Comp))
            return;

        foreach (var mapUid in network.Comp.SortedZLevels)
        {
            if (mapUid.IsValid())
                EnrollExistingPlanetaryGrids(mapUid, network.Comp);
        }
    }
}
