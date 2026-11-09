/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Server.GameStates;

namespace Content.Server._Classic.PVS;

public sealed partial class ClassicPvsOverrideSystem : EntitySystem
{
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClassicPvsOverrideComponent, ComponentStartup>(OnPvsStartup);
        SubscribeLocalEvent<ClassicPvsOverrideComponent, ComponentShutdown>(OnPvsShutdown);
    }

    private void OnPvsStartup(Entity<ClassicPvsOverrideComponent> ent, ref ComponentStartup args)
    {
        _pvs.AddGlobalOverride(ent.Owner);
    }

    private void OnPvsShutdown(Entity<ClassicPvsOverrideComponent> ent, ref ComponentShutdown args)
    {
        _pvs.RemoveGlobalOverride(ent.Owner);
    }
}
