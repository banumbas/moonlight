/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */


using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;

namespace Content.Shared._Moonlight.ZLevels.Ghost;

public abstract partial class MoonlightSharedZLevelGhostMoverSystem : EntitySystem
{
    [Dependency] private MoonlightSharedZLevelsSystem _zLevel = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoonlightZLevelGhostMoverComponent, MoonlightZLevelActionUp>(OnZLevelUp);
        SubscribeLocalEvent<MoonlightZLevelGhostMoverComponent, MoonlightZLevelActionDown>(OnZLevelDown);
    }

    private void OnZLevelDown(Entity<MoonlightZLevelGhostMoverComponent> ent, ref MoonlightZLevelActionDown args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveDown(ent);
    }

    private void OnZLevelUp(Entity<MoonlightZLevelGhostMoverComponent> ent, ref MoonlightZLevelActionUp args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveUp(ent);
    }
}
