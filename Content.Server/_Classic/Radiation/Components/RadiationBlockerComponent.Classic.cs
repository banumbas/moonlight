// Namespace does not match folder structure.
#pragma warning disable IDE0130

namespace Content.Server.Radiation.Components;

public sealed partial class RadiationBlockerComponent
{
    /// <summary>Resistance currently registered at <see cref="CurrentPosition"/>.</summary>
    public float RegisteredResistance;
}
