using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;

namespace Content.Shared._Moonlight.Vehicles.Rotorcraft;

public abstract partial class MoonlightSharedRotorcraftSystem
{
    private void OnGetZVelocity(Entity<MoonlightRotorcraftComponent> ent, ref MoonlightGetZVelocityEvent args)
    {
        if (args.Handled || !HasLift(ent) || args.FrameTime <= 0f)
            return;

        var physics = args.Target.Comp;
        ClampAltitudeTarget((ent, ent.Comp, physics));
        var target = GetHoverTarget((ent, ent.Comp, physics));
        var error = target - (physics.CurrentZLevel + physics.LocalPosition);
        var acceleration = Math.Clamp(error * ent.Comp.AltitudeStiffness - physics.Velocity * ent.Comp.AltitudeDamping,
            -ent.Comp.VerticalAcceleration, ent.Comp.VerticalAcceleration);
        var nextVelocity = Math.Clamp(physics.Velocity + acceleration * args.FrameTime,
            -ent.Comp.VerticalSpeed, ent.Comp.VerticalSpeed);
        args.VelocityDelta = (nextVelocity - physics.Velocity) / args.FrameTime;
        args.Handled = true;
    }

    /// <summary>Returns to a safe hover if the requested neighbouring map has been removed.</summary>
    private void ClampAltitudeTarget(Entity<MoonlightRotorcraftComponent, MoonlightZPhysicsComponent> ent)
    {
        var (uid, flight, physics) = ent;
        if (Transform(uid).MapUid is not { } map)
            return;

        // A landing surface at local height 1 still belongs to the current map.
        var above = flight.TargetAltitude > physics.CurrentZLevel + 1f && !_zLevels.TryMapUp(map, out _);
        var below = flight.TargetAltitude < physics.CurrentZLevel && !_zLevels.TryMapDown(map, out _);
        if (!above && !below)
            return;

        SetTargetAltitude((uid, flight), physics.CurrentZLevel +
            (above ? flight.MaxLocalHeight : flight.MinLocalHeight));
        if (above && physics.Velocity > 0f || below && physics.Velocity < 0f)
            _zLevels.SetZVelocity((uid, physics), 0f);
    }

    /// <summary>Fades hover bob near the destination and keeps it within the configured flight band.</summary>
    private float GetHoverTarget(Entity<MoonlightRotorcraftComponent, MoonlightZPhysicsComponent> ent)
    {
        var (_, flight, physics) = ent;
        if (flight.Stage != MoonlightRotorcraftStage.Flying)
            return flight.TargetAltitude;

        var targetLevel = MathF.Floor(flight.TargetAltitude);
        var localHeight = Math.Clamp(flight.TargetAltitude - targetLevel, flight.MinLocalHeight, flight.MaxLocalHeight);
        var target = targetLevel + localHeight;

        var amplitude = MathF.Max(0f, MathF.Min(flight.HoverBobAmplitude,
            MathF.Min(localHeight - flight.MinLocalHeight, flight.MaxLocalHeight - localHeight)));
        var distance = MathF.Abs(target - (physics.CurrentZLevel + physics.LocalPosition));
        var blend = flight.HoverHeight > 0f ? Math.Clamp(1f - distance / flight.HoverHeight, 0f, 1f) : 0f;
        blend = blend * blend * (3f - 2f * blend);
        var phase = (float) _timing.CurTime.TotalSeconds * flight.HoverBobFrequency * MathF.Tau;
        return target + amplitude * blend * MathF.Sin(phase);
    }
}
