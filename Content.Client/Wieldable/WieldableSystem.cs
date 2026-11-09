using System.Numerics;
using Content.Client.Movement.Components;
using Content.Client.Movement.Systems;
using Content.Shared.Camera;
using Content.Shared.Hands;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Ranged.Components; // classic-add
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Client.GameObjects; // classic-add
using Robust.Shared.Input; // classic-add
using Robust.Client.Timing;

namespace Content.Client.Wieldable;

public sealed partial class WieldableSystem : SharedWieldableSystem
{
    [Dependency] private EyeCursorOffsetSystem _eyeOffset = default!;
    [Dependency] private IClientGameTiming _gameTiming = default!;
    [Dependency] private InputSystem _input = default!; // classic-add

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CursorOffsetRequiresWieldComponent, ItemUnwieldedEvent>(OnEyeOffsetUnwielded);
        SubscribeLocalEvent<CursorOffsetRequiresWieldComponent, HeldRelayedEvent<GetEyeOffsetRelayedEvent>>(OnGetEyeOffset);
    }

    public void OnEyeOffsetUnwielded(Entity<CursorOffsetRequiresWieldComponent> entity, ref ItemUnwieldedEvent args)
    {
        if (!TryComp(entity.Owner, out EyeCursorOffsetComponent? cursorOffsetComp))
            return;

        if (_gameTiming.IsFirstTimePredicted)
        {
            cursorOffsetComp.CurrentPosition = Vector2.Zero;
            cursorOffsetComp.TargetPosition = Vector2.Zero;
        }
    }

    public void OnGetEyeOffset(Entity<CursorOffsetRequiresWieldComponent> entity, ref HeldRelayedEvent<GetEyeOffsetRelayedEvent> args)
    {
        if (!TryComp(entity.Owner, out WieldableComponent? wieldableComp))
            return;

        if (!wieldableComp.Wielded)
            return;

        // classic-start
        if (HasComp<GunComponent>(entity.Owner) &&
            _input.CmdStates.GetState(EngineKeyFunctions.UseSecondary) != BoundKeyState.Down)
        {
            _eyeOffset.ResetOffset(entity.Owner);
            return;
        }
        // classic-end

        var offset = _eyeOffset.OffsetAfterMouse(entity.Owner, null);
        if (offset == null)
            return;

        args.Args.Offset += offset.Value;
    }
}
