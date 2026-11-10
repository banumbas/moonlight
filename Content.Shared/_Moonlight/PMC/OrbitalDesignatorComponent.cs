using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Moonlight.PMC;

[RegisterComponent, NetworkedComponent]
public sealed partial class OrbitalDesignatorComponent : Component
{
    /// <summary>
    /// Prototype to spawn at target coordinates.
    /// </summary>
    [DataField]
    public string MarkerPrototype = "PMCOrbitalMarker";

    /// <summary>
    /// Time it takes to use the device.
    /// </summary>
    [DataField]
    public float DoAfterTime = 5f;

    /// <summary>
    /// Maximum targeting range for orbital designator.
    /// </summary>
    [DataField]
    public float Range = 30f;

    /// <summary>
    /// Currently running do-after for orbital targeting.
    /// </summary>
    public DoAfterId? DoAfter;

    /// <summary>
    /// Target coordinates for active do-after.
    /// </summary>
    public EntityCoordinates TargetCoordinates;

    /// <summary>
    /// User entity performing active do-after.
    /// </summary>
    public EntityUid? TargetUser;
}
