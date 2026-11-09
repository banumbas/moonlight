/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Classic.ZLevels.Flight.Components;
using Content.Shared.DoAfter;
using Content.Shared.Toggleable;

namespace Content.Shared._Classic.ZLevels.Flight;

public abstract partial class ClassicSharedZFlightSystem
{
    private void InitializeControllable()
    {
        SubscribeLocalEvent<ClassicControllableFlightComponent, ClassicZFlightActionUp>(OnZLevelUp);
        SubscribeLocalEvent<ClassicControllableFlightComponent, ClassicZFlightActionDown>(OnZLevelDown);
        SubscribeLocalEvent<ClassicControllableFlightComponent, ToggleActionEvent>(OnZLevelToggle);

        SubscribeLocalEvent<ClassicControllableFlightComponent, ClassicStartFlightDoAfterEvent>(OnStartFlightDoAfter);
        SubscribeLocalEvent<ClassicControllableFlightComponent, ClassicFlightStartedEvent>(OnControllableFlightStarted);
        SubscribeLocalEvent<ClassicControllableFlightComponent, ClassicFlightStoppedEvent>(OnControllableFlightStopped);
    }

    private void OnControllableFlightStopped(Entity<ClassicControllableFlightComponent> ent, ref ClassicFlightStoppedEvent args)
    {
        _actions.SetEnabled(ent.Comp.ZLevelDownActionEntity, false);
        _actions.SetEnabled(ent.Comp.ZLevelUpActionEntity, false);

        // Update toggle action icon state
        if (ent.Comp.ZLevelToggleActionEntity != null)
            _actions.SetToggled(ent.Comp.ZLevelToggleActionEntity, false);
    }

    private void OnControllableFlightStarted(Entity<ClassicControllableFlightComponent> ent, ref ClassicFlightStartedEvent args)
    {
        _actions.SetEnabled(ent.Comp.ZLevelDownActionEntity, true);
        _actions.SetEnabled(ent.Comp.ZLevelUpActionEntity, true);

        // Update toggle action icon state
        if (ent.Comp.ZLevelToggleActionEntity != null)
            _actions.SetToggled(ent.Comp.ZLevelToggleActionEntity, true);
    }

    private void OnZLevelUp(Entity<ClassicControllableFlightComponent> ent, ref ClassicZFlightActionUp args)
    {
        if (args.Handled)
            return;

        var map = Transform(ent).MapUid;
        if (map is null)
            return;

        if (!TryComp<ClassicZFlyerComponent>(ent, out var flyerComp))
            return;

        if (!_zLevel.TryMapUp(map.Value, out var mapAbove))
            return;

        SetTargetHeight((ent, flyerComp), mapAbove.Comp.Depth);

        args.Handled = true;
    }

    private void OnZLevelDown(Entity<ClassicControllableFlightComponent> ent, ref ClassicZFlightActionDown args)
    {
        if (args.Handled)
            return;

        var map = Transform(ent).MapUid;
        if (map is null)
            return;

        if (!TryComp<ClassicZFlyerComponent>(ent, out var flyerComp))
            return;

        if (!_zLevel.TryMapDown(map.Value, out var mapBelow))
            return;

        SetTargetHeight((ent, flyerComp), mapBelow.Comp.Depth);

        args.Handled = true;
    }

    private void OnZLevelToggle(Entity<ClassicControllableFlightComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        if (args.Action.Owner != ent.Comp.ZLevelToggleActionEntity)
            return;

        if (!TryComp<ClassicZFlyerComponent>(ent, out var flyerComp))
            return;

        if (flyerComp.Active)
        {
            DeactivateFlight((ent, flyerComp));
        }
        else
        {
            // If StartFlightDoAfter is set, start a doAfter before activating flight
            if (ent.Comp.StartFlightDoAfter != null)
            {
                //Preventive start flying visuals
                StartFlightVisuals((ent, flyerComp));

                var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.StartFlightDoAfter.Value, new ClassicStartFlightDoAfterEvent(), ent)
                {
                    BreakOnMove = false,
                    BlockDuplicate = true,
                    BreakOnDamage = true,
                    CancelDuplicate = true,
                };

                _doAfter.TryStartDoAfter(doAfter);
            }
            else
            {
                // No delay, activate flight immediately
                TryActivateFlight((ent, flyerComp));
            }
        }

        args.Handled = true;
    }

    private void OnStartFlightDoAfter(Entity<ClassicControllableFlightComponent> ent, ref ClassicStartFlightDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
        {
            StopFlightVisuals(ent.Owner);
            return;
        }

        if (!TryActivateFlight(ent.Owner))
            StopFlightVisuals(ent.Owner);
        args.Handled = true;
    }
}
