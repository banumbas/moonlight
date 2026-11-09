using Content.Shared._Classic.Vehicles;
using Content.Shared._Classic.Vehicles.Rotorcraft;
using Content.Shared._Classic.ZLevels.Core.Components;
using JetBrains.Annotations;

namespace Content.Server._Classic.Vehicles.Rotorcraft;

public sealed partial class ClassicRotorcraftSystem
{
    [Dependency] private EntityQuery<ClassicRotorcraftPilotComponent> _pilotQuery = default!;
    [Dependency] private EntityQuery<ClassicVehicleOperatorComponent> _operatorQuery = default!;

    private void OnOperatorSet(Entity<ClassicRotorcraftComponent> ent, ref ClassicVehicleOperatorSetEvent args)
    {
        CancelLanding(ent);
        if (args.OldOperator is { } oldOperator)
            RemoveControls(oldOperator);

        if (args.NewOperator is not { } pilot)
            return;

        var controls = EnsureComp<ClassicRotorcraftPilotComponent>(pilot);
        _actions.AddAction(pilot, ref controls.ToggleAction, ent.Comp.ToggleActionProto);
        _actions.AddAction(pilot, ref controls.UpAction, ent.Comp.UpActionProto);
        _actions.AddAction(pilot, ref controls.DownAction, ent.Comp.DownActionProto);
        RefreshActions(ent);
        SyncRotorAudio(ent);
    }

    private void OnOperatorShutdown(Entity<ClassicRotorcraftPilotComponent> ent, ref ComponentShutdown args)
    {
        // A normal transfer clears the old actions before removing this component.
        if ((ent.Comp.ToggleAction != null || ent.Comp.UpAction != null || ent.Comp.DownAction != null) &&
            _operatorQuery.TryComp(ent, out var vehicleOperator) && vehicleOperator.Vehicle is { } vehicle &&
            _vehicleQuery.TryComp(vehicle, out var chassis) && chassis.Operator == ent.Owner &&
            _flightQuery.TryComp(vehicle, out var flight))
        {
            CancelLanding((vehicle, flight));
        }
        RemoveActions(ent);
    }

    private void OnToggle(Entity<ClassicRotorcraftPilotComponent> ent, ref ClassicRotorcraftToggleActionEvent args)
    {
        if (!args.Handled && args.Action.Owner == ent.Comp.ToggleAction)
            args.Handled = TryToggleFlight(ent.AsNullable(), args.Performer);
    }

    private void OnUp(Entity<ClassicRotorcraftPilotComponent> ent, ref ClassicRotorcraftUpActionEvent args)
    {
        if (!args.Handled && args.Action.Owner == ent.Comp.UpAction)
            args.Handled = TryChangeTargetLevel(ent.AsNullable(), args.Performer, up: true);
    }

    private void OnDown(Entity<ClassicRotorcraftPilotComponent> ent, ref ClassicRotorcraftDownActionEvent args)
    {
        if (!args.Handled && args.Action.Owner == ent.Comp.DownAction)
            args.Handled = TryChangeTargetLevel(ent.AsNullable(), args.Performer, up: false);
    }

    /// <summary>Starts the rotor, requests landing, aborts descent, or stops the rotor after touchdown.</summary>
    [PublicAPI]
    public bool TryToggleFlight(Entity<ClassicRotorcraftPilotComponent?> pilot, EntityUid performer)
    {
        if (!CanControlFlight(pilot, performer, out var vehicle))
            return false;

        var (_, _, flight, physics) = vehicle;
        switch (flight.Stage)
        {
            case ClassicRotorcraftStage.Stopped:
                return TryStartRotorTransition((vehicle, flight), performer, starting: true);
            case ClassicRotorcraftStage.Flying:
                return TryLand((vehicle, flight), physics);
            case ClassicRotorcraftStage.Descending:
                ResumeHover((vehicle, flight));
                return true;
            case ClassicRotorcraftStage.Landed:
                return physics.CachedGroundHeight >= 0f &&
                       _zLevels.DistanceToGround((vehicle, physics)) <= flight.LandingContactHeight &&
                       TryStartRotorTransition((vehicle, flight), performer, starting: false);
            default:
                return false;
        }
    }

    /// <summary>Lifts off, aborts landing, lands on this level, or selects an adjacent level over open space.</summary>
    [PublicAPI]
    public bool TryChangeTargetLevel(Entity<ClassicRotorcraftPilotComponent?> pilot, EntityUid performer, bool up)
    {
        if (!CanControlFlight(pilot, performer, out var vehicle))
            return false;

        var (_, _, flight, physics) = vehicle;
        if (!HasLift((vehicle, flight)))
            return false;
        if (up && flight.Stage is ClassicRotorcraftStage.Landed or ClassicRotorcraftStage.Descending)
        {
            ResumeHover((vehicle, flight));
            return true;
        }

        if (flight.Stage != ClassicRotorcraftStage.Flying)
            return false;

        if (!up && physics.CachedGroundHeight >= 0f)
            return TryLand((vehicle, flight), physics);

        if (Transform(vehicle).MapUid is not { } map)
            return false;

        Entity<ClassicZMapComponent> target;
        if (up ? !_zLevels.TryMapUp(map, out target) : !_zLevels.TryMapDown(map, out target))
            return false;

        if (!_zLevels.CanOccupyMap(vehicle, target, includeFloors: up))
            return false;

        SetTargetAltitude((vehicle, flight), target.Comp.Depth +
            Math.Clamp(flight.HoverHeight, flight.MinLocalHeight, flight.MaxLocalHeight));
        return true;
    }

    /// <summary>Checks both sides of the pilot/vehicle relationship before accepting an action.</summary>
    private bool CanControlFlight(
        Entity<ClassicRotorcraftPilotComponent?> pilot,
        EntityUid performer,
        out Entity<ClassicVehicleComponent, ClassicRotorcraftComponent, ClassicZPhysicsComponent> vehicle)
    {
        vehicle = default;
        if (pilot.Owner != performer || !Resolve(pilot, ref pilot.Comp, false) ||
            pilot.Comp.LifeStage > ComponentLifeStage.Running ||
            !_operatorQuery.TryComp(pilot, out var vehicleOperator) || vehicleOperator.Vehicle is not { } uid ||
            !_vehicleQuery.TryComp(uid, out var chassis) || chassis.Operator != performer ||
            chassis.MovementKind != ClassicVehicleMovementKind.Flight || !_vehicles.CanOperate((uid, chassis), performer) ||
            !_flightQuery.TryComp(uid, out var flight) ||
            !_zPhysicsQuery.TryComp(uid, out var physics))
        {
            return false;
        }

        vehicle = (uid, chassis, flight, physics);
        return true;
    }

    private void RefreshActions(Entity<ClassicRotorcraftComponent> ent)
    {
        if (!_vehicleQuery.TryComp(ent, out var vehicle) ||
            !_pilotQuery.TryComp(vehicle.Operator, out var controls))
            return;

        var stage = ent.Comp.Stage;
        var transitioning = stage is ClassicRotorcraftStage.Starting or ClassicRotorcraftStage.Stopping;
        _actions.SetEnabled(controls.ToggleAction, !transitioning);
        _actions.SetToggled(controls.ToggleAction, stage != ClassicRotorcraftStage.Stopped);
        _actions.SetEnabled(controls.UpAction, !transitioning && stage != ClassicRotorcraftStage.Stopped);
        _actions.SetEnabled(controls.DownAction, stage == ClassicRotorcraftStage.Flying);
    }

    private void RemoveControls(EntityUid pilot)
    {
        if (!_pilotQuery.TryComp(pilot, out var controls))
            return;

        RemoveActions((pilot, controls));
        RemCompDeferred<ClassicRotorcraftPilotComponent>(pilot);
    }

    private void RemoveActions(Entity<ClassicRotorcraftPilotComponent> ent)
    {
        RemoveAction(ent, ent.Comp.ToggleAction);
        RemoveAction(ent, ent.Comp.UpAction);
        RemoveAction(ent, ent.Comp.DownAction);
        ent.Comp.ToggleAction = ent.Comp.UpAction = ent.Comp.DownAction = null;
    }

    private void RemoveAction(EntityUid pilot, EntityUid? action)
    {
        if (action is not { } uid || TerminatingOrDeleted(uid))
            return;

        _actions.RemoveAction(pilot, uid);
        if (Exists(uid))
            QueueDel(uid);
    }
}
