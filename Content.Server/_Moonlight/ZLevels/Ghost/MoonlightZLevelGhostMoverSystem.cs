/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Moonlight.ZLevels.Ghost;
using Content.Shared.Actions;

namespace Content.Server._Moonlight.ZLevels.Ghost;

public sealed partial class MoonlightZLevelGhostMoverSystem : MoonlightSharedZLevelGhostMoverSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoonlightZLevelGhostMoverComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MoonlightZLevelGhostMoverComponent, ComponentRemove>(OnRemove);
    }

    private void OnMapInit(Entity<MoonlightZLevelGhostMoverComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ZLevelUpActionEntity, ent.Comp.UpActionProto);
        _actions.AddAction(ent, ref ent.Comp.ZLevelDownActionEntity, ent.Comp.DownActionProto);
    }

    private void OnRemove(Entity<MoonlightZLevelGhostMoverComponent> ent, ref ComponentRemove args)
    {
        _actions.RemoveAction(ent.Comp.ZLevelUpActionEntity);
        _actions.RemoveAction(ent.Comp.ZLevelDownActionEntity);
    }
}
