using Content.Shared._Classic.Vehicles;
using Robust.Shared.Containers;

namespace Content.Shared._Classic.Vehicles;

public sealed partial class ClassicVehicleBaseSystem
{
    public void InitializeKey()
    {
        SubscribeLocalEvent<ClassicKeyedVehicleComponent, ContainerIsInsertingAttemptEvent>(OnGenericKeyedInsertAttempt);
        SubscribeLocalEvent<ClassicKeyedVehicleComponent, EntInsertedIntoContainerMessage>(OnGenericKeyedEntInserted);
        SubscribeLocalEvent<ClassicKeyedVehicleComponent, EntRemovedFromContainerMessage>(OnGenericKeyedEntRemoved);
        SubscribeLocalEvent<ClassicKeyedVehicleComponent, ClassicVehicleCanRunEvent>(OnGenericKeyedCanRun);
    }

    private void OnGenericKeyedInsertAttempt(Entity<ClassicKeyedVehicleComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || _timing.ApplyingState || !ent.Comp.PreventInvalidInsertion || args.Container.ID != ent.Comp.ContainerId)
            return;

        if (_entityWhitelist.IsWhitelistPass(ent.Comp.KeyWhitelist, args.EntityUid))
            return;

        args.Cancel();
    }

    private void OnGenericKeyedEntInserted(Entity<ClassicKeyedVehicleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (_timing.ApplyingState || args.Container.ID != ent.Comp.ContainerId)
            return;
        RefreshCanRun(ent.Owner);
    }

    private void OnGenericKeyedEntRemoved(Entity<ClassicKeyedVehicleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (_timing.ApplyingState || args.Container.ID != ent.Comp.ContainerId)
            return;
        RefreshCanRun(ent.Owner);
    }

    private void OnGenericKeyedCanRun(Entity<ClassicKeyedVehicleComponent> ent, ref ClassicVehicleCanRunEvent args)
    {
        if (!args.CanRun)
            return;

        args.CanRun = false;

        if (!_container.TryGetContainer(ent.Owner, ent.Comp.ContainerId, out var container))
            return;

        foreach (var contained in container.ContainedEntities)
        {
            if (_entityWhitelist.IsWhitelistFail(ent.Comp.KeyWhitelist, contained))
                continue;

            args.CanRun = true;
            break;
        }
    }
}
