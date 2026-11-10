using Content.Shared._Starlight.Trail;

namespace Content.Shared._Moonlight.Trail;

/// <summary>
/// Configures the motion trail created by active clothing for its wearer.
/// </summary>
[RegisterComponent]
public sealed partial class ClothingTrailComponent : Component
{
    /// <summary>
    /// Settings used to render the wearer's trail on the client.
    /// </summary>
    [DataField]
    public TrailSettings Trail = new();

    /// <summary>
    /// The entity currently receiving this clothing item's trail.
    /// </summary>
    [NonSerialized]
    public EntityUid? Wearer;

    /// <summary>
    /// Whether the trail component on <see cref="Wearer"/> was created by this clothing item.
    /// </summary>
    [NonSerialized]
    public bool OwnsTrail;
}
