namespace Content.Shared._Classic.Weapons.Ranged.Events;

/// <summary>
/// Raised on a battery weapon entity to allow custom visuals systems
/// to intercept and handle appearance updates instead of the default behavior.
/// </summary>
[ByRefEvent]
public record struct BatteryWeaponUpdateVisualsEvent(int Shots)
{
    public bool Handled;
}

/// <summary>
/// Raised after a battery weapon's discharge state has been calculated.
/// This gives every visual representation of the weapon the same source of truth.
/// </summary>
[ByRefEvent]
public record struct BatteryWeaponDischargeStateChangedEvent(bool Discharged);
