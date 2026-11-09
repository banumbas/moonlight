/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Classic.ZLevels.Core;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Roof;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Classic.ZLevels.Mapping;

public sealed partial class ClassicZLevelMappingSystem
{
    [Dependency] private SharedMapSystem _map = default!;

    private static readonly Color PlanetaryLightColor = Color.FromHex("#D8B059");
    private bool _synchronizingPlanetaryLighting;

    private void InitializePlanetaryLighting()
    {
        SubscribeLocalEvent<ClassicZMapComponent, LightCycleOffsetEvent>(
            OnPlanetaryLightCycleOffset,
            after: [typeof(Content.Server.Light.EntitySystems.SunShadowSystem)]);
    }

    private void OnPlanetaryLightCycleOffset(Entity<ClassicZMapComponent> ent, ref LightCycleOffsetEvent args)
    {
        if (!_zLevels.TryGetMapNetwork(ent, out var network) ||
            !IsPlanetaryNetwork(network.Comp) ||
            !TryGetReferenceMap(network.Comp, out var referenceMap))
            return;

        // A map initialized after the rest of the network may generate its own random offset.
        // Only the reference map is authoritative; re-copy it instead of shifting every level.
        if (ent.Owner != referenceMap)
        {
            TrySynchronizePlanetaryLighting(network);
            return;
        }

        foreach (var mapUid in network.Comp.SortedZLevels)
        {
            if (!mapUid.IsValid())
                continue;

            SetLightOffset(mapUid, args.Offset);

            if (!TryComp<MapComponent>(mapUid, out var map))
                continue;

            foreach (var grid in _map.GetAllGrids(map.MapId))
            {
                if (HasComp<ClassicZLevelRoofComponent>(grid.Owner))
                    SetShadowOffset(grid.Owner, args.Offset);
            }
        }
    }

    /// <summary>
    /// Makes every planetary map and enrolled grid in a Z-network use one day/night cycle.
    /// Roof masks then replace that shared ambient light only where an upper level blocks the sky.
    /// </summary>
    public bool TrySynchronizePlanetaryLighting(
        Entity<ClassicZMapNetworkComponent> network,
        Color? originalColor = null)
    {
        // Adding LightCycle to an initialized non-reference map catches up MapInit immediately.
        // Its offset event must not recursively start another whole-network synchronization while
        // this pass is still configuring that map.
        if (_synchronizingPlanetaryLighting)
            return true;

        if (!IsPlanetaryNetwork(network.Comp) ||
            !TryGetReferenceMap(network.Comp, out var referenceMap))
        {
            return false;
        }

        _synchronizingPlanetaryLighting = true;
        try
        {
            TryComp<MapLightComponent>(referenceMap, out var existingLight);
            TryComp<LightCycleComponent>(referenceMap, out var existingCycle);
            var color = originalColor ?? GetPlanetaryLightColor(existingLight, existingCycle);

            // Set the fallback before adding LightCycle. If this map is already initialized, the
            // component's catch-up MapInit copies MapLight into OriginalColor immediately.
            var referenceLight = EnsureComp<MapLightComponent>(referenceMap);
            referenceLight.AmbientLightColor = color;
            Dirty(referenceMap, referenceLight);

            var referenceCycle = EnsureComp<LightCycleComponent>(referenceMap);
            referenceCycle.OriginalColor = color;
            referenceCycle.InitialOffset = false;
            Dirty(referenceMap, referenceCycle);

            EnsureComp<SunShadowComponent>(referenceMap);
            var referenceShadowCycle = EnsureComp<SunShadowCycleComponent>(referenceMap);
            referenceShadowCycle.Duration = referenceCycle.Duration;
            referenceShadowCycle.Offset = referenceCycle.Offset;
            Dirty(referenceMap, referenceShadowCycle);

            foreach (var mapUid in network.Comp.SortedZLevels)
            {
                if (!mapUid.IsValid())
                    continue;

                ConfigurePlanetaryMap(mapUid, referenceCycle, referenceShadowCycle, color);
            }

            return true;
        }
        finally
        {
            _synchronizingPlanetaryLighting = false;
        }
    }

    private void ConfigurePlanetaryMap(
        EntityUid mapUid,
        LightCycleComponent referenceCycle,
        SunShadowCycleComponent referenceShadowCycle,
        Color originalColor)
    {
        var mapLight = EnsureComp<MapLightComponent>(mapUid);
        mapLight.AmbientLightColor = originalColor;
        Dirty(mapUid, mapLight);

        var cycle = EnsureComp<LightCycleComponent>(mapUid);
        CopyLightCycle(referenceCycle, cycle, originalColor);
        Dirty(mapUid, cycle);

        EnsureComp<SunShadowComponent>(mapUid);
        var shadowCycle = EnsureComp<SunShadowCycleComponent>(mapUid);
        CopyShadowCycle(referenceShadowCycle, shadowCycle, cycle);
        Dirty(mapUid, shadowCycle);

        if (!TryComp<MapComponent>(mapUid, out var map))
            return;

        foreach (var grid in _map.GetAllGrids(map.MapId))
        {
            if (HasComp<ClassicZLevelRoofComponent>(grid.Owner))
                ConfigurePlanetaryGrid(grid.Owner, referenceCycle, referenceShadowCycle);
        }
    }

    private void ConfigurePlanetaryGrid(
        EntityUid gridUid,
        LightCycleComponent referenceCycle,
        SunShadowCycleComponent referenceShadowCycle)
    {
        EnsureComp<RoofComponent>(gridUid);
        RemCompDeferred<ImplicitRoofComponent>(gridUid);
        EnsureComp<SunShadowComponent>(gridUid);

        var shadowCycle = EnsureComp<SunShadowCycleComponent>(gridUid);
        CopyShadowCycle(referenceShadowCycle, shadowCycle, referenceCycle);
        Dirty(gridUid, shadowCycle);
    }

    private bool IsPlanetaryNetwork(ClassicZMapNetworkComponent network)
    {
        foreach (var mapUid in network.SortedZLevels)
        {
            if (!mapUid.IsValid())
                continue;

            if (HasComp<ClassicZLevelRoofComponent>(mapUid))
                return true;

            if (!TryComp<MapComponent>(mapUid, out var map))
                continue;

            foreach (var grid in _map.GetAllGrids(map.MapId))
            {
                if (HasComp<ClassicZLevelRoofComponent>(grid.Owner))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetReferenceMap(ClassicZMapNetworkComponent network, out EntityUid mapUid)
    {
        if (network.ZLevels.TryGetValue(0, out var levelZero) && levelZero is { } validLevelZero)
        {
            mapUid = validLevelZero;
            return true;
        }

        for (var i = network.SortedZLevels.Count - 1; i >= 0; i--)
        {
            var candidate = network.SortedZLevels[i];
            if (!candidate.IsValid())
                continue;

            mapUid = candidate;
            return true;
        }

        mapUid = EntityUid.Invalid;
        return false;
    }

    private static Color GetPlanetaryLightColor(MapLightComponent? mapLight, LightCycleComponent? cycle)
    {
        if (cycle is not null && cycle.OriginalColor != Color.Transparent)
            return cycle.OriginalColor;

        return mapLight is not null && mapLight.AmbientLightColor != MapLightComponent.DefaultColor
            ? mapLight.AmbientLightColor
            : PlanetaryLightColor;
    }

    private static void CopyLightCycle(
        LightCycleComponent source,
        LightCycleComponent target,
        Color originalColor)
    {
        target.OriginalColor = originalColor;
        target.Duration = source.Duration;
        target.Offset = source.Offset;
        target.Enabled = source.Enabled;
        target.InitialOffset = false;
        target.MinLightLevel = source.MinLightLevel;
        target.MaxLightLevel = source.MaxLightLevel;
        target.ClipLight = source.ClipLight;
        target.ClipLevel = source.ClipLevel;
        target.MinLevel = source.MinLevel;
        target.MaxLevel = source.MaxLevel;
    }

    private static void CopyShadowCycle(
        SunShadowCycleComponent source,
        SunShadowCycleComponent target,
        LightCycleComponent lightCycle)
    {
        target.Duration = lightCycle.Duration;
        target.Offset = lightCycle.Offset;

        if (ReferenceEquals(source, target))
            return;

        target.Directions.Clear();
        target.Directions.AddRange(source.Directions);
    }

    private void SetLightOffset(EntityUid mapUid, TimeSpan offset)
    {
        if (TryComp<LightCycleComponent>(mapUid, out var cycle))
        {
            cycle.Offset = offset;
            cycle.InitialOffset = false;
            Dirty(mapUid, cycle);
        }

        SetShadowOffset(mapUid, offset);
    }

    private void SetShadowOffset(EntityUid uid, TimeSpan offset)
    {
        if (!TryComp<SunShadowCycleComponent>(uid, out var shadowCycle))
            return;

        shadowCycle.Offset = offset;
        Dirty(uid, shadowCycle);
    }
}
