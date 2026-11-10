using Robust.Shared.Audio;

namespace Content.Server._Moonlight.Vehicles.Rotorcraft;

/// <summary>Optional rotor sounds mirrored onto the vehicle exterior and its separate cabin map.</summary>
[RegisterComponent, Access(typeof(MoonlightRotorcraftSystem))]
public sealed partial class MoonlightRotorcraftSoundComponent : Component
{
    /// <summary>Rotor spin-up before takeoff.</summary>
    [DataField]
    public SoundSpecifier? RotorStartSound;

    /// <summary>Rotor loop while the rotor is turning.</summary>
    [DataField]
    public SoundSpecifier? RotorSound;

    /// <summary>Rotor spin-down after touchdown.</summary>
    [DataField]
    public SoundSpecifier? RotorStopSound;

    /// <summary>Playback parameters for the exterior PVS emitter.</summary>
    [DataField]
    public AudioParams RotorAudioParams = AudioParams.Default.WithVolume(-3f).WithMaxDistance(16f);

    /// <summary>Playback parameters for the quieter cabin PVS emitter.</summary>
    [DataField]
    public AudioParams InteriorRotorAudioParams = AudioParams.Default.WithVolume(-6f).WithMaxDistance(20f);

    /// <summary>Current exterior rotor audio entity.</summary>
    public EntityUid? ExteriorRotorAudio;

    /// <summary>Current cabin rotor audio entity.</summary>
    public EntityUid? InteriorRotorAudio;
}
