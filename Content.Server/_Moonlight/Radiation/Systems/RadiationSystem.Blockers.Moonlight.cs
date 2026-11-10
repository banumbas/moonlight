// Namespace does not match folder structure.
#pragma warning disable IDE0130

using Content.Server.Radiation.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Radiation.Systems;

public partial class RadiationSystem
{
    private void AddMoonlightTile(EntityUid uid, RadiationBlockerComponent component)
    {
        if (!component.Enabled || component.RadResistance <= 0)
        {
            RemoveMoonlightTile(component);
            return;
        }

        var xform = Transform(uid);
        if (!xform.Anchored || !TryComp(xform.GridUid, out MapGridComponent? grid))
        {
            RemoveMoonlightTile(component);
            return;
        }

        var gridUid = xform.GridUid.Value;
        var tile = _maps.TileIndicesFor((gridUid, grid), xform.Coordinates);
        if (component.CurrentPosition == (gridUid, tile) &&
            component.RegisteredResistance.Equals(component.RadResistance))
        {
            return;
        }

        RemoveMoonlightTile(component);
        AddToTile(gridUid, tile, component.RadResistance);
        component.CurrentPosition = (gridUid, tile);
        component.RegisteredResistance = component.RadResistance;
    }

    private void RemoveMoonlightTile(RadiationBlockerComponent component)
    {
        if (component.CurrentPosition is not { } position)
            return;

        RemoveFromTile(position.Grid, position.Tile, component.RegisteredResistance);
        component.CurrentPosition = null;
        component.RegisteredResistance = 0f;
    }
}
