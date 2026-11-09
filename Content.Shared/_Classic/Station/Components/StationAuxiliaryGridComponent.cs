using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Station.Components;

/// <summary>
/// Marks a station-owned grid that should not be selected as the station's primary/largest grid
/// or counted as playable station area. Ownership and station tracking still work normally.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StationAuxiliaryGridComponent : Component;
