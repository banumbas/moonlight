/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Moonlight.ZLevels.Ghost;

/// <summary>
/// component that allows you to quickly move between Z levels
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MoonlightZLevelGhostMoverComponent : Component
{
    [DataField]
    public EntProtoId UpActionProto = "MoonlightActionZLevelUp";

    [DataField, AutoNetworkedField]
    public EntityUid? ZLevelUpActionEntity;

    [DataField]
    public EntProtoId DownActionProto = "MoonlightActionZLevelDown";

    [DataField, AutoNetworkedField]
    public EntityUid? ZLevelDownActionEntity;
}

/// <summary>
/// Should be relayed upon using the action.
/// </summary>
public sealed partial class MoonlightZLevelActionUp : InstantActionEvent;

/// <summary>
/// Should be relayed upon using the action.
/// </summary>
public sealed partial class MoonlightZLevelActionDown : InstantActionEvent;
