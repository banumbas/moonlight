using Robust.Shared.GameStates;

using Content.Shared._Classic.Weapons.Ranged.Systems;

namespace Content.Shared._Classic.Weapons.Ranged.Components;

/// <summary>
/// Marker component preserving an item's ready prefixes while it is visually discharged.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(BatteryWeaponDischargePrefixSystem))]
public sealed partial class ActiveGunDischargePrefixComponent : Component
{
    /// <summary>
    /// The unwielded prefix to restore once the weapon is ready to fire.
    /// </summary>
    [AutoNetworkedField]
    public string? ReadyHeldPrefix;

    /// <summary>
    /// The equipped clothing prefix to restore once the weapon has shots again.
    /// </summary>
    [AutoNetworkedField]
    public string? ReadyEquippedPrefix;
}
