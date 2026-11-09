/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._Classic.ZLevels.Core.EntitySystems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.ZLevels.Core.Components;

/// <summary>
/// Allows entity to see through Z-levels
/// </summary>
[UnsavedComponent]
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
[Access(typeof(ClassicSharedZLevelsSystem))]
public sealed partial class ClassicZLevelViewerComponent : Component
{
    public HashSet<EntityUid> Eyes = new();

    /// <summary>Server-owned subset of <see cref="Eyes"/> used only while looking upward.</summary>
    public HashSet<EntityUid> UpperEyes = new();

    /// <summary>Last resolved view map, including viewers that cannot create auxiliary PVS eyes.</summary>
    public EntityUid? ViewMap;

    /// <summary>Last position used to place the server's auxiliary PVS eyes.</summary>
    public Vector2 ViewPosition;

    /// <summary>Last resolved biome loading permission; changes rebuild the server's auxiliary PVS eyes.</summary>
    public bool CanLoadBiomes;

    /// <summary>
    /// We can look at 1 z-level up.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool LookUp;

    [AutoNetworkedField]
    public byte VisibleZLevelsAbove;

    [AutoNetworkedField]
    public EntityUid? VisibleZLevelsAboveMap;

    [AutoNetworkedField]
    public Vector2i VisibleZLevelsAboveTile;

    #region Actions

    [DataField]
    public EntProtoId ActionId = "ClassicActionToggleLookUp";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    #endregion
}
