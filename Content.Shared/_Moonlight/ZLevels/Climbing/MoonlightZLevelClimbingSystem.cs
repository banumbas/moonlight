using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared.Climbing.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Moonlight.ZLevels.Climbing;

/// <summary>
/// Allows airborne entities to pass over climbable obstacles (fences, tables) without triggering a climb.
/// </summary>
public sealed class MoonlightZLevelClimbingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MoonlightZPhysicsComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<MoonlightZPhysicsComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.Disabled)
            return;

        if (!TryComp<PhysicsComponent>(ent, out var physics) || physics.BodyStatus != BodyStatus.InAir)
            return;

        if (HasComp<ClimbableComponent>(args.OtherEntity))
            args.Cancelled = true;
    }
}
