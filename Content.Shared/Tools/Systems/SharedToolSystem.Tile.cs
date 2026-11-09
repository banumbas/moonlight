using Content.Shared.Database;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Tools.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Shared.Tools.Systems;

public abstract partial class SharedToolSystem
{
    [Dependency] private INetManager _net = default!;

    public void InitializeTile()
    {
        SubscribeLocalEvent<ToolTileCompatibleComponent, AfterInteractEvent>(OnToolTileAfterInteract);
        SubscribeLocalEvent<ToolTileCompatibleComponent, TileToolDoAfterEvent>(OnToolTileComplete);
    }

    private void OnToolTileAfterInteract(Entity<ToolTileCompatibleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target != null && !HasComp<PuddleComponent>(args.Target))
            return;

        args.Handled = UseToolOnTile((ent, ent, null), args.User, args.ClickLocation);
    }

    private void OnToolTileComplete(Entity<ToolTileCompatibleComponent> ent, ref TileToolDoAfterEvent args)
    {
        var comp = ent.Comp;
        if (args.Handled || args.Cancelled)
            return;

        if (!TryComp<ToolComponent>(ent, out var tool))
            return;

        var gridUid = GetEntity(args.Grid);
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
        {
            Log.Error("Attempted use tool on a non-existent grid?");
            return;
        }

        var tileRef = _maps.GetTileRef(gridUid, grid, args.GridTile);
        var coords = _maps.ToCoordinates(tileRef, grid);
        if (comp.RequiresUnobstructed && _turfs.IsTileBlocked(gridUid, tileRef.GridIndices, CollisionGroup.MobMask))
            return;

        // Classic-Start
        if (!TryDeconstructWithToolQualities(tileRef, tool.Qualities, ent.Owner, args.User))
            return;
        // Classic-End

        AdminLogger.Add(
            LogType.LatticeCut,
            LogImpact.Medium,
            $"{ToPrettyString(args.User):player} used {ToPrettyString(ent)} to edit the tile at {coords}");
        args.Handled = true;
    }

    private bool UseToolOnTile(Entity<ToolTileCompatibleComponent?, ToolComponent?> ent, EntityUid user, EntityCoordinates clickLocation)
    {
        if (!Resolve(ent, ref ent.Comp1, ref ent.Comp2, false))
            return false;

        var comp = ent.Comp1!;
        var tool = ent.Comp2!;

        if (!_maps.TryFindGridAt(_transformSystem.ToMapCoordinates(clickLocation), out var gridUid, out var mapGrid))
            return false;

        var tileRef = _maps.GetTileRef(gridUid, mapGrid, clickLocation);
        var tileDef = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];

        if (!tool.Qualities.ContainsAny(tileDef.DeconstructTools))
            return false;

        if (string.IsNullOrWhiteSpace(tileDef.BaseTurf))
            return false;

        // Classic-Start
        if (!CanDigClassicTile(tileRef, tool.Qualities, ent.Owner, user))
            return false;
        // Classic-End

        if (comp.RequiresUnobstructed && _turfs.IsTileBlocked(gridUid, tileRef.GridIndices, CollisionGroup.MobMask))
            return false;

        var coordinates = _maps.GridTileToLocal(gridUid, mapGrid, tileRef.GridIndices);
        if (!InteractionSystem.InRangeUnobstructed(user, coordinates, popup: false))
            return false;

        var args = new TileToolDoAfterEvent(GetNetEntity(gridUid), tileRef.GridIndices);
        UseTool(ent, user, ent, comp.Delay, tool.Qualities, args, out _, toolComponent: tool);
        return true;
    }

    public bool TryDeconstructWithToolQualities(
        TileRef tileRef,
        PrototypeFlags<ToolQualityPrototype> withToolQualities,
        EntityUid? toolUid = null,
        EntityUid? user = null)
    {
        // Classic-Start
        if (!CanDigClassicTile(tileRef, withToolQualities, toolUid, user))
            return false;
        // Classic-End

        var tileDef = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];
        if (withToolQualities.ContainsAny(tileDef.DeconstructTools))
        {
            // don't do this on the client or else the tile entity spawn mispredicts and looks horrible
            var deconstructed = _net.IsClient || _tiles.DeconstructTile(tileRef); // classic-edit z-levels digging
            // classic-start z-levels digging
            if (deconstructed &&
                !_net.IsClient &&
                tileDef.DeconstructTools.Contains(DiggingQuality) &&
                withToolQualities.Contains(DiggingQuality) &&
                TryComp<MapGridComponent>(tileRef.GridUid, out var grid) &&
                _maps.GetTileRef(tileRef.GridUid, grid, tileRef.GridIndices).Tile.IsEmpty)
            {
                RaiseLocalEvent(new ClassicDiggingTileDeconstructedEvent(tileRef.GridUid, tileRef.GridIndices));
            }

            return deconstructed;
            // classic-end
        }
        return false;
    }
}
