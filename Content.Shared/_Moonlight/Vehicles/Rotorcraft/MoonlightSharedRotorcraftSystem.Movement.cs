using System.Numerics;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared.Movement.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Moonlight.Vehicles.Rotorcraft;

public abstract partial class MoonlightSharedRotorcraftSystem
{
    [Dependency] private EntityQuery<PhysicsComponent> _bodyQuery = default!;
    [Dependency] private EntityQuery<InputMoverComponent> _inputQuery = default!;

    /// <summary>Tilts the rotor disc towards pilot input and accelerates the existing velocity, independently of heading.</summary>
    private void ApplyHorizontalFlightControls(
        Entity<MoonlightRotorcraftComponent, MoonlightZPhysicsComponent> ent,
        float frameTime)
    {
        var (uid, flight, zPhysics) = ent;
        if (frameTime <= 0f || !_vehicleQuery.TryComp(uid, out var vehicle) ||
            vehicle.MovementKind != MoonlightVehicleMovementKind.Flight || !_bodyQuery.TryComp(uid, out var body))
            return;

        if (_net.IsClient && (!_timing.InPrediction || vehicle.Operator != _player.LocalEntity || vehicle.Operator == null))
            return;

        var canCruise = HasLift((uid, flight)) &&
                        (flight.Stage is MoonlightRotorcraftStage.Flying or MoonlightRotorcraftStage.Descending) &&
                        _vehicles.CanRun((uid, vehicle));
        if (_net.IsClient)
        {
            // The ground cache is local-only; the pilot may be viewing the aircraft from its cabin map.
            canCruise &= flight.AirControlsActive;
        }
        else
        {
            var clearance = flight.AirControlsActive ? flight.LandingContactHeight : flight.TakeoffClearance;
            canCruise &= _zLevels.DistanceToGround((uid, zPhysics)) > clearance;
            if (flight.AirControlsActive != canCruise)
            {
                flight.AirControlsActive = canCruise;
                DirtyField(uid, flight, nameof(MoonlightRotorcraftComponent.AirControlsActive));
            }
        }

        if (!canCruise)
        {
            SetCyclic((uid, flight), Vector2.Zero);
            return;
        }

        var input = vehicle.Operator is { } pilot && _vehicles.CanOperate((uid, vehicle), pilot) &&
                    _inputQuery.TryComp(pilot, out var mover)
            ? GetFlightInput(mover)
            : Vector2.Zero;
        var previousCyclic = flight.Cyclic;
        var response = flight.CyclicResponse > 0f ? MathF.Exp(-frameTime / flight.CyclicResponse) : 0f;
        SetCyclic((uid, flight), Vector2.Lerp(input, previousCyclic, response));

        // Integrate thrust and drag on the current map velocity. Turning the nose never redirects momentum.
        var (velocity, yaw) = _physics.GetMapVelocities(uid, body);
        var parentVelocity = velocity - body.LinearVelocity;
        var drag = flight.MaxSpeed > 0f ? flight.Acceleration / flight.MaxSpeed : 0f;
        if (flight.Stage == MoonlightRotorcraftStage.Descending)
            drag += flight.LandingDrag;
        var retention = MathF.Exp(-MathF.Max(0f, drag) * frameTime);
        var thrustTime = drag > 0f ? (1f - retention) / drag : frameTime;
        var thrust = (previousCyclic + flight.Cyclic) * (0.5f * flight.Acceleration);
        velocity = velocity * retention + thrust * thrustTime;
        _physics.SetLinearVelocity(uid, velocity - parentVelocity, body: body);

        if (input != Vector2.Zero && flight.Cyclic.LengthSquared() > 0.01f)
        {
            var heading = flight.Cyclic.ToWorldAngle();
            if (flight.TargetHeading != heading)
            {
                flight.TargetHeading = heading;
                DirtyField(uid, flight, nameof(MoonlightRotorcraftComponent.TargetHeading));
            }
        }

        var error = (float) Angle.ShortestDistance(_transform.GetWorldRotation(uid), flight.TargetHeading).Theta;
        var targetYaw = Math.Clamp(error * flight.TurnResponse, -flight.TurnSpeed, flight.TurnSpeed);
        var parentYaw = yaw - body.AngularVelocity;
        var maxChange = flight.TurnAcceleration * frameTime;
        yaw += Math.Clamp(targetYaw - yaw, -maxChange, maxChange);
        _physics.SetAngularVelocity(uid, yaw - parentYaw, body: body);
    }

    private void SetCyclic(Entity<MoonlightRotorcraftComponent> ent, Vector2 cyclic)
    {
        if (ent.Comp.Cyclic == cyclic)
            return;

        ent.Comp.Cyclic = cyclic;
        DirtyField(ent, ent.Comp, nameof(MoonlightRotorcraftComponent.Cyclic));
    }

    private Vector2 GetFlightInput(InputMoverComponent mover)
    {
        // Walking and sprinting use the same thrust, but retain their fractions of this tick.
        var (walking, sprinting) = _mover.GetVelocityInput(mover);
        var input = walking + sprinting;
        return _relativeMovement ? _mover.GetParentGridAngle(mover).RotateVec(input) : input;
    }
}
