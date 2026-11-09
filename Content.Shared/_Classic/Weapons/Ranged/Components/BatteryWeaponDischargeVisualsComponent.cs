using Content.Shared._Classic.Weapons.Ranged.Systems;
using Robust.Shared.Audio;

namespace Content.Shared._Classic.Weapons.Ranged.Components;

/// <summary>
/// Enables binary ready/discharged visuals and configures the ready sound for a self-recharging battery weapon.
/// </summary>
[RegisterComponent]
[Access(typeof(BatteryWeaponDischargeVisualsSystem))]
public sealed partial class BatteryWeaponDischargeVisualsComponent : Component
{
    /// <summary>
    /// Sound played once when the recharge cooldown completes.
    /// </summary>
    [DataField]
    public SoundSpecifier? SoundReload;
}
