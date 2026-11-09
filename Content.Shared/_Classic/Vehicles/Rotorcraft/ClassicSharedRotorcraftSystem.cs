using Content.Shared._Classic.ZLevels.Core.Components;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Content.Shared.Gravity;
using Content.Shared.Movement.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._Classic.Vehicles.Rotorcraft;

/// <summary>Predicts vehicle motion; rotor sequences and pilot actions are owned by the server.</summary>
public abstract partial class ClassicSharedRotorcraftSystem : EntitySystem
{
    [Dependency] protected ClassicSharedZLevelsSystem _zLevels = default!;
    [Dependency] protected ClassicVehicleBaseSystem _vehicles = default!;
    [Dependency] protected IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] protected ISharedPlayerManager _player = default!;
    [Dependency] protected SharedTransformSystem _transform = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    [Dependency] protected EntityQuery<ClassicVehicleComponent> _vehicleQuery = default!;
    [Dependency] protected EntityQuery<ClassicRotorcraftComponent> _flightQuery = default!;
    [Dependency] protected EntityQuery<ClassicZPhysicsComponent> _zPhysicsQuery = default!;

    private bool _relativeMovement;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configuration, CCVars.RelativeMovement, value => _relativeMovement = value, true);
        UpdatesAfter.Add(typeof(SharedMoverController));
        SubscribeLocalEvent<ClassicRotorcraftComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ClassicRotorcraftComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ClassicRotorcraftComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
        SubscribeLocalEvent<ClassicRotorcraftComponent, ClassicGetZVelocityEvent>(OnGetZVelocity);
        SubscribeLocalEvent<ClassicRotorcraftComponent, ClassicCheckGravityEvent>(OnGetGravity);
        SubscribeLocalEvent<ClassicRotorcraftComponent, IsWeightlessEvent>(OnIsWeightless);
        SubscribeLocalEvent<ClassicRotorcraftComponent, TileFrictionEvent>(OnTileFriction);
        SubscribeLocalEvent<ClassicRotorcraftComponent, ClassicZLevelMapMoveAttemptEvent>(OnMapMoveAttempt);
        SubscribeLocalEvent<ClassicRotorcraftComponent, ClassicZLevelChasmAttempt>(OnChasmAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ClassicRotorcraftComponent, ClassicZPhysicsComponent>();
        while (query.MoveNext(out var uid, out var vehicle, out var physics))
            ApplyHorizontalFlightControls((uid, vehicle, physics), frameTime);
    }

    private void OnStartup(Entity<ClassicRotorcraftComponent> ent, ref ComponentStartup args)
    {
        RefreshLiftState(ent);
    }

    protected virtual void OnShutdown(Entity<ClassicRotorcraftComponent> ent, ref ComponentShutdown args)
    {
        // The component still exists during shutdown, so event handlers must reject its lifestage.
        RefreshLiftState(ent);
    }

    private void OnAfterHandleState(Entity<ClassicRotorcraftComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshLiftState(ent);
    }

    private void OnGetGravity(Entity<ClassicRotorcraftComponent> ent, ref ClassicCheckGravityEvent args)
    {
        if (HasLift(ent))
            args.Gravity = 0f;
    }

    private void OnIsWeightless(Entity<ClassicRotorcraftComponent> ent, ref IsWeightlessEvent args)
    {
        if (args.Handled || !HasLift(ent) ||
            ent.Comp.Stage is not (ClassicRotorcraftStage.Flying or ClassicRotorcraftStage.Descending))
            return;

        args.IsWeightless = true;
        args.Handled = true;
    }

    private void OnTileFriction(Entity<ClassicRotorcraftComponent> ent, ref TileFrictionEvent args)
    {
        if (ent.Comp.AirControlsActive &&
            ent.Comp.Stage is ClassicRotorcraftStage.Flying or ClassicRotorcraftStage.Descending &&
            HasLift(ent))
        {
            args.Cancelled = true;
        }
    }

    private void OnMapMoveAttempt(Entity<ClassicRotorcraftComponent> ent, ref ClassicZLevelMapMoveAttemptEvent args)
    {
        if (_zLevels.CanOccupyMap(ent, args.Destination, includeFloors: args.Offset > 0))
            return;

        args.Cancelled = true;
        args.Blocked = true;
    }

    private void OnChasmAttempt(Entity<ClassicRotorcraftComponent> ent, ref ClassicZLevelChasmAttempt args)
    {
        if (HasLift(ent))
            args.Cancel();
    }

    /// <summary>Whether the running rotorcraft controller currently holds its altitude.</summary>
    protected static bool HasLift(Entity<ClassicRotorcraftComponent> ent)
    {
        return ent.Comp.LifeStage is >= ComponentLifeStage.Starting and <= ComponentLifeStage.Running &&
               ent.Comp.Stage is ClassicRotorcraftStage.Flying or ClassicRotorcraftStage.Descending or
                   ClassicRotorcraftStage.Landed or ClassicRotorcraftStage.Stopping;
    }

    /// <summary>Changes the authoritative phase and reconciles its derived physics state.</summary>
    protected void SetFlightStage(Entity<ClassicRotorcraftComponent> ent, ClassicRotorcraftStage stage)
    {
        if (ent.Comp.Stage != stage)
        {
            ent.Comp.Stage = stage;
            DirtyField(ent, ent.Comp, nameof(ClassicRotorcraftComponent.Stage));
        }

        RefreshLiftState(ent);
    }

    /// <summary>Selects an absolute altitude and wakes vertical physics when it changes.</summary>
    protected void SetTargetAltitude(Entity<ClassicRotorcraftComponent> ent, float altitude)
    {
        if (ent.Comp.TargetAltitude == altitude)
            return;

        ent.Comp.TargetAltitude = altitude;
        DirtyField(ent, ent.Comp, nameof(ClassicRotorcraftComponent.TargetAltitude));
        _zLevels.WakeBody(ent.Owner);
    }

    private void RefreshLiftState(Entity<ClassicRotorcraftComponent> ent)
    {
        if (_zPhysicsQuery.TryComp(ent, out var physics) && physics.LifeStage < ComponentLifeStage.Stopping)
        {
            var lift = HasLift(ent);
            if (physics.VelocityRaiseEvent != lift)
            {
                physics.VelocityRaiseEvent = lift;
                DirtyField(ent, physics, nameof(ClassicZPhysicsComponent.VelocityRaiseEvent));
            }

            _zLevels.UpdateGravityState((ent, physics));
            _zLevels.WakeBody((ent, physics));
        }

        _gravity.RefreshWeightless(ent.Owner);
    }
}
