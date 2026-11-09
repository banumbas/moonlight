using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Classic.PMC;

/// <summary>
///     Dispatches an automatic global announcement for PMC after a configured delay from round start.
/// </summary>
[RegisterComponent, Access(typeof(PMCAutoAnnouncementSystem))]
public sealed partial class PMCAutoAnnouncementComponent : Component
{
    /// <summary>
    ///     Delay from round start before the announcement is broadcast.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Whether the announcement has already been broadcast.
    /// </summary>
    [DataField]
    public bool Announced;

    /// <summary>
    ///     Sender title locale key.
    /// </summary>
    [DataField]
    public LocId Sender = "pmc-announcement-sender";

    /// <summary>
    ///     Announcement text locale key.
    /// </summary>
    [DataField]
    public LocId Message = "pmc-announcement-message";

    /// <summary>
    ///     Color of the announcement text in chat. White by default.
    /// </summary>
    [DataField]
    public Color Color = Color.White;
}
