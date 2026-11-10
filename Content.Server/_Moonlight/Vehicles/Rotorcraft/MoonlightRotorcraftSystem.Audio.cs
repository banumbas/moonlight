using Content.Shared._Moonlight.Vehicles;
using Content.Shared._Moonlight.Vehicles.Rotorcraft;
using Robust.Shared.Audio;

namespace Content.Server._Moonlight.Vehicles.Rotorcraft;

public sealed partial class MoonlightRotorcraftSystem
{
    [Dependency] private EntityQuery<MoonlightRotorcraftSoundComponent> _soundQuery = default!;

    private void OnSoundStartup(Entity<MoonlightRotorcraftSoundComponent> ent, ref ComponentStartup args)
    {
        if (_flightQuery.TryComp(ent, out var flight))
            SyncRotorAudio((ent, flight));
    }

    private void OnSoundShutdown(Entity<MoonlightRotorcraftSoundComponent> ent, ref ComponentShutdown args)
    {
        StopRotorAudio(ent.Comp);
    }

    /// <summary>Mirrors rotor audio onto the exterior and the separate cabin map.</summary>
    private void SyncRotorAudio(Entity<MoonlightRotorcraftComponent> vehicle, bool restart = false)
    {
        if (!_soundQuery.TryComp(vehicle, out var sounds) || sounds.LifeStage > ComponentLifeStage.Running)
            return;

        if (restart)
            StopRotorAudio(sounds);

        if (vehicle.Comp.Stage == MoonlightRotorcraftStage.Stopped)
        {
            StopRotorAudio(sounds);
            return;
        }

        var sound = vehicle.Comp.Stage switch
        {
            MoonlightRotorcraftStage.Starting => sounds.RotorStartSound,
            MoonlightRotorcraftStage.Stopping => sounds.RotorStopSound,
            _ => sounds.RotorSound,
        };
        var looping = vehicle.Comp.Stage is not (MoonlightRotorcraftStage.Starting or MoonlightRotorcraftStage.Stopping);

        sounds.ExteriorRotorAudio = EnsureRotorAudio(
            sounds.ExteriorRotorAudio,
            sound,
            vehicle.Owner,
            sounds.RotorAudioParams.WithLoop(looping));

        if (!TryComp<VehicleInteriorComponent>(vehicle, out var interior))
        {
            sounds.InteriorRotorAudio = _audio.Stop(sounds.InteriorRotorAudio);
            return;
        }

        // Compact cabins can be maps without a grid.
        var cabinEmitter = interior.Grid != EntityUid.Invalid ? interior.Grid : interior.Map;
        if (TerminatingOrDeleted(cabinEmitter))
        {
            sounds.InteriorRotorAudio = _audio.Stop(sounds.InteriorRotorAudio);
            return;
        }

        sounds.InteriorRotorAudio = EnsureRotorAudio(
            sounds.InteriorRotorAudio,
            sound,
            cabinEmitter,
            sounds.InteriorRotorAudioParams.WithLoop(looping));
    }

    private EntityUid? EnsureRotorAudio(
        EntityUid? current,
        SoundSpecifier? sound,
        EntityUid emitter,
        AudioParams parameters)
    {
        if (current is { } currentUid && !TerminatingOrDeleted(currentUid))
            return currentUid;

        return _audio.PlayPvs(sound, emitter, parameters)?.Entity;
    }

    private void StopRotorAudio(MoonlightRotorcraftSoundComponent sounds)
    {
        sounds.ExteriorRotorAudio = _audio.Stop(sounds.ExteriorRotorAudio);
        sounds.InteriorRotorAudio = _audio.Stop(sounds.InteriorRotorAudio);
    }
}
