/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Content.Shared._Moonlight.ZLevels.Roof;
using Content.Shared.Light.Components;
using Content.Shared.Maps;

namespace Content.Server._Moonlight.ZLevels.Roof;

public sealed partial class MoonlightZLevelsRoofSystem
{
    [Dependency] private EntityQuery<MoonlightZGridComponent> _zgridQuery = default!;
    [Dependency] private EntityQuery<MoonlightZGridNetworkComponent> _zGridNetworkQuery = default!;

    private void InitGrids()
    {
        SubscribeLocalEvent<MoonlightZGridComponent, MapInitEvent>(OnZGridMapInit);

        SubscribeLocalEvent<MoonlightZGridNetworkComponent, MoonlightZLevelGridNetworkUpdatedEvent>(OnZGridNetworkUpdate);
    }

    private void OnZGridNetworkUpdate(Entity<MoonlightZGridNetworkComponent> ent, ref MoonlightZLevelGridNetworkUpdatedEvent args)
    {
        RecalculateGridRoofs(ent);
    }

    private void OnZGridMapInit(Entity<MoonlightZGridComponent> ent, ref MapInitEvent args)
    {
        EnsureComp<MoonlightZLevelRoofComponent>(ent.Owner);
    }

    public void RecalculateGridRoofs(Entity<MoonlightZGridNetworkComponent> network)
    {
        _roofMap.Clear();

        var sorted = network.Comp.Grids
            .Select(g => (Grid: g, Depth: ZLevel.TryGetGridZDepth(g)))
            .Where(x => x.Depth.HasValue)
            .OrderByDescending(x => x.Depth!.Value);

        foreach (var (gridUid, _) in sorted)
        {
            RemCompDeferred<ImplicitRoofComponent>(gridUid); //hack but that way we dont need edit vanilla code

            if (!GridQuery.TryComp(gridUid, out var grid))
                continue;
            var roofComp = EnsureComp<RoofComponent>(gridUid);
            var enumerator = Map.GetAllTilesEnumerator(gridUid, grid);

            while (enumerator.MoveNext(out var tileRef))
            {
                var worldTile = ZLevel.GridTileToWorldTile(gridUid, grid, tileRef.Value.GridIndices);

                Roof.SetRoof((gridUid, grid, roofComp),
                    tileRef.Value.GridIndices,
                    _roofMap.Contains(worldTile));

                var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Value.Tile.TypeId];
                if (!tileDef.Transparent)
                    _roofMap.Add(worldTile);
            }
        }
    }
}
