using Content.Shared.Access;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Moonlight.Vendors;

/// <summary>
/// Attached to automated vendor machines to define their sections and entries (items available for purchase).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedMoonlightAutomatedVendorSystem))]
public sealed partial class MoonlightAutomatedVendorComponent : Component
{
    /// <summary>
    /// List of sections (categories) available in this vendor.
    /// Each section contains entries (items).
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<MoonlightVendorSection> Sections = new();

    /// <summary>
    /// The specific type of points this vendor uses.
    /// If null, it uses the standard Points pool from the user.
    /// If set (e.g. "Medical"), it pulls from the user's ExtraPoints under that key.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? PointsType;

    /// <summary>
    /// An optional animation state to play when the vendor successfully vends an item.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi? AnimationSprite;

    /// <summary>
    /// If set, the user must pass this whitelist to open the vendor UI.
    /// This allows restricting vendors to specific roles or tags.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityWhitelist? UserWhitelist;

    /// <summary>
    /// The sound played when an item is purchased and vended.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? Sound;
}
