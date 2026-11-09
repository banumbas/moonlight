using Content.Shared._Classic.Vehicles.Rotorcraft;
using Content.Shared._Classic.ZLevels.Core.Components;

namespace Content.Server._Classic.Vehicles.Rotorcraft;

public sealed partial class ClassicRotorcraftSystem
{
    /// <summary>Completes a rotor transition from its replicated, pause-adjusted deadline.</summary>
    private void UpdateRotorTransition(Entity<ClassicRotorcraftComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.RotorTransitionEnd)
            return;

        switch (ent.Comp.Stage)
        {
            case ClassicRotorcraftStage.Starting:
                ent.Comp.TargetHeading = _transform.GetWorldRotation(ent);
                DirtyField(ent, ent.Comp, nameof(ClassicRotorcraftComponent.TargetHeading));
                _zLevels.RefreshGroundAfterTeleport(ent.Owner);
                SetSurfaceTarget(ent, hover: true);
                SetStage(ent, ClassicRotorcraftStage.Flying);
                break;
            case ClassicRotorcraftStage.Stopping:
                StopFlight(ent);
                break;
        }
    }

    private void StopFlight(Entity<ClassicRotorcraftComponent> ent)
    {
        SetStage(ent, ClassicRotorcraftStage.Stopped);
    }

    private bool CanStartRotorTransition(Entity<ClassicRotorcraftComponent> ent, bool starting)
    {
        return starting
            ? ent.Comp.Stage == ClassicRotorcraftStage.Stopped && _vehicles.CanRun(ent.Owner) &&
              HasComp<ClassicZMapComponent>(Transform(ent).MapUid)
            : ent.Comp.Stage == ClassicRotorcraftStage.Landed;
    }

    private bool TryStartRotorTransition(Entity<ClassicRotorcraftComponent> ent, EntityUid pilot, bool starting)
    {
        if (!CanStartRotorTransition(ent, starting) || !_vehicles.CanOperate(ent.Owner, pilot))
            return false;

        var duration = starting ? ent.Comp.RotorStartupTime : ent.Comp.RotorShutdownTime;
        ent.Comp.RotorTransitionStart = _timing.CurTime;
        ent.Comp.RotorTransitionEnd = _timing.CurTime + (duration > TimeSpan.Zero ? duration : TimeSpan.Zero);
        DirtyField(ent, ent.Comp, nameof(ClassicRotorcraftComponent.RotorTransitionStart));
        DirtyField(ent, ent.Comp, nameof(ClassicRotorcraftComponent.RotorTransitionEnd));
        SetStage(ent, starting ? ClassicRotorcraftStage.Starting : ClassicRotorcraftStage.Stopping);
        UpdateRotorTransition(ent);
        return true;
    }

    private void CancelLanding(Entity<ClassicRotorcraftComponent> ent)
    {
        if (ent.Comp.Stage == ClassicRotorcraftStage.Descending)
            ResumeHover(ent);
    }

    private void SetStage(Entity<ClassicRotorcraftComponent> ent, ClassicRotorcraftStage stage)
    {
        var restartAudio = ent.Comp.Stage != stage &&
            (ent.Comp.Stage is ClassicRotorcraftStage.Starting or ClassicRotorcraftStage.Stopping ||
             stage is ClassicRotorcraftStage.Starting or ClassicRotorcraftStage.Stopping);
        SetFlightStage(ent, stage);
        SyncRotorAudio(ent, restart: restartAudio);
        RefreshActions(ent);
    }
}
