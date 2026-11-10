using System.Collections.Generic;
using System.Numerics;
using SharedMover = Content.Shared._Moonlight.Vehicles.GridVehicleMoverSystem;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;

namespace Content.Client._Moonlight.Vehicles;

public sealed class GridVehicleMoverSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    public static readonly List<Vector2> DebugCollisionPositions = new();

    private GridVehicleMoverOverlay? _overlay;
    private VehicleHardpointDebugOverlay? _hardpointOverlay;

    public override void Initialize()
    {
        _overlay = new GridVehicleMoverOverlay(EntityManager);
        _hardpointOverlay = new VehicleHardpointDebugOverlay(EntityManager);

        _overlayManager.AddOverlay(_hardpointOverlay);
    }

    public bool ToggleDebugOverlay()
    {
        if (_overlay == null)
            return false;

        _overlay.DebugEnabled = !_overlay.DebugEnabled;
        RefreshSharedDebugFlags();
        RefreshVehicleDebugOverlay();
        return _overlay.DebugEnabled;
    }

    public bool ToggleHardpointOverlay()
    {
        if (_hardpointOverlay == null)
            return false;

        _hardpointOverlay.Enabled = !_hardpointOverlay.Enabled;
        return _hardpointOverlay.Enabled;
    }

    public bool ToggleCollisionOverlay()
    {
        if (_overlay == null)
            return false;

        _overlay.CollisionsEnabled = !_overlay.CollisionsEnabled;
        RefreshSharedDebugFlags();
        RefreshVehicleDebugOverlay();
        return _overlay.CollisionsEnabled;
    }

    public bool ToggleMovementOverlay()
    {
        if (_overlay == null)
            return false;

        _overlay.MovementEnabled = !_overlay.MovementEnabled;
        RefreshSharedDebugFlags();
        RefreshVehicleDebugOverlay();
        return _overlay.MovementEnabled;
    }

    public override void Shutdown()
    {
        if (_overlay != null)
            _overlayManager.RemoveOverlay(_overlay);
        if (_hardpointOverlay != null)
            _overlayManager.RemoveOverlay(_hardpointOverlay);
    }

    private void RefreshSharedDebugFlags()
    {
        SharedMover.CollisionDebugEnabled =
            _overlay is { DebugEnabled: true } or { CollisionsEnabled: true };
        SharedMover.MovementDebugEnabled =
            _overlay is { MovementEnabled: true };
    }

    private void RefreshVehicleDebugOverlay()
    {
        if (_overlay == null)
            return;

        var enabled = _overlay.DebugEnabled || _overlay.CollisionsEnabled || _overlay.MovementEnabled;
        var hasOverlay = _overlayManager.HasOverlay<GridVehicleMoverOverlay>();
        if (enabled && !hasOverlay)
        {
            _overlayManager.AddOverlay(_overlay);
        }
        else if (!enabled && hasOverlay)
        {
            _overlayManager.RemoveOverlay(_overlay);
        }
    }
}
