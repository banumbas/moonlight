using Content.Shared.Parallax.Biomes;
using Robust.Shared.Prototypes;

namespace Content.Server._Classic.Station;

/// <summary>
/// Sets up the station's main grid as a planetary biome grid without merging grids at runtime.
/// </summary>
[RegisterComponent, Access(typeof(ClassicStationBiomeSystem))]
public sealed partial class ClassicStationBiomeComponent : Component
{
    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Biome = "GrasslandsClassic";

    [DataField]
    public int? Seed;

    [DataField]
    public Color MapLightColor = Color.FromHex("#D8B059");

    [DataField]
    public bool DisableGridSplitting = true;

    [DataField]
    public string? Parallax;

    /// <summary>
    /// Data-driven terrain configuration for maps below the surface.
    /// </summary>
    [DataField]
    public List<ClassicUndergroundLevelData> UndergroundLevels = new();
}

[DataDefinition]
public sealed partial class ClassicUndergroundLevelData
{
    [DataField(required: true)]
    public int Depth;

    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Biome;

    [DataField]
    public bool LoadEntities;

    [DataField]
    public bool LoadDecals;

    /// <summary>
    /// Ambient color used where terrain on a higher Z-level blocks planetary light.
    /// Open columns still receive the surface day/night cycle.
    /// </summary>
    [DataField]
    public Color MapLightColor = Color.FromHex("#050505");

    [DataField]
    public float Temperature = 293.15f;

    [DataField]
    public string Parallax = "Dirt";
}
