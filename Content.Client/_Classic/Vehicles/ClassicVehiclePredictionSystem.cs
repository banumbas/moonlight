using Content.Shared._Classic.Vehicles;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using ClientPhysicsSystem = Robust.Client.Physics.PhysicsSystem;

namespace Content.Client._Classic.Vehicles;

/// <summary>Predicts custom vehicle controllers for the local operator and refreshes ownership on transfer.</summary>
public sealed partial class ClassicVehiclePredictionSystem : EntitySystem
{
    [Dependency] private ClientPhysicsSystem _physics = default!;
    [Dependency] private IPlayerManager _player = default!;

    private EntityUid? _lastVehicle;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClassicVehicleComponent, UpdateIsPredictedEvent>(OnUpdateIsPredicted);
    }

    private void OnUpdateIsPredicted(Entity<ClassicVehicleComponent> ent, ref UpdateIsPredictedEvent args)
    {
        if (ent.Comp.MovementKind is ClassicVehicleMovementKind.Grid or ClassicVehicleMovementKind.Flight &&
            ent.Comp.Operator is { } pilot && pilot == _player.LocalEntity)
        {
            args.IsPredicted = true;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        EntityUid? vehicle = null;
        if (TryComp<ClassicVehicleOperatorComponent>(_player.LocalEntity, out var controls) &&
            TryComp<ClassicVehicleComponent>(controls.Vehicle, out var chassis) &&
            chassis.MovementKind is ClassicVehicleMovementKind.Grid or ClassicVehicleMovementKind.Flight)
        {
            vehicle = controls.Vehicle;
        }

        if (_lastVehicle == vehicle)
            return;

        _physics.UpdateIsPredicted(_lastVehicle);
        _lastVehicle = vehicle;
        _physics.UpdateIsPredicted(vehicle);
    }
}
