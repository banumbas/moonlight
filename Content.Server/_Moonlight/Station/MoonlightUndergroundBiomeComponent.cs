using Content.Server.Parallax;

namespace Content.Server._Moonlight.Station;

/// <summary>
/// Marks a station terrain grid as an independently generated underground level.
/// </summary>
[RegisterComponent, Access(typeof(MoonlightStationBiomeSystem), typeof(BiomeSystem))]
public sealed partial class MoonlightUndergroundBiomeComponent : Component
{
    [DataField]
    public int Depth;

    [DataField]
    public bool LoadEntities;

    [DataField]
    public bool LoadDecals;
}
