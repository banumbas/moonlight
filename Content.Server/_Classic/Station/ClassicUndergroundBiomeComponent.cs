using Content.Server.Parallax;

namespace Content.Server._Classic.Station;

/// <summary>
/// Marks a station terrain grid as an independently generated underground level.
/// </summary>
[RegisterComponent, Access(typeof(ClassicStationBiomeSystem), typeof(BiomeSystem))]
public sealed partial class ClassicUndergroundBiomeComponent : Component
{
    [DataField]
    public int Depth;

    [DataField]
    public bool LoadEntities;

    [DataField]
    public bool LoadDecals;
}
