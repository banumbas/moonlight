using Content.Shared._Moonlight.Vehicles.Rotorcraft;
using Robust.Shared.GameObjects;

namespace Content.Client._Moonlight.Vehicles.Rotorcraft;

/// <summary>Runs the shared flight controller for the local pilot.</summary>
public sealed partial class MoonlightRotorcraftSystem : MoonlightSharedRotorcraftSystem
{
    [Dependency] private MoonlightRotorcraftVisualsSystem _visuals = default!;

    protected override void OnShutdown(Entity<MoonlightRotorcraftComponent> ent, ref ComponentShutdown args)
    {
        base.OnShutdown(ent, ref args);
        _visuals.ResetRotorcraft(ent);
    }
}
