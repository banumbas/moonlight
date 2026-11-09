using Content.Server.Parallax;

namespace Content.Server._Classic.Station;

[RegisterComponent, Access(typeof(BiomeSystem))]
public sealed partial class ClassicBiomeAmbientMarkerComponent : Component
{
    [DataField]
    public bool PlayerTouched;
}
