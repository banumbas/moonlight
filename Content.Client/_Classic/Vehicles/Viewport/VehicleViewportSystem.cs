using Content.Shared._Classic.Vehicles.Viewport;
using Content.Shared.Movement.Events;
using Robust.Shared.GameObjects;

namespace Content.Client._Classic.Vehicles.Viewport;

public sealed partial class VehicleViewportSystem : SharedVehicleViewportSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleViewportUserComponent, MoveInputEvent>(OnUserMove);
    }

    private void OnUserMove(Entity<VehicleViewportUserComponent> ent, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement)
            return;

        if (TryComp(ent, out EyeComponent? eye))
            _eye.SetTarget(ent.Owner, ent.Comp.PreviousTarget, eye);
    }
}
