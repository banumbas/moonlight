using Robust.Shared.GameStates;

using Content.Shared._Classic.Weapons.Ranged.Systems;

namespace Content.Shared._Classic.Weapons.Ranged.Components;

/// <summary>
/// Marker component tracking a self-recharging gun's firing-rate and recharge cooldowns.
/// Used internally for efficient iteration in Update.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
[Access(typeof(BatteryWeaponDischargeVisualsSystem))]
public sealed partial class ActiveGunDischargeVisualsComponent : Component
{
    /// <summary>
    /// Time at which the self-recharge cooldown ends and the ready sound is played.
    /// </summary>
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan? RechargeEnd;

    /// <summary>
    /// Time at which the normal firing-rate delay ends.
    /// </summary>
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan? FireDelayEnd;
}
