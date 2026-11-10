using Content.Shared._Moonlight.Vehicles.Rotorcraft;
using Content.Shared._Moonlight.ZLevels.Core.Components;

namespace Content.Server._Moonlight.Vehicles.Rotorcraft;

public sealed partial class MoonlightRotorcraftSystem
{
    /// <summary>Completes a rotor transition from its replicated, pause-adjusted deadline.</summary>
    private void UpdateRotorTransition(Entity<MoonlightRotorcraftComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.RotorTransitionEnd)
            return;

        switch (ent.Comp.Stage)
        {
            case MoonlightRotorcraftStage.Starting:
                ent.Comp.TargetHeading = _transform.GetWorldRotation(ent);
                DirtyField(ent, ent.Comp, nameof(MoonlightRotorcraftComponent.TargetHeading));
                _zLevels.RefreshGroundAfterTeleport(ent.Owner);
                SetSurfaceTarget(ent, hover: true);
                SetStage(ent, MoonlightRotorcraftStage.Flying);
                break;
            case MoonlightRotorcraftStage.Stopping:
                StopFlight(ent);
                break;
        }
    }

    private void StopFlight(Entity<MoonlightRotorcraftComponent> ent)
    {
        SetStage(ent, MoonlightRotorcraftStage.Stopped);
    }

    private bool CanStartRotorTransition(Entity<MoonlightRotorcraftComponent> ent, bool starting)
    {
        return starting
            ? ent.Comp.Stage == MoonlightRotorcraftStage.Stopped && _vehicles.CanRun(ent.Owner) &&
              HasComp<MoonlightZMapComponent>(Transform(ent).MapUid)
            : ent.Comp.Stage == MoonlightRotorcraftStage.Landed;
    }

    private bool TryStartRotorTransition(Entity<MoonlightRotorcraftComponent> ent, EntityUid pilot, bool starting)
    {
        if (!CanStartRotorTransition(ent, starting) || !_vehicles.CanOperate(ent.Owner, pilot))
            return false;

        var duration = starting ? ent.Comp.RotorStartupTime : ent.Comp.RotorShutdownTime;
        ent.Comp.RotorTransitionStart = _timing.CurTime;
        ent.Comp.RotorTransitionEnd = _timing.CurTime + (duration > TimeSpan.Zero ? duration : TimeSpan.Zero);
        DirtyField(ent, ent.Comp, nameof(MoonlightRotorcraftComponent.RotorTransitionStart));
        DirtyField(ent, ent.Comp, nameof(MoonlightRotorcraftComponent.RotorTransitionEnd));
        SetStage(ent, starting ? MoonlightRotorcraftStage.Starting : MoonlightRotorcraftStage.Stopping);
        UpdateRotorTransition(ent);
        return true;
    }

    private void CancelLanding(Entity<MoonlightRotorcraftComponent> ent)
    {
        if (ent.Comp.Stage == MoonlightRotorcraftStage.Descending)
            ResumeHover(ent);
    }

    private void SetStage(Entity<MoonlightRotorcraftComponent> ent, MoonlightRotorcraftStage stage)
    {
        var restartAudio = ent.Comp.Stage != stage &&
            (ent.Comp.Stage is MoonlightRotorcraftStage.Starting or MoonlightRotorcraftStage.Stopping ||
             stage is MoonlightRotorcraftStage.Starting or MoonlightRotorcraftStage.Stopping);
        SetFlightStage(ent, stage);
        SyncRotorAudio(ent, restart: restartAudio);
        RefreshActions(ent);
    }
}
