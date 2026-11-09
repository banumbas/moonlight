/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Classic.ZLevels.Mapping;
using Content.Shared._Classic.ZLevels.Roof;
using Robust.Shared.Map;

namespace Content.Server._Classic.ZLevels.Roof;

public sealed partial class ClassicZLevelsRoofSystem : ClassicSharedZLevelsRoofSystem
{
    [Dependency] private ClassicZLevelMappingSystem _zMapping = default!;

    private readonly HashSet<Vector2i> _roofMap = new();
    private readonly HashSet<Vector2i> _opaqueOnMapLevel = new();
    private readonly Dictionary<EntityUid, EntityUid> _planetaryGridMaps = new();
    private bool _recalculatingMapRoofs;

    public override void Initialize()
    {
        base.Initialize();

        InitializeTilePropagation();
        InitMaps();
        InitGrids();
    }
}
