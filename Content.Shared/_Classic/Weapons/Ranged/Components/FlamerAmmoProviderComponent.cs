using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Weapons.Ranged.Components;

/// <summary>
/// Gun ammo provider that consumes liquid fuel from either an internal tank slot or an equipped backpack tank.
/// Spawns chemical flame projectiles when fired.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FlamerAmmoProviderComponent : AmmoProviderComponent
{
    /// <summary>
    /// ID of the gun's internal cartridge slot (e.g. "flamer_tank").
    /// </summary>
    [DataField, AutoNetworkedField]
    public string SlotId = "flamer_tank";

    /// <summary>
    /// Name of the solution container inside the fuel tank entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string SolutionId = "tank";

    /// <summary>
    /// Fuel volume consumed per shot.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 FuelCostPerShot = FixedPoint2.New(1);

    /// <summary>
    /// Flame projectile prototype spawned when firing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId ProjectilePrototype = "FlameProjectile";

    /// <summary>
    /// If true, uses the internal tank first before falling back to the backpack tank.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PreferInternalTank = true;

    /// <summary>
    /// Minimum time between empty click popup warnings to prevent spam.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan EmptyPopupCooldown = TimeSpan.FromSeconds(0.8);

    /// <summary>
    /// Timestamp of last empty popup shown.
    /// </summary>
    public TimeSpan LastEmptyPopup = TimeSpan.Zero;
}
