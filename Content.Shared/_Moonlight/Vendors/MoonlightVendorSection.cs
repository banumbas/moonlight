using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Moonlight.Vendors;

/// <summary>
/// Represents a category or section in an automated vendor.
/// Each section can group related items (like "Weapons" or "Ammo").
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class MoonlightVendorSection
{
    /// <summary>
    /// The display name of the section shown in the UI.
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public (string Id, int Amount)? Choices;

    [DataField]
    public string? TakeAll;

    [DataField]
    public string? TakeOne;

    /// <summary>
    /// The list of items available within this section.
    /// </summary>
    [DataField(required: true)]
    public List<MoonlightVendorEntry> Entries = new();
}

/// <summary>
/// Represents a single purchasable item within a vendor section.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class MoonlightVendorEntry
{
    /// <summary>
    /// The prototype ID of the entity that will be spawned when purchased.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Id;

    /// <summary>
    /// Optional prototype ID of an entity to use for the icon, name, and description in the vendor UI.
    /// Useful when vending boxes or containers (e.g. weapon cases) so the vendor shows the actual weapon inside.
    /// </summary>
    [DataField]
    public EntProtoId? DisplayEntity;

    /// <summary>
    /// Optional sprite specifier to display as icon in the vendor UI.
    /// </summary>
    [DataField]
    public SpriteSpecifier? Icon;

    /// <summary>
    /// Overrides the name of the item shown in the UI.
    /// If null, the prototype's default name is used.
    /// </summary>
    [DataField]
    public string? Name;

    /// <summary>
    /// Overrides the description of the item shown in the UI tooltip.
    /// If null, the prototype's default description is used.
    /// </summary>
    [DataField]
    public string? Description;

    /// <summary>
    /// Amount of items given (e.g. ammo in a box).
    /// Used for display purposes in the UI to indicate quantity.
    /// </summary>
    [DataField]
    public int? Amount;

    /// <summary>
    /// Global stock of this item in the vendor.
    /// If null, it is infinite.
    /// </summary>
    [DataField]
    public int? Stock;

    /// <summary>
    /// The point cost for this entry. If null, the item might be free or rely on other limits.
    /// </summary>
    [DataField]
    public int? Points;

    /// <summary>
    /// Number of items to spawn when this entry is purchased.
    /// Defaults to 1.
    /// </summary>
    [DataField]
    public int Spawn = 1;

    [DataField]
    public bool Recommended;

    /// <summary>
    /// If true, the item will be automatically equipped to the recipient's inventory slot
    /// instead of being handed directly. Useful for clothing and armor.
    /// </summary>
    [DataField]
    public bool AutoEquip;

    /// <summary>
    /// Additional items to spawn and auto-equip alongside the main item (Id).
    /// Only used when <see cref="AutoEquip"/> is true.
    /// Allows a single vendor entry to equip a full set of clothing/armor.
    /// </summary>
    [DataField]
    public List<EntProtoId> AutoEquipContents = new();
}
