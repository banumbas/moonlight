/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Content.Server._Classic.PVS;
using Content.Shared._Classic.ZLevels.Core.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Classic.ZLevels.Core;

public sealed partial class ClassicZLevelsSystem
{
    /// <summary>
    /// Creates a new zLevel Map Network entity
    /// </summary>
    [PublicAPI]
    public Entity<ClassicZMapNetworkComponent> CreateMapNetwork(ComponentRegistry? components = null)
    {
        var ent = Spawn();

        var zLevel = EnsureComp<ClassicZMapNetworkComponent>(ent);
        EnsureComp<ClassicPvsOverrideComponent>(ent);

        zLevel.Components = components ?? new ComponentRegistry();

        return (ent, zLevel);
    }

    /// <summary>
    /// Attempts to add the specified maps to the zNetwork network at the specified depths
    /// </summary>
    [PublicAPI]
    public bool TryAddMapsIntoNetwork(Entity<ClassicZMapNetworkComponent> network, Dictionary<EntityUid, int> maps)
    {
        var success = true;
        foreach (var (mapUid, depth) in maps)
        {
            if (TryGetMapNetwork(mapUid, out var otherNetwork))
            {
                Log.Error($"Failed attempt to add map {mapUid} to ZLevelNetwork {network}: This map is already in another network {otherNetwork}.");
                success = false;
                continue;
            }

            if (network.Comp.ZLevels.ContainsKey(depth))
            {
                Log.Error($"Failed to add map {mapUid} to ZLevelNetwork {network}: This depth is already occupied.");
                success = false;
                continue;
            }

            if (network.Comp.ZLevels.ContainsValue(mapUid))
            {
                Log.Error($"Failed attempt to add map {mapUid} to ZLevelNetwork {network} at depth {depth}: This map is already in this network.");
                success = false;
                continue;
            }

            network.Comp.ZLevels[depth] = mapUid;
            network.Comp.ZLevelByEntity[mapUid] = depth;

            Dirty(network);

            // Welcome to fast api code
            QuickApiCache(network, mapUid, depth);

            var levelMapComponent = EnsureComp<ClassicZMapComponent>(mapUid);
            levelMapComponent.Depth = depth;
            levelMapComponent.NetworkUid = network;

            if (network.Comp.ZLevels.TryGetValue(depth + 1, out var aboveMapUid))
            {
                levelMapComponent.MapAbove = aboveMapUid;

                if (aboveMapUid is { } aboveUid && TryComp<ClassicZMapComponent>(aboveUid, out var aboveMapComponent))
                {
                    aboveMapComponent.MapBelow = mapUid;
                    Dirty(aboveUid, aboveMapComponent);
                }
            }

            if (network.Comp.ZLevels.TryGetValue(depth - 1, out var belowMapUid))
            {
                levelMapComponent.MapBelow = belowMapUid;

                if (belowMapUid is { } belowUid && TryComp<ClassicZMapComponent>(belowUid, out var belowMapComponent))
                {
                    belowMapComponent.MapAbove = mapUid;
                    Dirty(belowUid, belowMapComponent);
                }
            }

            Dirty(mapUid, levelMapComponent);

            var ev = new ClassicMapAddedIntoZNetworkEvent(network, depth);
            RaiseLocalEvent(mapUid, ref ev);
        }

        RaiseLocalEvent(network, new ClassicZLevelMapNetworkUpdatedEvent());
        RaiseLocalEvent(new ClassicZLevelMapNetworkReadyEvent(network.Owner));

        return success;
    }

    /// <summary>
    /// Returns the map entity at a specific depth within a z-network, or false if none exists.
    /// </summary>
    [PublicAPI]
    public bool TryGetMapAtDepth(Entity<ClassicZMapNetworkComponent?> network, int depth, out EntityUid mapUid)
    {
        mapUid = EntityUid.Invalid;

        if (!Resolve(network, ref network.Comp, false) ||
            !network.Comp.ZLevels.TryGetValue(depth, out var uid) ||
            uid is not { } validUid)
            return false;

        mapUid = validUid;
        return true;
    }

    /// <summary>
    /// Deletes a map z-network: queues deletion of all maps in the network, then the network entity itself.
    /// </summary>
    [PublicAPI]
    public void DeleteMapNetwork(EntityUid networkUid)
    {
        if (!TryComp<ClassicZMapNetworkComponent>(networkUid, out var zNet))
        {
            Log.Error($"ClassicZLevelsSystem: entity {networkUid} does not have ClassicZLevelsNetworkComponent.");
            return;
        }

        foreach (var (_, mapUid) in zNet.ZLevels)
        {
            if (mapUid != null)
                QueueDel(mapUid.Value);
        }

        QueueDel(networkUid);
    }

    /// <summary>
    /// Removes a map entity from the z-network and updates/deletes the network entity.
    /// </summary>
    [PublicAPI]
    public void RemoveMapFromNetwork(Entity<ClassicZMapNetworkComponent> network, EntityUid mapUid)
    {
        if (!network.Comp.ZLevelByEntity.Remove(mapUid, out var depth))
            return;

        network.Comp.ZLevels.Remove(depth);

        if (network.Comp.ZLevels.TryGetValue(depth + 1, out var aboveMapUid) && aboveMapUid is { } above && TryComp<ClassicZMapComponent>(above, out var aboveComp))
        {
            aboveComp.MapBelow = network.Comp.ZLevels.GetValueOrDefault(depth - 1);
            Dirty(above, aboveComp);
        }

        if (network.Comp.ZLevels.TryGetValue(depth - 1, out var belowMapUid) && belowMapUid is { } below && TryComp<ClassicZMapComponent>(below, out var belowComp))
        {
            belowComp.MapAbove = network.Comp.ZLevels.GetValueOrDefault(depth + 1);
            Dirty(below, belowComp);
        }

        if (TryComp<ClassicZMapComponent>(mapUid, out var removedMap))
        {
            removedMap.NetworkUid = EntityUid.Invalid;
            removedMap.MapAbove = null;
            removedMap.MapBelow = null;
            removedMap.Depth = 0;
            Dirty(mapUid, removedMap);

            var removedEvent = new ClassicMapRemovedFromZNetworkEvent(network.Owner);
            RaiseLocalEvent(mapUid, ref removedEvent);
        }

        // If the network is now empty or invalid, delete it
        if (network.Comp.ZLevelByEntity.Count == 0 || network.Comp.ZLevels.Count == 0)
        {
            QueueDel(network.Owner);
            return;
        }

        // Rebuild SortedZLevels, SortedMin, SortedMax
        network.Comp.SortedZLevels.Clear();
        network.Comp.SortedMin = network.Comp.ZLevels.Keys.Min();
        network.Comp.SortedMax = network.Comp.ZLevels.Keys.Max();
        for (var d = network.Comp.SortedMin; d <= network.Comp.SortedMax; d++)
        {
            network.Comp.SortedZLevels.Add(network.Comp.ZLevels.GetValueOrDefault(d, EntityUid.Invalid) ?? EntityUid.Invalid);
        }

        Dirty(network);
        RaiseLocalEvent(network, new ClassicZLevelMapNetworkUpdatedEvent());
    }

    private void QuickApiCache(Entity<ClassicZMapNetworkComponent> network, EntityUid value, int depth)
    {
        var comp = network.Comp;
        var list = comp.SortedZLevels;

        // First entry: anchor the cache at whatever depth this map actually is,
        // instead of assuming it's 0 (SortedMin/SortedMax's default value).
        if (list.Count == 0)
        {
            list.Add(value);

            comp.SortedMin = depth;
            comp.SortedMax = depth;
            Dirty(network);
            return;
        }

        var min = comp.SortedMin;
        var max = comp.SortedMax;

        if (depth < min)
        {
            var delta = min - depth;
            if (delta == 1)
            {
                list.Insert(0, value);

                comp.SortedMin = depth;
                Dirty(network);
                return;
            }

            list.InsertRange(0, Enumerable.Repeat(EntityUid.Invalid, delta - 1));
            list.Insert(0, value);

            comp.SortedMin = depth;
            Dirty(network);
            return;
        }

        if (depth > max)
        {
            var delta = depth - max;
            if (delta == 1)
            {
                list.Add(value);

                comp.SortedMax = depth;
                Dirty(network);
                return;
            }

            list.AddRange(Enumerable.Repeat(EntityUid.Invalid, delta - 1));
            list.Add(value);

            comp.SortedMax = depth;
            Dirty(network);
            return;
        }

        list[depth - min] = value;
    }
}

/// <summary>
/// Called on ZLevel Network Entity, when maps added or removed from network
/// </summary>
public sealed class ClassicZLevelMapNetworkUpdatedEvent : EntityEventArgs;

/// <summary>
/// Called on a map after it has been detached from a Z-level network.
/// </summary>
[ByRefEvent]
public readonly struct ClassicMapRemovedFromZNetworkEvent(EntityUid formerNetwork)
{
    public EntityUid FormerNetwork { get; } = formerNetwork;
}

/// <summary>
/// Broadcast after a Z-level network has finished updating its maps.
/// </summary>
public sealed class ClassicZLevelMapNetworkReadyEvent(EntityUid network) : EntityEventArgs
{
    public EntityUid Network { get; } = network;
}

/// <summary>
/// Called on map, when it added to ZNetwork
/// </summary>
[ByRefEvent]
public readonly struct ClassicMapAddedIntoZNetworkEvent(Entity<ClassicZMapNetworkComponent> network, int depth)
{
    public readonly Entity<ClassicZMapNetworkComponent> Network = network;
    public readonly int Depth = depth;
}
