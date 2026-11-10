using Robust.Shared.GameStates;

namespace Content.Shared._Moonlight.Vendors;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedMoonlightAutomatedVendorSystem))]
public sealed partial class MoonlightVendorUserComponent : Component
{
    /// <summary>
    /// Tracks how many times specific choices have been made.
    /// Keys are the choice identifiers, values are the number of times chosen.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> Choices = new();

    /// <summary>
    /// Tracks items that have been taken under a "TakeAll" limit.
    /// Used for categories where a player can take everything in the category once.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<(string Category, string Ent)> TakeAll = new();

    /// <summary>
    /// Tracks item categories where the player can only choose a single item from the entire category.
    /// Once an item is chosen, the category ID is added here.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> TakeOne = new();

    /// <summary>
    /// The primary currency/points the user has available to spend in automated vendors.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Points;

    /// <summary>
    /// Additional point pools the user might have for specific vendor types (e.g., specialist points, medical points).
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int>? ExtraPoints;
}
