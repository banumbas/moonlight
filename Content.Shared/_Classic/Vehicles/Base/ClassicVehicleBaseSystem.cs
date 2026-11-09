using Content.Shared._Classic.Vehicles;
using Content.Shared.Damage.Systems;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared._Classic.Vehicles;

public sealed partial class ClassicVehicleBaseSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityWhitelistSystem _entityWhitelist = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        InitializeOperator();
        InitializeKey();

        SubscribeLocalEvent<ClassicVehicleComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<ClassicVehicleComponent, UpdateCanMoveEvent>(OnVehicleUpdateCanMove);
        SubscribeLocalEvent<ClassicVehicleComponent, ComponentShutdown>(OnVehicleShutdown);
        SubscribeLocalEvent<ClassicVehicleComponent, GetAdditionalAccessEvent>(OnVehicleGetAdditionalAccess);

        SubscribeLocalEvent<ClassicVehicleOperatorComponent, ComponentShutdown>(OnOperatorShutdown);
    }

    private void OnBeforeDamageChanged(Entity<ClassicVehicleComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!ent.Comp.TransferDamage || !args.Damage.AnyPositive() || ent.Comp.Operator is not { } operatorUid)
            return;

        var vehicleMap = Transform(ent.Owner).MapID;
        var operatorMap = Transform(operatorUid).MapID;
        if (vehicleMap != operatorMap)
            return;

        var damage = DamageSpecifier.GetPositive(args.Damage);

        if (ent.Comp.TransferDamageModifier is { } modifierSet)
            damage = DamageSpecifier.ApplyModifierSet(damage, modifierSet);

        _damageable.TryChangeDamage(operatorUid, damage, origin: args.Origin);
    }

    private void OnVehicleUpdateCanMove(Entity<ClassicVehicleComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (!CanRun(ent.AsNullable()))
            args.Cancel();
    }

    /// <summary>Checks the vehicle's shared operating restrictions, including frame integrity and installed equipment.</summary>
    [PublicAPI]
    public bool CanRun(Entity<ClassicVehicleComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        var ev = new ClassicVehicleCanRunEvent((ent, ent.Comp));
        RaiseLocalEvent(ent, ref ev);
        return ev.CanRun;
    }

    private void OnVehicleShutdown(Entity<ClassicVehicleComponent> ent, ref ComponentShutdown args)
    {
        TryRemoveOperator(ent);
    }

    private void OnVehicleGetAdditionalAccess(Entity<ClassicVehicleComponent> ent, ref GetAdditionalAccessEvent args)
    {
        if (ent.Comp.Operator is { } operatorUid)
            args.Entities.Add(operatorUid);
    }

    private void OnOperatorShutdown(Entity<ClassicVehicleOperatorComponent> ent, ref ComponentShutdown args)
    {
        TryRemoveOperator((ent, ent));
    }

    public bool TrySetOperator(Entity<ClassicVehicleComponent> entity, EntityUid? uid, bool removeExisting = true)
    {
        if (entity.Comp.Operator == null && uid is null)
            return false;

        if (entity.Comp.Operator == uid)
            return true;

        if (uid != null &&
            TryComp<ClassicVehicleOperatorComponent>(uid, out var eOperator) &&
            eOperator.Vehicle != null &&
            eOperator.Vehicle != entity.Owner)
        {
            if (!removeExisting)
                return false;

            if (TryComp<ClassicVehicleComponent>(eOperator.Vehicle, out var oldVeh))
                TryRemoveOperator((eOperator.Vehicle.Value, oldVeh));
        }

        if (!removeExisting && entity.Comp.Operator is not null)
            return false;

        if (uid != null && !CanOperate(entity.AsNullable(), uid.Value))
            return false;

        var oldOperator = entity.Comp.Operator;

        if (entity.Comp.Operator is { } currentOperator && TryComp<ClassicVehicleOperatorComponent>(currentOperator, out var currentOperatorComponent))
        {
            var exitEvent = new OnClassicVehicleExitedEvent(entity, currentOperator);
            RaiseLocalEvent(currentOperator, ref exitEvent);

            currentOperatorComponent.Vehicle = null;
            RemCompDeferred<ClassicVehicleOperatorComponent>(currentOperator);
            RemCompDeferred<RelayInputMoverComponent>(currentOperator);
            RemCompDeferred<GridVehicleOperatorComponent>(currentOperator);
        }

        entity.Comp.Operator = uid;

        if (uid != null)
        {
            var vehicleOperator = EnsureComp<ClassicVehicleOperatorComponent>(uid.Value);
            vehicleOperator.Vehicle = entity.Owner;
            Dirty(uid.Value, vehicleOperator);

            if (entity.Comp.MovementKind == ClassicVehicleMovementKind.Standard)
            {
                _mover.SetRelay(uid.Value, entity);
            }
            else
            {
                if (entity.Comp.MovementKind == ClassicVehicleMovementKind.Grid)
                {
                    EnsureComp<GridVehicleMoverComponent>(entity);
                    EnsureComp<GridVehicleOperatorComponent>(uid.Value);
                }
                RemCompDeferred<RelayInputMoverComponent>(uid.Value);
                RemCompDeferred<MovementRelayTargetComponent>(entity);
            }

            var enterEvent = new OnClassicVehicleEnteredEvent(entity, uid.Value);
            RaiseLocalEvent(uid.Value, ref enterEvent);
        }
        else
        {
            RemCompDeferred<MovementRelayTargetComponent>(entity);
        }

        RefreshCanRun((entity, entity.Comp));

        var setEvent = new ClassicVehicleOperatorSetEvent(uid, oldOperator);
        RaiseLocalEvent(entity, ref setEvent);

        Dirty(entity);
        return true;
    }

    [PublicAPI]
    public bool TryRemoveOperator(Entity<ClassicVehicleComponent> entity)
    {
        return TrySetOperator(entity, null, removeExisting: true);
    }

    [PublicAPI]
    public bool TryRemoveOperator(Entity<ClassicVehicleOperatorComponent?> operatorEntity)
    {
        if (!Resolve(operatorEntity, ref operatorEntity.Comp, false))
            return true;

        if (!TryComp<ClassicVehicleComponent>(operatorEntity.Comp.Vehicle, out var vehicle))
            return true;

        return TrySetOperator((operatorEntity.Comp.Vehicle.Value, vehicle), null, removeExisting: true);
    }

    [PublicAPI]
    public bool TryGetOperator(Entity<ClassicVehicleComponent?> entity, [NotNullWhen(true)] out Entity<ClassicVehicleOperatorComponent>? operatorEnt)
    {
        operatorEnt = null;
        if (!Resolve(entity, ref entity.Comp))
            return false;

        if (entity.Comp.Operator is not { } operatorUid)
            return false;

        if (!TryComp<ClassicVehicleOperatorComponent>(operatorUid, out var operatorComponent))
            return false;

        operatorEnt = (operatorUid, operatorComponent);
        return true;
    }

    public EntityUid? GetOperatorOrNull(Entity<ClassicVehicleComponent?> entity)
    {
        TryGetOperator(entity, out var operatorEnt);
        return operatorEnt;
    }

    [PublicAPI]
    public bool HasOperator(Entity<ClassicVehicleComponent?> entity)
    {
        return TryGetOperator(entity, out _);
    }

    public bool CanOperate(Entity<ClassicVehicleComponent?> entity, EntityUid uid)
    {
        if (!Resolve(entity, ref entity.Comp))
            return false;

        if (_entityWhitelist.IsWhitelistFail(entity.Comp.OperatorWhitelist, uid))
            return false;

        return _actionBlocker.CanConsciouslyPerformAction(uid);
    }

    public void RefreshCanRun(Entity<ClassicVehicleComponent?> entity)
    {
        if (TerminatingOrDeleted(entity))
            return;

        if (!Resolve(entity, ref entity.Comp))
            return;

        _actionBlocker.UpdateCanMove(entity);
        UpdateAppearance((entity, entity.Comp));
    }

    private void UpdateAppearance(Entity<ClassicVehicleComponent> entity)
    {
        if (!TryComp<AppearanceComponent>(entity, out var appearance))
            return;

        if (TryComp<InputMoverComponent>(entity, out var inputMover))
            _appearance.SetData(entity, ClassicVehicleVisuals.CanRun, inputMover.CanMove, appearance);

        _appearance.SetData(entity, ClassicVehicleVisuals.HasOperator, entity.Comp.Operator is not null, appearance);
    }
}
