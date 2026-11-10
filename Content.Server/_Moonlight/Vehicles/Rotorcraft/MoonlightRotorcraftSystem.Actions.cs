using Content.Shared._Moonlight.Vehicles;
using Content.Shared._Moonlight.Vehicles.Rotorcraft;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using JetBrains.Annotations;

namespace Content.Server._Moonlight.Vehicles.Rotorcraft;

public sealed partial class MoonlightRotorcraftSystem
{
    [Dependency] private EntityQuery<MoonlightRotorcraftPilotComponent> _pilotQuery = default!;
    [Dependency] private EntityQuery<MoonlightVehicleOperatorComponent> _operatorQuery = default!;

    private void OnOperatorSet(Entity<MoonlightRotorcraftComponent> ent, ref MoonlightVehicleOperatorSetEvent args)
    {
        CancelLanding(ent);
        if (args.OldOperator is { } oldOperator)
            RemoveControls(oldOperator);

        if (args.NewOperator is not { } pilot)
            return;

        var controls = EnsureComp<MoonlightRotorcraftPilotComponent>(pilot);
        _actions.AddAction(pilot, ref controls.ToggleAction, ent.Comp.ToggleActionProto);
        _actions.AddAction(pilot, ref controls.UpAction, ent.Comp.UpActionProto);
        _actions.AddAction(pilot, ref controls.DownAction, ent.Comp.DownActionProto);
        RefreshActions(ent);
        SyncRotorAudio(ent);
    }

    private void OnOperatorShutdown(Entity<MoonlightRotorcraftPilotComponent> ent, ref ComponentShutdown args)
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

    private void OnToggle(Entity<MoonlightRotorcraftPilotComponent> ent, ref MoonlightRotorcraftToggleActionEvent args)
    {
        if (!args.Handled && args.Action.Owner == ent.Comp.ToggleAction)
            args.Handled = TryToggleFlight(ent.AsNullable(), args.Performer);
    }

    private void OnUp(Entity<MoonlightRotorcraftPilotComponent> ent, ref MoonlightRotorcraftUpActionEvent args)
    {
        if (!args.Handled && args.Action.Owner == ent.Comp.UpAction)
            args.Handled = TryChangeTargetLevel(ent.AsNullable(), args.Performer, up: true);
    }

    private void OnDown(Entity<MoonlightRotorcraftPilotComponent> ent, ref MoonlightRotorcraftDownActionEvent args)
    {
        if (!args.Handled && args.Action.Owner == ent.Comp.DownAction)
            args.Handled = TryChangeTargetLevel(ent.AsNullable(), args.Performer, up: false);
    }

    /// <summary>Starts the rotor, requests landing, aborts descent, or stops the rotor after touchdown.</summary>
    [PublicAPI]
    public bool TryToggleFlight(Entity<MoonlightRotorcraftPilotComponent?> pilot, EntityUid performer)
    {
        if (!CanControlFlight(pilot, performer, out var vehicle))
            return false;

        var (_, _, flight, physics) = vehicle;
        switch (flight.Stage)
        {
            case MoonlightRotorcraftStage.Stopped:
                return TryStartRotorTransition((vehicle, flight), performer, starting: true);
            case MoonlightRotorcraftStage.Flying:
                return TryLand((vehicle, flight), physics);
            case MoonlightRotorcraftStage.Descending:
                ResumeHover((vehicle, flight));
                return true;
            case MoonlightRotorcraftStage.Landed:
                return physics.CachedGroundHeight >= 0f &&
                       _zLevels.DistanceToGround((vehicle, physics)) <= flight.LandingContactHeight &&
                       TryStartRotorTransition((vehicle, flight), performer, starting: false);
            default:
                return false;
        }
    }

    /// <summary>Lifts off, aborts landing, lands on this level, or selects an adjacent level over open space.</summary>
    [PublicAPI]
    public bool TryChangeTargetLevel(Entity<MoonlightRotorcraftPilotComponent?> pilot, EntityUid performer, bool up)
    {
        if (!CanControlFlight(pilot, performer, out var vehicle))
            return false;

        var (_, _, flight, physics) = vehicle;
        if (!HasLift((vehicle, flight)))
            return false;
        if (up && flight.Stage is MoonlightRotorcraftStage.Landed or MoonlightRotorcraftStage.Descending)
        {
            ResumeHover((vehicle, flight));
            return true;
        }

        if (flight.Stage != MoonlightRotorcraftStage.Flying)
            return false;

        if (!up && physics.CachedGroundHeight >= 0f)
            return TryLand((vehicle, flight), physics);

        if (Transform(vehicle).MapUid is not { } map)
            return false;

        Entity<MoonlightZMapComponent> target;
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
        Entity<MoonlightRotorcraftPilotComponent?> pilot,
        EntityUid performer,
        out Entity<MoonlightVehicleComponent, MoonlightRotorcraftComponent, MoonlightZPhysicsComponent> vehicle)
    {
        vehicle = default;
        if (pilot.Owner != performer || !Resolve(pilot, ref pilot.Comp, false) ||
            pilot.Comp.LifeStage > ComponentLifeStage.Running ||
            !_operatorQuery.TryComp(pilot, out var vehicleOperator) || vehicleOperator.Vehicle is not { } uid ||
            !_vehicleQuery.TryComp(uid, out var chassis) || chassis.Operator != performer ||
            chassis.MovementKind != MoonlightVehicleMovementKind.Flight || !_vehicles.CanOperate((uid, chassis), performer) ||
            !_flightQuery.TryComp(uid, out var flight) ||
            !_zPhysicsQuery.TryComp(uid, out var physics))
        {
            return false;
        }

        vehicle = (uid, chassis, flight, physics);
        return true;
    }

    private void RefreshActions(Entity<MoonlightRotorcraftComponent> ent)
    {
        if (!_vehicleQuery.TryComp(ent, out var vehicle) ||
            !_pilotQuery.TryComp(vehicle.Operator, out var controls))
            return;

        var stage = ent.Comp.Stage;
        var transitioning = stage is MoonlightRotorcraftStage.Starting or MoonlightRotorcraftStage.Stopping;
        _actions.SetEnabled(controls.ToggleAction, !transitioning);
        _actions.SetToggled(controls.ToggleAction, stage != MoonlightRotorcraftStage.Stopped);
        _actions.SetEnabled(controls.UpAction, !transitioning && stage != MoonlightRotorcraftStage.Stopped);
        _actions.SetEnabled(controls.DownAction, stage == MoonlightRotorcraftStage.Flying);
    }

    private void RemoveControls(EntityUid pilot)
    {
        if (!_pilotQuery.TryComp(pilot, out var controls))
            return;

        RemoveActions((pilot, controls));
        RemCompDeferred<MoonlightRotorcraftPilotComponent>(pilot);
    }

    private void RemoveActions(Entity<MoonlightRotorcraftPilotComponent> ent)
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
