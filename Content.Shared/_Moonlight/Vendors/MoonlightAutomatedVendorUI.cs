using Robust.Shared.Serialization;

namespace Content.Shared._Moonlight.Vendors;

/// <summary>
/// UI Key for the automated vendor interface.
/// </summary>
[Serializable, NetSerializable]
public enum MoonlightAutomatedVendorUI : byte
{
    Key
}

/// <summary>
/// Sent by the client to the server when the user clicks the "Vend" button on an entry.
/// </summary>
[Serializable, NetSerializable]
public sealed class MoonlightVendorVendBuiMsg : BoundUserInterfaceMessage
{
    /// <summary>
    /// The index of the section in the vendor's section list.
    /// </summary>
    public readonly int Section;

    /// <summary>
    /// The index of the entry within the chosen section.
    /// </summary>
    public readonly int Entry;

    public MoonlightVendorVendBuiMsg(int section, int entry)
    {
        Section = section;
        Entry = entry;
    }
}

/// <summary>
/// Sent by the client to the server to request an update to the UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class MoonlightVendorRefreshBuiMsg : BoundUserInterfaceMessage;

/// <summary>
/// Contains the user's current points and limitations to update the UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class MoonlightAutomatedVendorBuiState : BoundUserInterfaceState
{
    /// <summary>
    /// User's available primary points.
    /// </summary>
    public readonly int Points;

    /// <summary>
    /// User's available extra point pools.
    /// </summary>
    public readonly Dictionary<string, int>? ExtraPoints;

    /// <summary>
    /// Tracks items chosen via the Choice system.
    /// </summary>
    public readonly Dictionary<string, int> Choices;

    /// <summary>
    /// Tracks categories exhausted via the TakeAll system.
    /// </summary>
    public readonly HashSet<(string Category, string Ent)> TakeAll;

    /// <summary>
    /// Tracks categories exhausted via the TakeOne system.
    /// </summary>
    public readonly HashSet<string> TakeOne;

    public MoonlightAutomatedVendorBuiState(
        int points,
        Dictionary<string, int>? extraPoints,
        Dictionary<string, int> choices,
        HashSet<(string Category, string Ent)> takeAll,
        HashSet<string> takeOne)
    {
        Points = points;
        ExtraPoints = extraPoints;
        Choices = choices;
        TakeAll = takeAll;
        TakeOne = takeOne;
    }
}
