/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Moonlight.ZLevels.Pulling;

public sealed partial class MoonlightZLevelPullingSystem : EntitySystem
{
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;

    private EntityQuery<MoonlightZLevelPullingTransitionComponent> _transitionQuery;
    private EntityQuery<MoonlightZPhysicsComponent> _zPhysicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _transitionQuery = GetEntityQuery<MoonlightZLevelPullingTransitionComponent>();
        _zPhysicsQuery = GetEntityQuery<MoonlightZPhysicsComponent>();

        SubscribeLocalEvent<ActivePullerComponent, MoonlightZLevelBeforeMapMoveEvent>(OnPullerMove);
        SubscribeLocalEvent<MoonlightZLevelPullingTransitionComponent, MoonlightZLevelMapMoveEvent>(OnPulledEntityMove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<MoonlightZLevelPullingTransitionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NextTransition is null)
                continue;

            if (comp.TargetPuller is null)
                continue;

            if (comp.NextTransition < _timing.CurTime)
            {
                FinishTransition(uid, comp);
                continue;
            }

            // Move entity towards target position (saved position of puller at transition start)
            var currentPos = _transform.GetWorldPosition(uid);
            var direction = (comp.TargetPosition - currentPos).Normalized();
            var distance = (comp.TargetPosition - currentPos).Length();
            var moveDistance = comp.TransitionSpeed * frameTime;

            if (moveDistance >= distance)
            {
                _transform.SetWorldPosition(uid, comp.TargetPosition);
            }
            else
            {
                _transform.SetWorldPosition(uid, currentPos + direction * moveDistance);
            }
        }
    }

    private void OnPullerMove(Entity<ActivePullerComponent> ent, ref MoonlightZLevelBeforeMapMoveEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryComp<PullerComponent>(ent, out var puller) || puller.Pulling == null)
            return;

        var pulledEntity = puller.Pulling.Value;

        // Get the current position of the puller before transition
        var pullerPos = _transform.GetWorldPosition(ent);

        // Add transition component to the pulled entity
        var transComp = EnsureComp<MoonlightZLevelPullingTransitionComponent>(pulledEntity);
        transComp.TargetPuller = ent;
        transComp.StartPosition = _transform.GetWorldPosition(pulledEntity);
        transComp.TargetPosition = pullerPos;  // Save puller's position at the moment of transition
        transComp.TargetZLevel = args.CurrentZLevel + args.Offset;

        var distance = Vector2.Distance(transComp.StartPosition, transComp.TargetPosition);
        var duration = TimeSpan.FromSeconds(distance / transComp.TransitionSpeed);
        transComp.NextTransition = _timing.CurTime + duration;

        Dirty(pulledEntity, transComp);
    }

    private void OnPulledEntityMove(Entity<MoonlightZLevelPullingTransitionComponent> ent, ref MoonlightZLevelMapMoveEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        // Check if the pulled entity reached the target z-level
        if (args.CurrentZLevel != ent.Comp.TargetZLevel)
            return;

        TryResumePulling(ent, ent.Comp);
    }

    private void FinishTransition(EntityUid uid, MoonlightZLevelPullingTransitionComponent comp)
    {
        comp.NextTransition = null;
        Dirty(uid, comp);

        // If still transitioning and entity didn't move to target level, stop transition
        if (!Exists(uid) || !_transitionQuery.HasComp(uid))
            return;

        TryResumePulling(uid, comp);
    }

    /// <summary>
    /// Attempts to resume pulling after the transition is complete (either by time or z-level change).
    /// Removes the transition component whether pulling succeeds or not.
    /// </summary>
    private void TryResumePulling(EntityUid uid, MoonlightZLevelPullingTransitionComponent comp)
    {
        // Check if puller still exists
        if (!Exists(comp.TargetPuller))
        {
            RemComp<MoonlightZLevelPullingTransitionComponent>(uid);
            return;
        }

        // Check if both entities are on the same map
        if (Transform(uid).MapUid != Transform(comp.TargetPuller.Value).MapUid)
        {
            RemComp<MoonlightZLevelPullingTransitionComponent>(uid);
            return;
        }

        if (!_actionBlocker.CanInteract(comp.TargetPuller.Value, uid))
        {
            RemComp<MoonlightZLevelPullingTransitionComponent>(uid);
            return;
        }

        // Try to resume pulling from the puller
        _pulling.TryStartPull(comp.TargetPuller.Value, uid);
        RemComp<MoonlightZLevelPullingTransitionComponent>(uid);
    }
}
