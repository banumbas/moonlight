using Content.Shared._Classic.Vehicles.Rotorcraft;
using Robust.Shared.GameObjects;

namespace Content.Client._Classic.Vehicles.Rotorcraft;

/// <summary>Runs the shared flight controller for the local pilot.</summary>
public sealed partial class ClassicRotorcraftSystem : ClassicSharedRotorcraftSystem
{
    [Dependency] private ClassicRotorcraftVisualsSystem _visuals = default!;

    protected override void OnShutdown(Entity<ClassicRotorcraftComponent> ent, ref ComponentShutdown args)
    {
        base.OnShutdown(ent, ref args);
        _visuals.ResetRotorcraft(ent);
    }
}
