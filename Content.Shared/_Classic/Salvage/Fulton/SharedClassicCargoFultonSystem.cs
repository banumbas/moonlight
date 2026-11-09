using System.Numerics;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Classic.Salvage.Fulton;

public abstract partial class SharedClassicCargoFultonSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public static readonly EntProtoId EffectProto = "FultonEffect";
    protected static readonly Vector2 EffectOffset = Vector2.Zero;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<ClassicCargoFultonComponent> _fultonQuery;
    private EntityQuery<ClassicFultonSoldComponent> _fultonedQuery;
    private EntityQuery<AppearanceComponent> _appearanceQuery;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _fultonQuery = GetEntityQuery<ClassicCargoFultonComponent>();
        _fultonedQuery = GetEntityQuery<ClassicFultonSoldComponent>();
        _appearanceQuery = GetEntityQuery<AppearanceComponent>();

        SubscribeLocalEvent<ClassicCargoFultonComponent, AfterInteractEvent>(OnCargoFultonInteract);
        SubscribeLocalEvent<ClassicCargoFultonDoAfterEvent>(OnCargoFultonDoAfter);

        SubscribeLocalEvent<ClassicFultonSoldComponent, GetVerbsEvent<InteractionVerb>>(OnFultonedGetVerbs);
        SubscribeLocalEvent<ClassicFultonSoldComponent, ExaminedEvent>(OnFultonedExamine);
        SubscribeLocalEvent<ClassicFultonSoldComponent, EntGotInsertedIntoContainerMessage>(OnFultonContainerInserted);
    }

    private void OnFultonContainerInserted(
        Entity<ClassicFultonSoldComponent> ent,
        ref EntGotInsertedIntoContainerMessage args)
    {
        RemCompDeferred<ClassicFultonSoldComponent>(ent);
    }

    private void OnFultonedExamine(Entity<ClassicFultonSoldComponent> ent, ref ExaminedEvent args)
    {
        var remaining = ent.Comp.NextFulton + _metadata.GetPauseTime(ent.Owner) - _timing.CurTime;
        var message = Loc.GetString("fulton-examine", ("time", $"{remaining.TotalSeconds:0.00}"));

        args.PushText(message);
    }

    private void OnFultonedGetVerbs(Entity<ClassicFultonSoldComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new InteractionVerb()
        {
            Text = Loc.GetString("fulton-remove"),
            Act = () => Unfulton(ent.Owner, ent.Comp)
        });
    }

    private void Unfulton(EntityUid uid, ClassicFultonSoldComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || !component.Removeable)
            return;

        RemCompDeferred<ClassicFultonSoldComponent>(uid);
    }

    private void OnCargoFultonDoAfter(ClassicCargoFultonDoAfterEvent args)
    {
        if (args.Cancelled ||
            args.Target == null ||
            args.Used == null ||
            !_fultonQuery.TryComp(args.Used.Value, out var fulton))
        {
            return;
        }

        if (!CanUseCargoFulton(args.Target.Value, fulton) ||
            !CanApplyCargoFulton(args.Target.Value, fulton))
        {
            return;
        }

        if (!CanCompleteCargoFulton(args.Used.Value, args.Target.Value, args.User, fulton))
            return;

        if (!_stack.TryUse(args.Used.Value, 1))
            return;

        var fultoned = EnsureComp<ClassicFultonSoldComponent>(args.Target.Value);
        fultoned.NextFulton = _timing.CurTime + fulton.FultonDuration;
        fultoned.FultonDuration = fulton.FultonDuration;
        fultoned.Sound = fulton.LaunchSound;
        fultoned.Removeable = fulton.Removeable;
        UpdateAppearance(args.Target.Value, fultoned);
        Dirty(args.Target.Value, fultoned);

        OnCargoFultonApplied(args.Used.Value, args.Target.Value, args.User, fulton, fultoned);
        _audio.PlayPredicted(fulton.FultonSound, args.Target.Value, args.User);
    }

    private void OnCargoFultonInteract(Entity<ClassicCargoFultonComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Target == null || args.Handled || !args.CanReach)
            return;

        if (!CanUseCargoFulton(args.Target.Value, ent.Comp))
        {
            _popup.PopupClient(Loc.GetString("fulton-invalid"), ent.Owner, args.User);
            return;
        }

        if (_fultonedQuery.HasComp(args.Target.Value))
        {
            _popup.PopupClient(Loc.GetString("fulton-fultoned"), ent.Owner, args.User);
            return;
        }

        if (!CanApplyCargoFulton(args.Target.Value, ent.Comp))
        {
            _popup.PopupClient(Loc.GetString("fulton-invalid"), ent.Owner, args.User);
            return;
        }

        args.Handled = true;

        var ev = new ClassicCargoFultonDoAfterEvent();
        _doAfter.TryStartDoAfter(
            new DoAfterArgs(EntityManager, args.User, ent.Comp.ApplyFultonDuration, ev, args.Target.Value, args.Target.Value, ent.Owner)
            {
                MovementThreshold = 0.5f,
                BreakOnMove = true,
                Broadcast = true,
                NeedHand = true,
            });
    }

    protected virtual bool CanCompleteCargoFulton(
        EntityUid fultonUid,
        EntityUid targetUid,
        EntityUid userUid,
        ClassicCargoFultonComponent component)
    {
        return true;
    }

    protected virtual void OnCargoFultonApplied(
        EntityUid fultonUid,
        EntityUid targetUid,
        EntityUid userUid,
        ClassicCargoFultonComponent fulton,
        ClassicFultonSoldComponent fultoned)
    {
    }

    protected virtual void UpdateAppearance(EntityUid uid, ClassicFultonSoldComponent fultoned)
    {
    }

    protected bool CanUseCargoFulton(EntityUid target, ClassicCargoFultonComponent component)
    {
        return !component.RequiresSensorTower || TryFindActiveSensorTower(target, out _);
    }

    protected bool CanApplyCargoFulton(EntityUid targetUid, ClassicCargoFultonComponent component)
    {
        if (!CanCargoFulton(targetUid))
            return false;

        if (_whitelist.IsWhitelistFailOrNull(component.Whitelist, targetUid))
            return false;

        return true;
    }

    protected bool CanCargoFulton(EntityUid uid)
    {
        if (!_xformQuery.TryComp(uid, out var xform) || xform.Anchored)
            return false;

        if (_container.IsEntityInContainer(uid))
            return false;

        return true;
    }

    public bool TryFindActiveSensorTower(EntityUid targetUid, out EntityUid towerUid)
    {
        towerUid = EntityUid.Invalid;

        if (!_xformQuery.TryComp(targetUid, out var targetXform))
            return false;

        var targetCoords = _transform.GetMapCoordinates(targetUid, xform: targetXform);
        var query = EntityQueryEnumerator<FultonSensorTowerComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var tower, out var towerXform))
        {
            if (!IsSensorTowerPowered(uid))
                continue;

            var towerCoords = _transform.GetMapCoordinates(uid, xform: towerXform);
            if (towerCoords.MapId != targetCoords.MapId)
                continue;

            if ((towerCoords.Position - targetCoords.Position).LengthSquared() > tower.Range * tower.Range)
                continue;

            towerUid = uid;
            return true;
        }

        return false;
    }

    private bool IsSensorTowerPowered(EntityUid uid)
    {
        return _appearanceQuery.TryComp(uid, out var appearance) &&
               _appearance.TryGetData<bool>(uid, PowerDeviceVisuals.Powered, out var powered, appearance) &&
               powered;
    }

    [Serializable, NetSerializable]
    private sealed partial class ClassicCargoFultonDoAfterEvent : SimpleDoAfterEvent
    {
    }

    [Serializable, NetSerializable]
    protected sealed class ClassicFultonAnimationMessage : EntityEventArgs
    {
        public NetEntity Entity;
        public NetCoordinates Coordinates;
    }
}
