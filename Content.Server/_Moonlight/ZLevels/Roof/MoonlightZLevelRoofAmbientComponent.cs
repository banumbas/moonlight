/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Server._Moonlight.ZLevels.Roof;

/// <summary>
/// Stores the ambient color applied below solid terrain on one planetary map level.
/// Keeping it on the map lets grids enrolled after station setup inherit the same color.
/// </summary>
[RegisterComponent, Access(typeof(MoonlightZLevelsRoofSystem))]
public sealed partial class MoonlightZLevelRoofAmbientComponent : Component
{
    [DataField]
    public Color Color = Color.Black;
}
