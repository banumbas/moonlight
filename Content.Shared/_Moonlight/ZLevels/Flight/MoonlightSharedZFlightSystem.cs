/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Content.Shared._Moonlight.ZLevels.Flight.Components;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Stunnable;
using JetBrains.Annotations;
using Robust.Shared.Serialization;

namespace Content.Shared._Moonlight.ZLevels.Flight;

public abstract partial class MoonlightSharedZFlightSystem : EntitySystem
{
    [Dependency] private MoonlightSharedZLevelsSystem _zLevel = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;

    [Dependency] protected EntityQuery<MoonlightZPhysicsComponent> ZPhyzQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeControllable();

        SubscribeLocalEvent<MoonlightZPhysicsComponent, MoonlightFlightStartedEvent>(OnStartFlight);
        SubscribeLocalEvent<MoonlightZPhysicsComponent, MoonlightFlightStoppedEvent>(OnStopFlight);
        SubscribeLocalEvent<MoonlightZFlyerComponent, MoonlightGetZVelocityEvent>(OnGetZVelocity);
        SubscribeLocalEvent<MoonlightZFlyerComponent, MoonlightCheckGravityEvent>(OnGetGravity);
        SubscribeLocalEvent<MoonlightZFlyerComponent, IsWeightlessEvent>(CheckWeightless);

        SubscribeLocalEvent<MoonlightZFlyerComponent, StunnedEvent>(OnStunned);
        SubscribeLocalEvent<MoonlightZFlyerComponent, KnockedDownEvent>(OnKnockDowned);
        SubscribeLocalEvent<MoonlightZFlyerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MoonlightZFlyerComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void CheckWeightless(Entity<MoonlightZFlyerComponent> ent, ref IsWeightlessEvent args)
    {
        if (!ent.Comp.Active || args.Handled)
            return;

        args.IsWeightless = true;
        args.Handled = true;
    }

    private void OnDamageChanged(Entity<MoonlightZFlyerComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (!args.InterruptsDoAfters)
            return;

        DeactivateFlight((ent, ent));
    }

    private void OnMobStateChanged(Entity<MoonlightZFlyerComponent> ent, ref MobStateChangedEvent args)
    {
        DeactivateFlight((ent, ent));
    }

    private void OnKnockDowned(Entity<MoonlightZFlyerComponent> ent, ref KnockedDownEvent args)
    {
        DeactivateFlight((ent, ent));
    }

    private void OnStunned(Entity<MoonlightZFlyerComponent> ent, ref StunnedEvent args)
    {
        DeactivateFlight((ent, ent));
    }

    private void OnStartFlight(Entity<MoonlightZPhysicsComponent> ent, ref MoonlightFlightStartedEvent args)
    {
        SetTargetHeight(ent.Owner, ent.Comp.CurrentZLevel);
        StartFlightVisuals(ent.Owner);
    }

    private void OnStopFlight(Entity<MoonlightZPhysicsComponent> ent, ref MoonlightFlightStoppedEvent args)
    {
        StopFlightVisuals(ent.Owner);
    }

    private void OnGetZVelocity(Entity<MoonlightZFlyerComponent> ent, ref MoonlightGetZVelocityEvent args)
    {
        if (!ent.Comp.Active || args.Handled)
            return;

        var zPhys = args.Target.Comp;
        var currentPos = zPhys.CurrentZLevel + zPhys.LocalPosition;
        var targetPos = ent.Comp.TargetMapHeight + 0.2f;
        var currentVelocity = zPhys.Velocity;

        var distanceToTarget = targetPos - currentPos;

        args.Handled = true;
        var targetVelocity = Math.Clamp(distanceToTarget * ent.Comp.FlightSpeed, -ent.Comp.FlightSpeed, ent.Comp.FlightSpeed);
        var velocityDelta = targetVelocity - currentVelocity;

        var upperBound = ent.Comp.TargetMapHeight + 0.9f;
        var lowerBound = ent.Comp.TargetMapHeight + 0.1f;

        var newVelocity = currentVelocity + velocityDelta;
        var nextPos = currentPos + newVelocity;

        if (nextPos > upperBound)
        {
            var maxAllowedVelocity = upperBound - currentPos;
            velocityDelta = maxAllowedVelocity - currentVelocity;
        }
        else if (nextPos < lowerBound)
        {
            var maxAllowedVelocity = lowerBound - currentPos;
            velocityDelta = maxAllowedVelocity - currentVelocity;
        }

        args.VelocityDelta = velocityDelta;
    }

    private void OnGetGravity(Entity<MoonlightZFlyerComponent> ent, ref MoonlightCheckGravityEvent args)
    {
        if (ent.Comp.Active)
            args.Gravity *= 0;
    }

    [PublicAPI]
    public bool TryActivateFlight(Entity<MoonlightZFlyerComponent?> ent, MoonlightZPhysicsComponent? zPhys = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (!Resolve(ent, ref zPhys, false))
            return false;

        if (ent.Comp.Active)
            return false;

        var ev = new MoonlightStartFlightAttemptEvent();
        RaiseLocalEvent(ent, ev);

        if (ev.Cancelled)
            return false;

        ent.Comp.Active = true;
        DirtyField(ent, ent.Comp, nameof(MoonlightZFlyerComponent.Active));

        zPhys.VelocityRaiseEvent = true;
        DirtyField(ent, zPhys, nameof(MoonlightZPhysicsComponent.VelocityRaiseEvent));

        _zLevel.UpdateGravityState((ent, zPhys));
        _zLevel.WakeBody((ent, zPhys));
        _gravity.RefreshWeightless(ent.Owner);

        RaiseLocalEvent(ent, new MoonlightFlightStartedEvent());
        return true;
    }

    [PublicAPI]
    public void DeactivateFlight(Entity<MoonlightZFlyerComponent?> ent, MoonlightZPhysicsComponent? zPhys = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!Resolve(ent, ref zPhys, false))
            return;

        if (!ent.Comp.Active)
            return;

        ent.Comp.Active = false;
        DirtyField(ent, ent.Comp, nameof(MoonlightZFlyerComponent.Active));

        zPhys.VelocityRaiseEvent = false;
        DirtyField(ent, zPhys, nameof(MoonlightZPhysicsComponent.VelocityRaiseEvent));

        _zLevel.UpdateGravityState((ent, zPhys));
        _gravity.RefreshWeightless(ent.Owner);

        RaiseLocalEvent(ent, new MoonlightFlightStoppedEvent());
    }

    [PublicAPI]
    public void SetTargetHeight(Entity<MoonlightZFlyerComponent?> ent, int targetHeight)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.TargetMapHeight == targetHeight)
            return;

        ent.Comp.TargetMapHeight = targetHeight;
        DirtyField(ent, ent.Comp, nameof(MoonlightZFlyerComponent.TargetMapHeight));
        _zLevel.WakeBody(ent.Owner);
    }

    private void StartFlightVisuals(Entity<MoonlightZFlyerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        _appearance.SetData(ent, MoonlightFlightVisuals.Active, true);
        _ambient.SetAmbience(ent, true);
    }

    private void StopFlightVisuals(Entity<MoonlightZFlyerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        _appearance.SetData(ent, MoonlightFlightVisuals.Active, false);
        _ambient.SetAmbience(ent, false);
    }
}

/// <summary>
/// Called on an entity when it attempts to start flight mode. Subscribe and cancel this event if you want to cancel your flight for any reason.
/// </summary>
public sealed partial class MoonlightStartFlightAttemptEvent : CancellableEntityEventArgs;

/// <summary>
/// Called on an entity when it enters flight mode
/// </summary>
public sealed partial class MoonlightFlightStartedEvent : EntityEventArgs;

/// <summary>
/// Called on an entity when it exits flight mode
/// </summary>
public sealed partial class MoonlightFlightStoppedEvent : EntityEventArgs;


/// <summary>
/// Instant Action, raising the target flight level by 1
/// </summary>
public sealed partial class MoonlightZFlightActionUp : InstantActionEvent
{
}

/// <summary>
/// Instant Action, lowering the target flight level by 1
/// </summary>
public sealed partial class MoonlightZFlightActionDown : InstantActionEvent
{
}


[Serializable, NetSerializable]
public enum MoonlightFlightVisuals
{
    Active,
}

/// <summary>
/// DoAfter event for starting flight with a delay
/// </summary>
[Serializable, NetSerializable]
public sealed partial class MoonlightStartFlightDoAfterEvent : SimpleDoAfterEvent
{
}
