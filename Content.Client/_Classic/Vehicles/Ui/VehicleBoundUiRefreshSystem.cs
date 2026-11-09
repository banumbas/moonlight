using Content.Client._Classic.Vehicles.Supply;
using Content.Shared._Classic.Vehicles;
using Content.Shared._Classic.Vehicles.Supply;
using Robust.Client.GameObjects;

namespace Content.Client._Classic.Vehicles.Ui;

public sealed class VehicleBoundUiRefreshSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<HardpointSlotsComponent, AfterAutoHandleStateEvent>(OnHardpointState);
        SubscribeLocalEvent<VehicleAmmoLoaderComponent, AfterAutoHandleStateEvent>(OnAmmoLoaderState);
        SubscribeLocalEvent<VehicleWeaponsSeatComponent, AfterAutoHandleStateEvent>(OnWeaponsSeatState);
        SubscribeLocalEvent<VehicleSupplyConsoleComponent, AfterAutoHandleStateEvent>(OnSupplyConsoleState);
    }

    public void RefreshUIs<T>(EntityUid uid) where T : BoundUserInterface, IRefreshableBui
    {
        if (TerminatingOrDeleted(uid) || !TryComp(uid, out UserInterfaceComponent? uiComp))
            return;

        foreach (var bui in uiComp.ClientOpenInterfaces.Values)
        {
            if (bui is T ui)
                ui.Refresh();
        }
    }

    private void OnHardpointState(Entity<HardpointSlotsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshUIs<HardpointBoundUserInterface>(ent.Owner);
    }

    private void OnAmmoLoaderState(Entity<VehicleAmmoLoaderComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshUIs<VehicleAmmoLoaderBoundUserInterface>(ent.Owner);
    }

    private void OnWeaponsSeatState(Entity<VehicleWeaponsSeatComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshUIs<VehicleWeaponsBoundUserInterface>(ent.Owner);
    }

    private void OnSupplyConsoleState(Entity<VehicleSupplyConsoleComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshUIs<VehicleSupplyBui>(ent.Owner);
    }
}
