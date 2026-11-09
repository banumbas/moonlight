using System;
using Content.Shared._Classic.Vehicles;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Classic.Vehicles.Ui;

public sealed class HardpointBoundUserInterface : BoundUserInterface, IRefreshableBui
{
    private HardpointMenu? _menu;

    public HardpointBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new HardpointMenu();
        _menu.OnClose += Close;
        _menu.VehicleEntity = Owner;

        if (EntMan.TryGetComponent(Owner, out MetaDataComponent? metadata))
            _menu.Title = metadata.EntityName;

        _menu.OnRemove += slotId => SendMessage(new HardpointRemoveMessage(slotId));
        _menu.OpenCentered();
        Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        if (_menu != null)
            _menu.OnClose -= Close;

        _menu?.Dispose();
        _menu = null;
    }

    public void Refresh()
    {
        if (_menu is not { IsOpen: true })
            return;

        if (!EntMan.TryGetComponent(Owner, out HardpointSlotsComponent? hardpoints))
            return;

        var hardpointState = hardpoints.Ui;
        _menu?.Update(
            hardpointState.Hardpoints,
            hardpointState.FrameIntegrity,
            hardpointState.FrameMaxIntegrity,
            hardpointState.HasFrameIntegrity,
            hardpointState.Error);
    }
}
