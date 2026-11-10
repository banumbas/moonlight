using Content.Shared._Moonlight.Vehicles;
using Content.Shared._Moonlight.Vehicles.Viewport;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server._Moonlight.Vehicles.Viewport;

public sealed partial class VehicleViewportSystem : SharedVehicleViewportSystem
{
    private static readonly EntProtoId VehiclePeekAnchor = "VehiclePeekAnchor";

    [Dependency] private readonly MoonlightVehicleSystem _vehicles = default!;
    [Dependency] private readonly VehicleViewToggleSystem _viewToggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleViewportComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<VehicleViewportComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<VehicleViewportComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<VehicleEnterComponent, GetVerbsEvent<AlternativeVerb>>(OnVehicleEnterVerbs);
        SubscribeLocalEvent<VehicleViewportUserComponent, MoveInputEvent>(OnUserMove);
    }

    private void OnActivate(Entity<VehicleViewportComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryToggleViewport(ent, args.User);
    }

    private void OnUserMove(Entity<VehicleViewportUserComponent> ent, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement)
            return;

        CloseViewport(ent.Owner, ent.Comp);
    }

    private void OnInteractUsing(Entity<VehicleViewportComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryToggleViewport(ent, args.User);
    }

    private void OnInteractHand(Entity<VehicleViewportComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryToggleViewport(ent, args.User);
    }

    private void OnVehicleEnterVerbs(Entity<VehicleEnterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract ||
            !args.CanAccess ||
            args.Using != null ||
            !_vehicles.TryFindEntryPoint(ent.Owner, args.User, out var entryIndex))
        {
            return;
        }

        var user = args.User;
        var vehicle = ent.Owner;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("rmc-vehicle-look-inside"),
            Act = () => ToggleInteriorPeek(user, vehicle, entryIndex),
        });
    }

    private bool TryToggleViewport(Entity<VehicleViewportComponent> source, EntityUid user)
    {
        if (!_vehicles.TryGetVehicleFromInterior(source, out var vehicle) || vehicle is null)
            return false;

        if (TryComp(user, out VehicleViewportUserComponent? existing))
        {
            CloseViewport(user, existing);
            return true;
        }

        var userState = EnsureComp<VehicleViewportUserComponent>(user);
        if (TryComp(user, out EyeComponent? newEye))
            userState.PreviousTarget = newEye.Target;
        userState.Source = source;
        userState.ExteriorOffset = source.Comp.ExteriorOffset;
        Dirty(user, userState);

        _eye.SetTarget(user, vehicle);
        _viewToggle.EnableViewToggle(user, vehicle.Value, source, userState.PreviousTarget, isOutside: true);
        return true;
    }

    private bool ToggleInteriorPeek(EntityUid user, EntityUid vehicle, int entryIndex)
    {
        if (TryComp(user, out VehicleViewportUserComponent? existing))
        {
            if (existing.Source == vehicle)
            {
                CloseViewport(user, existing);
                return true;
            }

            CloseViewport(user, existing);
        }

        if (!_vehicles.TryGetInteriorEntryCoordinates(vehicle, entryIndex, out var peekCoords))
            return false;

        var userState = EnsureComp<VehicleViewportUserComponent>(user);
        if (TryComp(user, out EyeComponent? eye))
            userState.PreviousTarget = eye.Target;
        userState.Source = vehicle;
        userState.PeekTarget = Spawn(VehiclePeekAnchor, peekCoords);

        _eye.SetTarget(user, userState.PeekTarget);
        Dirty(user, userState);
        return true;
    }

    private void CloseViewport(EntityUid user, VehicleViewportUserComponent? state = null)
    {
        state ??= CompOrNull<VehicleViewportUserComponent>(user);
        if (state == null)
            return;

        if (TryComp(user, out EyeComponent? eye))
            _eye.SetTarget(user, state.PreviousTarget, eye);

        if (state.Source != null)
            _viewToggle.DisableViewToggle(user, state.Source.Value);

        if (state.PeekTarget is { } peekTarget && Exists(peekTarget))
            QueueDel(peekTarget);

        RemCompDeferred<VehicleViewportUserComponent>(user);
    }
}
