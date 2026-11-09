using Content.Shared._Classic.Vehicles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._Classic.Vehicles;

public sealed class VehicleWeaponSupportSystem : EntitySystem
{
    [Dependency] private readonly VehicleTopologySystem _topology = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnGunRefresh);
    }

    private void OnGunRefresh(Entity<GunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!_topology.TryGetVehicle(ent.Owner, out var vehicle))
            return;

        if (!TryComp(vehicle, out VehicleWeaponSupportModifierComponent? mods))
            return;

        args.FireRate *= mods.FireRateMultiplier;
    }
}
