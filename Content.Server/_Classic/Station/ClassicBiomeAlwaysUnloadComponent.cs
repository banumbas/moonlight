using Content.Server.Parallax;

namespace Content.Server._Classic.Station;

/// <summary>
/// Opt-in for mutable procedural entities that are safe to discard and regenerate with their chunk.
/// </summary>
[RegisterComponent, Access(typeof(BiomeSystem))]
public sealed partial class ClassicBiomeAlwaysUnloadComponent : Component;
