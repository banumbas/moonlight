using System;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Shared._Classic.Vehicles;

public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    private Vector2i GetInputDirection(InputMoverComponent input)
    {
        var buttons = input.HeldMoveButtons;
        var dir = Vector2i.Zero;

        if ((buttons & MoveButtons.Up) != 0) dir += new Vector2i(0, 1);
        if ((buttons & MoveButtons.Down) != 0) dir += new Vector2i(0, -1);
        if ((buttons & MoveButtons.Right) != 0) dir += new Vector2i(1, 0);
        if ((buttons & MoveButtons.Left) != 0) dir += new Vector2i(-1, 0);

        if (dir == Vector2i.Zero)
            return dir;

        if (dir.X != 0 && dir.Y != 0)
        {
            if (Math.Abs(dir.X) >= Math.Abs(dir.Y))
                dir = new Vector2i(Math.Sign(dir.X), 0);
            else
                dir = new Vector2i(0, Math.Sign(dir.Y));
        }

        return dir;
    }

    private Vector2i GetMoverInput(EntityUid uid, GridVehicleMoverComponent mover, ClassicVehicleComponent vehicle, out bool pushing)
    {
        pushing = false;
        if (vehicle.Operator is { } op && TryComp<InputMoverComponent>(op, out var inputComp))
        {
            return GetInputDirection(inputComp);
        }

        if (vehicle.Operator != null)
        {
            return Vector2i.Zero;
        }

        if (TryComp(uid, out VehicleAutopilotComponent? autopilot))
        {
            return autopilot.Direction;
        }

        return Vector2i.Zero;
    }
}
