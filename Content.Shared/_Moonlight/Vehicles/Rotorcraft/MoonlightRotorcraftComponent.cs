using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
using System.Numerics;

namespace Content.Shared._Moonlight.Vehicles.Rotorcraft;

/// <summary>
/// Predicted rotorcraft motion and the authoritative rotor/landing phase.
/// Stopped and Starting leave lift disabled; the remaining phases hold the selected altitude,
/// including touchdown while the rotor is still running.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true, fieldDeltas: true)]
[AutoGenerateComponentPause]
[Access(typeof(MoonlightSharedRotorcraftSystem))]
public sealed partial class MoonlightRotorcraftComponent : Component
{
    /// <summary>
    /// Action used to begin or end flight.
    /// </summary>
    [DataField]
    public EntProtoId ToggleActionProto = "ActionMoonlightRotorcraftToggle";

    /// <summary>
    /// Action used to select the Z-level above the vehicle.
    /// </summary>
    [DataField]
    public EntProtoId UpActionProto = "ActionMoonlightRotorcraftUp";

    /// <summary>
    /// Action used to select the Z-level below the vehicle.
    /// </summary>
    [DataField]
    public EntProtoId DownActionProto = "ActionMoonlightRotorcraftDown";

    /// <summary>Start of rotor spin-up or spin-down, shared with client animation.</summary>
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan RotorTransitionStart;

    /// <summary>Server-selected end time; animation also supports map and VV duration overrides.</summary>
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan RotorTransitionEnd;

    /// <summary>Time spent bringing the rotor up to speed before takeoff.</summary>
    [DataField]
    public TimeSpan RotorStartupTime = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Time spent slowing the rotor after the vehicle has reached its landing surface.
    /// </summary>
    [DataField]
    public TimeSpan RotorShutdownTime = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cruising height within the current Z-level. At this height the vehicle is visibly clear
    /// of the ground and can safely use its horizontal flight controls.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HoverHeight = 0.6f;

    /// <summary>Lowest hover target within a map, clear of its lower boundary.</summary>
    [DataField, AutoNetworkedField]
    public float MinLocalHeight = 0.1f;

    /// <summary>Highest hover target within a map, clear of its upper boundary.</summary>
    [DataField, AutoNetworkedField]
    public float MaxLocalHeight = 0.9f;

    /// <summary>
    /// Minimum distance from the ground required before horizontal flight controls engage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TakeoffClearance = 0.35f;

    /// <summary>
    /// Height held at touchdown, including while the rotor remains running.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LandingHeight = 0f;

    /// <summary>
    /// Maximum distance to a landing surface treated as contact with the ground.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LandingContactHeight = 0.03f;

    /// <summary>
    /// Vertical travel added to the normal hover height while the aircraft is cruising.
    /// Kept small enough that the flight target always remains inside the safe Z-level band.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HoverBobAmplitude = 0.2f;

    /// <summary>
    /// Number of passive hover cycles per second.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HoverBobFrequency = 0.28f;

    /// <summary>Terminal horizontal speed at full cyclic deflection.</summary>
    [DataField, AutoNetworkedField]
    public float MaxSpeed = 7f;

    /// <summary>Horizontal thrust acceleration at full cyclic deflection.</summary>
    [DataField, AutoNetworkedField]
    public float Acceleration = 3f;

    /// <summary>Seconds for the rotor disc to approach a new cyclic input; short taps produce only a small tilt.</summary>
    [DataField, AutoNetworkedField]
    public float CyclicResponse = 0.8f;

    /// <summary>Additional horizontal damping during a landing approach.</summary>
    [DataField, AutoNetworkedField]
    public float LandingDrag = 1.5f;

    /// <summary>Maximum yaw speed in radians per second.</summary>
    [DataField, AutoNetworkedField]
    public float TurnSpeed = MathF.PI / 3f;

    /// <summary>Maximum yaw acceleration in radians per second squared.</summary>
    [DataField, AutoNetworkedField]
    public float TurnAcceleration = MathF.PI / 2f;

    /// <summary>Yaw response to heading error, in inverse seconds.</summary>
    [DataField, AutoNetworkedField]
    public float TurnResponse = 2f;

    /// <summary>Vertical spring strength towards the selected altitude.</summary>
    [DataField, AutoNetworkedField]
    public float AltitudeStiffness = 8f;

    /// <summary>Damping of the vertical spring; preserves smooth lift and braking.</summary>
    [DataField, AutoNetworkedField]
    public float AltitudeDamping = 4.5f;

    /// <summary>Maximum climb/descent speed, in levels per second.</summary>
    [DataField, AutoNetworkedField]
    public float VerticalSpeed = 0.65f;

    /// <summary>Maximum lift acceleration, in levels per second squared.</summary>
    [DataField, AutoNetworkedField]
    public float VerticalAcceleration = 0.75f;

    /// <summary>Smoothed rotor-disc tilt in map coordinates, shared for prediction and banking visuals.</summary>
    [AutoNetworkedField]
    public Vector2 Cyclic;

    /// <summary>Heading retained when the pilot releases the controls.</summary>
    [AutoNetworkedField]
    public Angle TargetHeading;

    /// <summary>Selected absolute Z altitude before physical hover bob is applied.</summary>
    [AutoNetworkedField]
    public float TargetAltitude;

    /// <summary>
    /// Authoritative phase of flight and rotor operation.
    /// </summary>
    [AutoNetworkedField]
    public MoonlightRotorcraftStage Stage;

    /// <summary>
    /// Server-authoritative result of the ground-clearance check. Ground-height caches
    /// are intentionally local optimisation data, so clients use this replicated flag when
    /// predicting the driver's horizontal flight.
    /// </summary>
    [AutoNetworkedField]
    public bool AirControlsActive;
}
