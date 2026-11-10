using Content.Server.Parallax;

namespace Content.Server._Moonlight.Station;

[RegisterComponent, Access(typeof(BiomeSystem))]
public sealed partial class MoonlightBiomeAmbientMarkerComponent : Component
{
    [DataField]
    public bool PlayerTouched;
}
