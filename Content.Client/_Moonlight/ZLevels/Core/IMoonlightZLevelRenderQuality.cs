/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Client._Moonlight.ZLevels.Core;

/// <summary>
/// Selects optional content-side render quality for a Z-level eye.
/// Regular eyes do not implement this interface and retain the default pipeline.
/// </summary>
public interface IMoonlightZLevelRenderQuality
{
    bool DrawContentLightBlur { get; }

    bool DrawAmbientOcclusion { get; }

    bool UseDirectContentLightTarget { get; }
}
