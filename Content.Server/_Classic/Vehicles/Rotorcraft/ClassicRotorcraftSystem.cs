using Content.Shared._Classic.Vehicles;
using Content.Shared._Classic.Vehicles.Rotorcraft;
using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Actions;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Classic.Vehicles.Rotorcraft;

/// <summary>Owns pilot actions, rotor transitions and landing. Motion is predicted by the shared controller.</summary>
public sealed partial class ClassicRotorcraftSystem : ClassicSharedRotorcraftSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClassicRotorcraftComponent, ClassicVehicleOperatorSetEvent>(OnOperatorSet);
        SubscribeLocalEvent<ClassicRotorcraftComponent, VehicleFrameIntegrityChangedEvent>(OnFrameIntegrityChanged);
        SubscribeLocalEvent<ClassicRotorcraftComponent, VehicleInteriorOccupantEnteredEvent>(OnOccupantEntered);
        SubscribeLocalEvent<ClassicRotorcraftComponent, ClassicZLevelMapMoveBlockedEvent>(OnMapMoveBlocked);
        SubscribeLocalEvent<ClassicRotorcraftPilotComponent, ComponentShutdown>(OnOperatorShutdown);
        SubscribeLocalEvent<ClassicRotorcraftPilotComponent, ClassicRotorcraftToggleActionEvent>(OnToggle);
        SubscribeLocalEvent<ClassicRotorcraftPilotComponent, ClassicRotorcraftUpActionEvent>(OnUp);
        SubscribeLocalEvent<ClassicRotorcraftPilotComponent, ClassicRotorcraftDownActionEvent>(OnDown);
        SubscribeLocalEvent<ClassicRotorcraftSoundComponent, ComponentStartup>(OnSoundStartup);
        SubscribeLocalEvent<ClassicRotorcraftSoundComponent, ComponentShutdown>(OnSoundShutdown);
    }

    protected override void OnShutdown(Entity<ClassicRotorcraftComponent> ent, ref ComponentShutdown args)
    {
        base.OnShutdown(ent, ref args);
        if (_soundQuery.TryComp(ent, out var sounds))
            StopRotorAudio(sounds);
        if (_vehicleQuery.TryComp(ent, out var vehicle) && vehicle.Operator is { } pilot)
            RemoveControls(pilot);
    }

    private void OnOccupantEntered(Entity<ClassicRotorcraftComponent> ent, ref VehicleInteriorOccupantEnteredEvent args)
    {
        SyncRotorAudio(ent);
    }

    private void OnFrameIntegrityChanged(Entity<ClassicRotorcraftComponent> ent, ref VehicleFrameIntegrityChangedEvent args)
    {
        if (!args.Intact)
            StopFlight(ent);
    }

    private void OnMapMoveBlocked(Entity<ClassicRotorcraftComponent> ent, ref ClassicZLevelMapMoveBlockedEvent args)
    {
        if (HasLift(ent))
            ResumeHover(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ClassicRotorcraftComponent, ClassicZPhysicsComponent>();
        while (query.MoveNext(out var uid, out var flight, out var physics))
        {
            if (flight.Stage == ClassicRotorcraftStage.Stopped)
                continue;

            if (!_vehicles.CanRun(uid) || !HasComp<ClassicZMapComponent>(Transform(uid).MapUid))
            {
                StopFlight((uid, flight));
                continue;
            }

            if (flight.Stage is ClassicRotorcraftStage.Descending or ClassicRotorcraftStage.Landed or
                ClassicRotorcraftStage.Stopping)
            {
                if (physics.CachedGroundHeight < 0f)
                {
                    // A removed floor must not strand the pilot in an unfinishable landing.
                    ResumeHover((uid, flight));
                    continue;
                }

                SetSurfaceTarget((uid, flight), hover: false);
                if (flight.Stage == ClassicRotorcraftStage.Descending &&
                    _zLevels.DistanceToGround((uid, physics)) <= flight.LandingContactHeight &&
                    MathF.Abs(physics.Velocity) <= physics.SleepThreshold)
                {
                    SetStage((uid, flight), ClassicRotorcraftStage.Landed);
                }
            }

            UpdateRotorTransition((uid, flight));
        }
    }

    private bool TryLand(Entity<ClassicRotorcraftComponent> ent, ClassicZPhysicsComponent physics)
    {
        if (ent.Comp.Stage != ClassicRotorcraftStage.Flying || physics.CachedGroundHeight < 0f ||
            Transform(ent).MapUid is not { } map || !_zLevels.CanOccupyMap(ent, map))
            return false;

        SetSurfaceTarget(ent, hover: false);
        SetStage(ent, ClassicRotorcraftStage.Descending);
        return true;
    }

    private void ResumeHover(Entity<ClassicRotorcraftComponent> ent)
    {
        SetSurfaceTarget(ent, hover: true);
        SetStage(ent, ClassicRotorcraftStage.Flying);
    }

    /// <summary>Targets clearance over the cached surface, including raised surfaces within a level.</summary>
    private void SetSurfaceTarget(Entity<ClassicRotorcraftComponent> ent, bool hover)
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
