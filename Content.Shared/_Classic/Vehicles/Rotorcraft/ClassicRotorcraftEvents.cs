using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._Classic.Vehicles.Rotorcraft;

/// <summary>
/// Authoritative rotor and flight phases. Touchdown leaves the rotor running until explicitly stopped.
/// </summary>
[Serializable, NetSerializable]
public enum ClassicRotorcraftStage : byte
{
    Stopped,
    Starting,
    Flying,
    Descending,
    Landed,
    Stopping,
}

/// <summary>
/// Sprite layer keys used by vehicles with an animated rotor.
/// </summary>
[Serializable, NetSerializable]
public enum ClassicRotorcraftVisualLayers : byte
{
    Rotor,
    RotorBlur,
}

/// <summary>
/// Starts the engine, lands, or stops the engine after touchdown.
/// </summary>
public sealed partial class ClassicRotorcraftToggleActionEvent : InstantActionEvent;

/// <summary>
/// Requests a target Z-level one level above the vehicle.
/// </summary>
public sealed partial class ClassicRotorcraftUpActionEvent : InstantActionEvent;

/// <summary>
/// Requests a target Z-level one level below the vehicle.
/// </summary>
public sealed partial class ClassicRotorcraftDownActionEvent : InstantActionEvent;
