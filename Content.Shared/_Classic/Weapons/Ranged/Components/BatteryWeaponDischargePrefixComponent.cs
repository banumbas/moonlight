using Content.Shared._Classic.Weapons.Ranged.Systems;

namespace Content.Shared._Classic.Weapons.Ranged.Components;

/// <summary>
/// Configures the held and equipped sprite prefixes used while a self-recharging battery weapon has no shots.
/// </summary>
[RegisterComponent]
[Access(typeof(BatteryWeaponDischargePrefixSystem))]
public sealed partial class BatteryWeaponDischargePrefixComponent : Component
{
    /// <summary>
    /// Prefix used while the weapon has no available shots.
    /// A wielded weapon uses this prefix followed by its wielded prefix.
    /// </summary>
    [DataField(required: true)]
    public string EmptyHeldPrefix = default!;

    /// <summary>
    /// Prefix used by equipped clothing states while the weapon has no available shots.
    /// If unset, equipped clothing sprites are left unchanged.
    /// </summary>
    [DataField]
    public string? EmptyEquippedPrefix;

    /// <summary>
    /// Prefix restored for an unwielded weapon once it is ready to fire.
    /// </summary>
    [DataField]
    public string? ReadyHeldPrefix;

    /// <summary>
    /// Prefix restored for equipped clothing sprites once at least one shot is available.
    /// </summary>
    [DataField]
    public string? ReadyEquippedPrefix;
}
