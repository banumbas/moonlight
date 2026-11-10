using Content.Shared._Moonlight.Vehicles;
using Content.Shared._Moonlight.Vehicles.Rotorcraft;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Content.Shared.Actions;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Moonlight.Vehicles.Rotorcraft;

/// <summary>Owns pilot actions, rotor transitions and landing. Motion is predicted by the shared controller.</summary>
public sealed partial class MoonlightRotorcraftSystem : MoonlightSharedRotorcraftSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MoonlightRotorcraftComponent, MoonlightVehicleOperatorSetEvent>(OnOperatorSet);
        SubscribeLocalEvent<MoonlightRotorcraftComponent, VehicleFrameIntegrityChangedEvent>(OnFrameIntegrityChanged);
        SubscribeLocalEvent<MoonlightRotorcraftComponent, VehicleInteriorOccupantEnteredEvent>(OnOccupantEntered);
        SubscribeLocalEvent<MoonlightRotorcraftComponent, MoonlightZLevelMapMoveBlockedEvent>(OnMapMoveBlocked);
        SubscribeLocalEvent<MoonlightRotorcraftPilotComponent, ComponentShutdown>(OnOperatorShutdown);
        SubscribeLocalEvent<MoonlightRotorcraftPilotComponent, MoonlightRotorcraftToggleActionEvent>(OnToggle);
        SubscribeLocalEvent<MoonlightRotorcraftPilotComponent, MoonlightRotorcraftUpActionEvent>(OnUp);
        SubscribeLocalEvent<MoonlightRotorcraftPilotComponent, MoonlightRotorcraftDownActionEvent>(OnDown);
        SubscribeLocalEvent<MoonlightRotorcraftSoundComponent, ComponentStartup>(OnSoundStartup);
        SubscribeLocalEvent<MoonlightRotorcraftSoundComponent, ComponentShutdown>(OnSoundShutdown);
    }

    protected override void OnShutdown(Entity<MoonlightRotorcraftComponent> ent, ref ComponentShutdown args)
    {
        base.OnShutdown(ent, ref args);
        if (_soundQuery.TryComp(ent, out var sounds))
            StopRotorAudio(sounds);
        if (_vehicleQuery.TryComp(ent, out var vehicle) && vehicle.Operator is { } pilot)
            RemoveControls(pilot);
    }

    private void OnOccupantEntered(Entity<MoonlightRotorcraftComponent> ent, ref VehicleInteriorOccupantEnteredEvent args)
    {
        SyncRotorAudio(ent);
    }

    private void OnFrameIntegrityChanged(Entity<MoonlightRotorcraftComponent> ent, ref VehicleFrameIntegrityChangedEvent args)
    {
        if (!args.Intact)
            StopFlight(ent);
    }

    private void OnMapMoveBlocked(Entity<MoonlightRotorcraftComponent> ent, ref MoonlightZLevelMapMoveBlockedEvent args)
    {
        if (HasLift(ent))
            ResumeHover(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MoonlightRotorcraftComponent, MoonlightZPhysicsComponent>();
        while (query.MoveNext(out var uid, out var flight, out var physics))
        {
            if (flight.Stage == MoonlightRotorcraftStage.Stopped)
                continue;

            if (!_vehicles.CanRun(uid) || !HasComp<MoonlightZMapComponent>(Transform(uid).MapUid))
            {
                StopFlight((uid, flight));
                continue;
            }

            if (flight.Stage is MoonlightRotorcraftStage.Descending or MoonlightRotorcraftStage.Landed or
                MoonlightRotorcraftStage.Stopping)
            {
                if (physics.CachedGroundHeight < 0f)
                {
                    // A removed floor must not strand the pilot in an unfinishable landing.
                    ResumeHover((uid, flight));
                    continue;
                }

                SetSurfaceTarget((uid, flight), hover: false);
                if (flight.Stage == MoonlightRotorcraftStage.Descending &&
                    _zLevels.DistanceToGround((uid, physics)) <= flight.LandingContactHeight &&
                    MathF.Abs(physics.Velocity) <= physics.SleepThreshold)
                {
                    SetStage((uid, flight), MoonlightRotorcraftStage.Landed);
                }
            }

            UpdateRotorTransition((uid, flight));
        }
    }

    private bool TryLand(Entity<MoonlightRotorcraftComponent> ent, MoonlightZPhysicsComponent physics)
    {
        if (ent.Comp.Stage != MoonlightRotorcraftStage.Flying || physics.CachedGroundHeight < 0f ||
            Transform(ent).MapUid is not { } map || !_zLevels.CanOccupyMap(ent, map))
            return false;

        SetSurfaceTarget(ent, hover: false);
        SetStage(ent, MoonlightRotorcraftStage.Descending);
        return true;
    }

    private void ResumeHover(Entity<MoonlightRotorcraftComponent> ent)
    {
        SetSurfaceTarget(ent, hover: true);
        SetStage(ent, MoonlightRotorcraftStage.Flying);
    }

    /// <summary>Targets clearance over the cached surface, including raised surfaces within a level.</summary>
    private void SetSurfaceTarget(Entity<MoonlightRotorcraftComponent> ent, bool hover)
    {
        if (!_zPhysicsQuery.TryComp(ent, out var physics))
            return;

        var clearance = hover ? ent.Comp.HoverHeight : ent.Comp.LandingHeight;
        var localHeight = MathF.Max(0f, physics.CachedGroundHeight) + clearance;
        localHeight = hover
            ? Math.Clamp(localHeight, ent.Comp.MinLocalHeight, ent.Comp.MaxLocalHeight)
            : Math.Clamp(localHeight, 0f, 1f);
        SetTargetAltitude(ent, physics.CurrentZLevel + localHeight);
    }
}
