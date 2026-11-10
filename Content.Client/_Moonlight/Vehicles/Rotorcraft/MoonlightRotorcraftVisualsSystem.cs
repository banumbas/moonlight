using System;
using System.Numerics;
using Content.Client._Moonlight.ZLevels.Core;
using Content.Shared._Moonlight.Vehicles.Rotorcraft;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Moonlight.Vehicles.Rotorcraft;

/// <summary>
/// Rotates the stationary blades during spin-up and shutdown and blends the running RSI
/// according to rotor speed. No per-frame angles are sent over the network.
/// </summary>
public sealed partial class MoonlightRotorcraftVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityQuery<MoonlightZPhysicsComponent> _zPhysicsQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
        UpdatesAfter.Add(typeof(MoonlightClientZLevelsPostAnimSystem));

        SubscribeLocalEvent<MoonlightRotorcraftVisualsComponent, ComponentShutdown>(OnVisualsShutdown);
        SubscribeLocalEvent<SpriteComponent, ComponentShutdown>(OnSpriteShutdown);
    }

    public override void Shutdown()
    {
        ClearAttitudes();
        base.Shutdown();
    }

    private void OnVisualsShutdown(Entity<MoonlightRotorcraftVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            ClearAttitude((ent.Owner, ent.Comp, sprite));
            SetRotor((ent.Owner, ent.Comp, sprite), Angle.Zero, 0f);
        }
    }

    /// <summary>Clears rotor and attitude output when the shared controller shuts down.</summary>
    internal void ResetRotorcraft(Entity<MoonlightRotorcraftComponent> ent)
    {
        if (!TryComp<MoonlightRotorcraftVisualsComponent>(ent, out var visuals) ||
            !TryComp<SpriteComponent>(ent, out var sprite))
        {
            return;
        }

        ClearAttitude((ent.Owner, visuals, sprite));
        visuals.Bank = 0f;
        visuals.HoverBlend = 0f;
        visuals.LastStage = null;
        SetRotor((ent.Owner, visuals, sprite), Angle.Zero, 0f);
    }

    private void OnSpriteShutdown(Entity<SpriteComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<MoonlightRotorcraftVisualsComponent>(ent, out var visuals))
        {
            ClearAttitude((ent.Owner, visuals, ent.Comp));
            visuals.LastStage = null;
        }
    }

    /// <summary>Removes only our last output before animations and the Z passes run again.</summary>
    internal void ClearAttitudes()
    {
        var query = EntityQueryEnumerator<MoonlightRotorcraftVisualsComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var sprite))
            ClearAttitude((uid, visuals, sprite));
    }

    private void ClearAttitude(Entity<MoonlightRotorcraftVisualsComponent, SpriteComponent> ent)
    {
        var (uid, visuals, sprite) = ent;
        if (!visuals.AttitudeApplied)
            return;

        visuals.AttitudeApplied = false;
        // Another system may have replaced a property since our pass. Its new value is
        // the next base; restoring a captured value in that case would erase its change.
        if (sprite.Rotation == visuals.AppliedRotation && sprite.Rotation != visuals.BaseRotation)
            _sprite.SetRotation((uid, sprite), visuals.BaseRotation);
        if (sprite.Scale == visuals.AppliedScale && sprite.Scale != visuals.BaseScale)
            _sprite.SetScale((uid, sprite), visuals.BaseScale);
        if (sprite.Offset == visuals.AppliedOffset && sprite.Offset != visuals.BaseOffset)
            _sprite.SetOffset((uid, sprite), visuals.BaseOffset);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<MoonlightRotorcraftVisualsComponent, MoonlightRotorcraftComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var flight, out var sprite))
        {
            UpdateRotor((uid, visuals, flight, sprite));
            UpdateAttitude((uid, visuals, flight, sprite), frameTime);
        }
    }

    private void UpdateRotor(Entity<MoonlightRotorcraftVisualsComponent, MoonlightRotorcraftComponent, SpriteComponent> ent)
    {
        var (uid, visuals, flight, sprite) = ent;
        var stage = flight.Stage;
        var starting = stage == MoonlightRotorcraftStage.Starting;
        var transitioning = starting || stage == MoonlightRotorcraftStage.Stopping;
        if (!transitioning && visuals.LastStage == stage)
            return;

        var blurAlpha = stage is MoonlightRotorcraftStage.Flying or MoonlightRotorcraftStage.Descending or
            MoonlightRotorcraftStage.Landed ? 1f : 0f;
        var rotation = Angle.Zero;

        if (transitioning)
        {
            var duration = (float) (flight.RotorTransitionEnd - flight.RotorTransitionStart).TotalSeconds;
            var elapsed = (float) (_timing.CurTime - flight.RotorTransitionStart).TotalSeconds;
            var progress = duration > 0f ? Math.Clamp(elapsed / duration, 0f, 1f) : 1f;
            var ramp = starting ? progress : 1f - progress;

            // Integrate the speed curve instead of advancing a handful of RSI frames. The
            // reverse integral ends at zero, so the stopped blades retain their original pose.
            var turns = 0.5f * ramp * ramp;
            rotation = Angle.FromDegrees((starting ? 1f : -1f) * visuals.MaxRotationSpeed * duration * turns);
            var threshold = Math.Clamp(visuals.BlurStartSpeed, 0f, 1f);
            blurAlpha = threshold < 1f
                ? SmoothStep(Math.Clamp((ramp - threshold) / (1f - threshold), 0f, 1f))
                : ramp >= 1f ? 1f : 0f;
        }

        if (SetRotor((uid, visuals, sprite), rotation, blurAlpha))
            visuals.LastStage = stage;
    }

    private bool SetRotor(Entity<MoonlightRotorcraftVisualsComponent, SpriteComponent> ent, Angle rotation, float blurAlpha)
    {
        var (uid, visuals, sprite) = ent;
        if (!_sprite.LayerMapTryGet((uid, sprite), MoonlightRotorcraftVisualLayers.Rotor, out var blades, false) ||
            !_sprite.LayerMapTryGet((uid, sprite), MoonlightRotorcraftVisualLayers.RotorBlur, out var blur, false))
        {
            return false;
        }

        // Sprite layers rotate about their frame centre. Compensate the offset so this large
        // asymmetric RSI rotates about its hub rather than orbiting around the aircraft.
        _sprite.LayerSetRotation((uid, sprite), blades, rotation);
        _sprite.LayerSetOffset((uid, sprite), blades, visuals.RotorPivot - rotation.RotateVec(visuals.RotorPivot));
        _sprite.LayerSetColor((uid, sprite), blades, Color.White.WithAlpha(1f - blurAlpha));
        _sprite.LayerSetVisible((uid, sprite), blades, blurAlpha < 1f);
        _sprite.LayerSetColor((uid, sprite), blur, Color.White.WithAlpha(blurAlpha));
        _sprite.LayerSetVisible((uid, sprite), blur, blurAlpha > 0f);
        _sprite.LayerSetAutoAnimated((uid, sprite), blur, blurAlpha > 0f);
        return true;
    }

    private static float SmoothStep(float value) => value * value * (3f - 2f * value);

    /// <summary>Adds hover sway and cyclic bank to the current animation and Z-level output.</summary>
    private void UpdateAttitude(
        Entity<MoonlightRotorcraftVisualsComponent, MoonlightRotorcraftComponent, SpriteComponent> ent,
        float frameTime)
    {
        var (uid, visuals, flight, sprite) = ent;
        if (!_zPhysicsQuery.HasComp(uid))
            return;

        var airborne = flight.Stage is MoonlightRotorcraftStage.Flying or MoonlightRotorcraftStage.Descending;
        var response = flight.CyclicResponse > 0f ? 1f - MathF.Exp(-frameTime / flight.CyclicResponse) : 1f;
        visuals.HoverBlend += ((airborne ? 1f : 0f) - visuals.HoverBlend) * response;
        var localCyclic = (-_transform.GetWorldRotation(uid)).RotateVec(flight.Cyclic);
        var targetBank = airborne ? localCyclic.X * visuals.MaxBankAngle : 0f;
        visuals.Bank += (targetBank - visuals.Bank) * response;

        var phase = (float) _timing.CurTime.TotalSeconds * visuals.SwayFrequency * MathF.Tau;
        var sway = MathF.Sin(phase) * visuals.HoverBlend;
        var roll = visuals.Bank + sway * visuals.HoverRollAngle;
        visuals.BaseRotation = sprite.Rotation;
        visuals.BaseScale = sprite.Scale;
        visuals.BaseOffset = sprite.Offset;
        visuals.AppliedRotation = sprite.Rotation + Angle.FromDegrees(roll);
        visuals.AppliedScale = sprite.Scale * new Vector2(MathF.Cos(MathHelper.DegreesToRadians(visuals.Bank)), 1f);
        visuals.AppliedOffset = sprite.Offset + new Vector2(sway * visuals.SwayAmplitude, 0f);
        visuals.AttitudeApplied = true;

        if (sprite.Rotation != visuals.AppliedRotation)
            _sprite.SetRotation((uid, sprite), visuals.AppliedRotation);
        if (sprite.Scale != visuals.AppliedScale)
            _sprite.SetScale((uid, sprite), visuals.AppliedScale);
        if (sprite.Offset != visuals.AppliedOffset)
            _sprite.SetOffset((uid, sprite), visuals.AppliedOffset);
    }
}

/// <summary>Clears the previous attitude before animation and Z-level transforms are composed.</summary>
internal sealed partial class MoonlightRotorcraftVisualsPreAnimSystem : EntitySystem
{
    [Dependency] private MoonlightRotorcraftVisualsSystem _visuals = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(MoonlightClientZLevelsPreAnimSystem));
        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        _visuals.ClearAttitudes();
    }
}
