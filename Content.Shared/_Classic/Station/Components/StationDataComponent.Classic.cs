// Namespace does not match folder structure
#pragma warning disable IDE0130
namespace Content.Shared.Station.Components;

public sealed partial class StationDataComponent
{
    /// <summary>
    /// Station-owned auxiliary grids used for presence/tracking (for example underground maps),
    /// but excluded from gameplay target selection, FTL destinations and station-area counts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> AuxiliaryGrids = new();
}
