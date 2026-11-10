/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Server.GameStates;

namespace Content.Server._Moonlight.PVS;

public sealed partial class MoonlightPvsOverrideSystem : EntitySystem
{
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoonlightPvsOverrideComponent, ComponentStartup>(OnPvsStartup);
        SubscribeLocalEvent<MoonlightPvsOverrideComponent, ComponentShutdown>(OnPvsShutdown);
    }

    private void OnPvsStartup(Entity<MoonlightPvsOverrideComponent> ent, ref ComponentStartup args)
    {
        _pvs.AddGlobalOverride(ent.Owner);
    }

    private void OnPvsShutdown(Entity<MoonlightPvsOverrideComponent> ent, ref ComponentShutdown args)
    {
        _pvs.RemoveGlobalOverride(ent.Owner);
    }
}
