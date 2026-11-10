using System.Numerics;
using Content.Shared._Moonlight.Vehicles.Rotorcraft;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._Moonlight.Vehicles.Rotorcraft;

/// <summary>Client configuration and cached state for rotor transitions and hover attitude.</summary>
[RegisterComponent, Access(typeof(MoonlightRotorcraftVisualsSystem))]
public sealed partial class MoonlightRotorcraftVisualsComponent : Component
{
    /// <summary>Hub position relative to the centre of the rotor RSI frame, in metres.</summary>
    [DataField]
    public Vector2 RotorPivot;

    /// <summary>Blade angular speed while running, in degrees per second.</summary>
    [DataField]
    public float MaxRotationSpeed = 720f;

    /// <summary>Fraction of full rotor speed at which the animated blur starts to appear.</summary>
    [DataField]
    public float BlurStartSpeed = 0.55f;

    /// <summary>Lateral hover travel in metres, applied only to the sprite.</summary>
    [DataField]
    public float SwayAmplitude = 0.14f;

    /// <summary>Lateral hover cycles per second.</summary>
    [DataField]
    public float SwayFrequency = 0.24f;

    /// <summary>Visible bank at full sideways cyclic deflection, in degrees.</summary>
    [DataField]
    public float MaxBankAngle = 12f;

    /// <summary>Passive hover roll, in degrees.</summary>
    [DataField]
    public float HoverRollAngle = 3f;

    /// <summary>Whether the last frame still has an attitude contribution to remove.</summary>
    public bool AttitudeApplied;

    /// <summary>Sprite rotation before attitude animation.</summary>
    public Angle BaseRotation;

    /// <summary>Sprite scale before attitude animation.</summary>
    public Vector2 BaseScale;

    /// <summary>Sprite offset before attitude animation, including this frame's Z offset.</summary>
    public Vector2 BaseOffset;

    /// <summary>Rotation written by the last attitude pass; external replacements are preserved.</summary>
    public Angle AppliedRotation;

    /// <summary>Scale written by the last attitude pass.</summary>
    public Vector2 AppliedScale;

    /// <summary>Offset written by the last attitude pass.</summary>
    public Vector2 AppliedOffset;

    /// <summary>Smoothed client-side bank for interpolated remote vehicles.</summary>
    public float Bank;

    /// <summary>Blend of hover attitude, faded on touchdown.</summary>
    public float HoverBlend;

    /// <summary>Last applied state; stable grounded and running rotors need no frame updates.</summary>
    public MoonlightRotorcraftStage? LastStage;
}
