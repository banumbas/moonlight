using Content.Shared._Moonlight.Vehicles;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent]
[Access(typeof(MoonlightVehicleSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleInteriorComponent : Component
{
    public EntityUid Map = EntityUid.Invalid;
    public MapId MapId = MapId.Nullspace;
    public EntityCoordinates Entry;
    public EntityUid EntryParent = EntityUid.Invalid;
    public EntityUid Grid = EntityUid.Invalid;
    public HashSet<int> EntryLocks = new();
    public HashSet<EntityUid> Passengers = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(MoonlightVehicleSystem))]
public sealed partial class VehicleInteriorLinkComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Vehicle = EntityUid.Invalid;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(MoonlightVehicleSystem))]
public sealed partial class VehicleInteriorOccupantComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Vehicle = EntityUid.Invalid;
}

/// <summary>
/// Raised on the exterior vehicle after an occupant has been moved onto its interior map.
/// Systems that need to mirror exterior state into the cabin should subscribe to this instead of
/// subscribing to the occupant's map-change event directly.
/// </summary>
[ByRefEvent]
public readonly record struct VehicleInteriorOccupantEnteredEvent(EntityUid Occupant);
