/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Classic.ZLevels.Core;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Roof;
using Robust.Shared.Map.Components;

namespace Content.Server._Classic.ZLevels.Mapping;

public sealed partial class ClassicZLevelMappingSystem : EntitySystem
{
    [Dependency] private ClassicZLevelsSystem _zLevels = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializePlanetaryLighting();
        SubscribeLocalEvent<ClassicZMapComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClassicZMapComponent, ClassicMapAddedIntoZNetworkEvent>(OnAddedIntoZNetwork);
    }

    private void OnAddedIntoZNetwork(Entity<ClassicZMapComponent> ent, ref ClassicMapAddedIntoZNetworkEvent args)
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

    private void OnMapInit(Entity<ClassicZMapComponent> ent, ref MapInitEvent args)
    {
        if (!_zLevels.TryGetMapNetwork(ent, out var network))
            return;

        EntityManager.AddComponents(ent, network.Comp.Components);
        EnrollExistingPlanetaryGrids(ent.Owner, network.Comp);
        TrySynchronizePlanetaryLighting(network);
    }

    private void EnrollExistingPlanetaryGrids(EntityUid mapUid, ClassicZMapNetworkComponent network)
    {
        if (!IsPlanetaryNetwork(network) ||
            !TryComp<MapComponent>(mapUid, out var map))
        {
            return;
        }

        foreach (var grid in _map.GetAllGrids(map.MapId))
            EnsureComp<ClassicZLevelRoofComponent>(grid.Owner);
    }

    /// <summary>
    /// Enrolls grids which already existed when a completed map batch joined a planetary network.
    /// This second pass makes enrollment independent of the maps' insertion order.
    /// </summary>
    public void EnrollExistingPlanetaryGrids(Entity<ClassicZMapNetworkComponent> network)
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
