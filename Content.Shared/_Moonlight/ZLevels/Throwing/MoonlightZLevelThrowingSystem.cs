using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Content.Shared.Throwing;

namespace Content.Shared._Moonlight.ZLevels.Throwing;

/// <summary>
/// Suspends z-physics for throws from the ground so it does not interfere with their
/// friction-based timing. Impulses to airborne bodies preserve their vertical motion.
/// </summary>
public sealed partial class MoonlightZLevelThrowingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MoonlightZPhysicsComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<MoonlightZPhysicsComponent, StopThrowEvent>(OnStopThrow);
    }

    private void OnThrown(Entity<MoonlightZPhysicsComponent> ent, ref ThrownEvent args)
    {
        if (ent.Comp.LocalPosition - ent.Comp.CachedGroundHeight > MoonlightSharedZLevelsSystem.AirborneHeightThreshold)
            return;

        ent.Comp.Disabled = true;
        DirtyField(ent, ent.Comp, nameof(MoonlightZPhysicsComponent.Disabled));
    }

    private void OnStopThrow(Entity<MoonlightZPhysicsComponent> ent, ref StopThrowEvent args)
    {
        ent.Comp.Disabled = false;
        DirtyField(ent, ent.Comp, nameof(MoonlightZPhysicsComponent.Disabled));
    }
}
