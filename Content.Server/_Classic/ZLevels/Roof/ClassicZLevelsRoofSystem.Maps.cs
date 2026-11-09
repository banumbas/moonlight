/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Content.Server._Classic.ZLevels.Core;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Roof;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Classic.ZLevels.Roof;

public sealed partial class ClassicZLevelsRoofSystem
{
    private void InitMaps()
    {
        SubscribeLocalEvent<ClassicZMapNetworkComponent, ClassicZLevelMapNetworkUpdatedEvent>(OnMapNetworkUpdated);
        SubscribeLocalEvent<ClassicZMapComponent, ClassicMapRemovedFromZNetworkEvent>(OnMapRemovedFromNetwork);
        SubscribeLocalEvent<ClassicZLevelRoofComponent, ComponentStartup>(OnMapRoofGridStartup);
        SubscribeLocalEvent<GridStartupEvent>(OnMapGridStartup);
        SubscribeLocalEvent<GridRemovalEvent>(OnMapGridRemoved);
        SubscribeLocalEvent<ClassicZLevelMapNetworkReadyEvent>(OnMapNetworkReady);
    }

    private void OnMapNetworkUpdated(Entity<ClassicZMapNetworkComponent> ent, ref ClassicZLevelMapNetworkUpdatedEvent args)
    {
        RecalculateMapRoofs(ent);
    }

    private void OnMapNetworkReady(ClassicZLevelMapNetworkReadyEvent args)
    {
        if (!TryComp<ClassicZMapNetworkComponent>(args.Network, out var network))
            return;

        _recalculatingMapRoofs = true;
        try
        {
            _zMapping.EnrollExistingPlanetaryGrids((args.Network, network));
        }
        finally
        {
            _recalculatingMapRoofs = false;
        }

        RecalculateMapRoofs((args.Network, network));
    }

    private void OnMapRemovedFromNetwork(Entity<ClassicZMapComponent> ent, ref ClassicMapRemovedFromZNetworkEvent args)
    {
        if (!TryComp<MapComponent>(ent, out var map))
            return;

        foreach (var grid in Map.GetAllGrids(map.MapId))
        {
            _planetaryGridMaps.Remove(grid.Owner);

            if (!HasComp<ClassicZLevelRoofComponent>(grid.Owner) ||
                !TryComp<RoofComponent>(grid.Owner, out var roof) ||
                roof.Data.Count == 0)
            {
                continue;
            }

            roof.Data.Clear();
            Dirty(grid.Owner, roof);
        }
    }

    private void OnMapRoofGridStartup(Entity<ClassicZLevelRoofComponent> ent, ref ComponentStartup args)
    {
        if (_recalculatingMapRoofs ||
            !TryComp<MapGridComponent>(ent, out _) ||
            !TryComp<TransformComponent>(ent, out var xform) ||
            xform.MapUid is not { } mapUid ||
            !ZLevel.TryGetMapNetwork(mapUid, out var network))
        {
            return;
        }

        RecalculateMapRoofs(network);
    }

    private void OnMapGridStartup(GridStartupEvent args)
    {
        if (_recalculatingMapRoofs ||
            !HasComp<ClassicZLevelRoofComponent>(args.EntityUid) ||
            !TryComp<TransformComponent>(args.EntityUid, out var xform) ||
            xform.MapUid is not { } mapUid ||
            !ZLevel.TryGetMapNetwork(mapUid, out var network))
        {
            return;
        }

        RecalculateMapRoofs(network);
    }

    private void OnMapGridRemoved(GridRemovalEvent args)
    {
        if (_recalculatingMapRoofs)
        {
            _planetaryGridMaps.Remove(args.EntityUid);
            return;
        }

        EntityUid mapUid;
        if (!_planetaryGridMaps.Remove(args.EntityUid, out mapUid))
        {
            if (!HasComp<ClassicZLevelRoofComponent>(args.EntityUid) ||
                !TryComp<TransformComponent>(args.EntityUid, out var xform) ||
                xform.MapUid is not { } currentMapUid)
            {
                return;
            }

            mapUid = currentMapUid;
        }

        if (TerminatingOrDeleted(mapUid) ||
            !ZLevel.TryGetMapNetwork(mapUid, out var network) ||
            TerminatingOrDeleted(network.Owner))
        {
            return;
        }

        RecalculateMapRoofs(network);
    }

    public void SetMapRoofAmbient(EntityUid mapUid, Color color)
    {
        var ambient = EnsureComp<ClassicZLevelRoofAmbientComponent>(mapUid);
        ambient.Color = color;

        if (!TryComp<MapComponent>(mapUid, out var map))
            return;

        foreach (var grid in Map.GetAllGrids(map.MapId))
        {
            if (!HasComp<ClassicZLevelRoofComponent>(grid.Owner))
                continue;

            SetGridRoofAmbient(grid.Owner, color);
        }
    }

    private void RecalculateMapRoofs(Entity<ClassicZMapNetworkComponent> network)
    {
        _roofMap.Clear();
        _recalculatingMapRoofs = true;

        try
        {
            foreach (var map in network.Comp.ZLevels
                         .OrderByDescending(kv => kv.Key)
                         .Select(kv => kv.Value)
                         .Where(uid => uid.HasValue)
                         .Select(uid => uid!.Value))
            {
                if (!TryComp<MapComponent>(map, out var mapComponent))
                    continue;

                _opaqueOnMapLevel.Clear();

                foreach (var grid in Map.GetAllGrids(mapComponent.MapId))
                {
                    var gridUid = grid.Owner;
                    if (!HasComp<ClassicZLevelRoofComponent>(gridUid))
                        continue;

                    _planetaryGridMaps[gridUid] = map;
                    var roofComp = EnsureComp<RoofComponent>(gridUid);
                    if (TryComp<ClassicZLevelRoofAmbientComponent>(map, out var ambient) &&
                        roofComp.Color != ambient.Color)
                    {
                        roofComp.Color = ambient.Color;
                        Dirty(gridUid, roofComp);
                    }

                    RemCompDeferred<ImplicitRoofComponent>(gridUid);

                    if (roofComp.Data.Count != 0)
                    {
                        roofComp.Data.Clear();
                        Dirty(gridUid, roofComp);
                    }

                    var enumerator = Map.GetAllTilesEnumerator(gridUid, grid.Comp);
                    while (enumerator.MoveNext(out var tileRef))
                    {
                        var worldTile = ZLevel.GridTileToWorldTile(gridUid, grid.Comp, tileRef.Value.GridIndices);
                        Roof.SetRoof((gridUid, grid.Comp, roofComp),
                            tileRef.Value.GridIndices,
                            _roofMap.Contains(worldTile));

                        if (tileRef.Value.Tile.IsEmpty)
                            continue;

                        var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Value.Tile.TypeId];
                        if (!tileDef.Transparent)
                            _opaqueOnMapLevel.Add(worldTile);
                    }
                }

                _roofMap.UnionWith(_opaqueOnMapLevel);
            }
        }
        finally
        {
            _recalculatingMapRoofs = false;
        }

        _zMapping.TrySynchronizePlanetaryLighting(network);
    }

    private void SetGridRoofAmbient(EntityUid gridUid, Color color)
    {
        var roof = EnsureComp<RoofComponent>(gridUid);
        if (roof.Color == color)
            return;

        roof.Color = color;
        Dirty(gridUid, roof);
    }
}
