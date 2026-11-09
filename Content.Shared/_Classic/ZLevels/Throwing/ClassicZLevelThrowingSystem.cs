using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Throwing;

namespace Content.Shared._Classic.ZLevels.Throwing;

/// <summary>
/// Suspends z-physics for throws from the ground so it does not interfere with their
/// friction-based timing. Impulses to airborne bodies preserve their vertical motion.
/// </summary>
public sealed partial class ClassicZLevelThrowingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClassicZPhysicsComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<ClassicZPhysicsComponent, StopThrowEvent>(OnStopThrow);
    }

    private void OnThrown(Entity<ClassicZPhysicsComponent> ent, ref ThrownEvent args)
    {
        if (ent.Comp.LocalPosition - ent.Comp.CachedGroundHeight > ClassicSharedZLevelsSystem.AirborneHeightThreshold)
            return;

        ent.Comp.Disabled = true;
        DirtyField(ent, ent.Comp, nameof(ClassicZPhysicsComponent.Disabled));
    }

    private void OnStopThrow(Entity<ClassicZPhysicsComponent> ent, ref StopThrowEvent args)
    {
        ent.Comp.Disabled = false;
        DirtyField(ent, ent.Comp, nameof(ClassicZPhysicsComponent.Disabled));
    }
}
