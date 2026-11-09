using System.Numerics;
using Content.Shared._Classic.Projectiles;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Classic.Projectiles;

/// <summary>
/// Handles smooth client-side visual scaling, stream growth, alpha fade-out,
/// and fire.rsi animation stage progression for flame projectiles,
/// as well as dynamic visual state and color tinting for ground tile fires.
/// </summary>
public sealed class FlameProjectileVisualizerSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlameTileFireComponent, ComponentStartup>(OnTileFireStartup);
        SubscribeLocalEvent<FlameTileFireComponent, AfterAutoHandleStateEvent>(OnTileFireState);
    }

    private void OnTileFireStartup(Entity<FlameTileFireComponent> ent, ref ComponentStartup args)
    {
        UpdateTileFireVisuals(ent);
    }

    private void OnTileFireState(Entity<FlameTileFireComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateTileFireVisuals(ent);
    }

    private void UpdateTileFireVisuals(Entity<FlameTileFireComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!_proto.TryIndex<ReagentFlameEffectPrototype>(ent.Comp.Effect, out var effect))
            return;

        if (_sprite.LayerExists((ent.Owner, sprite), 0))
        {
            _sprite.LayerSetRsiState((ent.Owner, sprite), 0, $"{effect.StatePrefix}_3");
        }
        _sprite.SetColor((ent.Owner, sprite), effect.FlameColor);

        if (TryComp<PointLightComponent>(ent, out var light) && light.Color != effect.FlameColor)
            _pointLight.SetColor(ent.Owner, effect.FlameColor, light);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<FlameProjectileComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var flame, out var sprite))
        {
            if (flame.SpawnTime == TimeSpan.Zero)
                flame.SpawnTime = curTime;

            var age = (float)(curTime - flame.SpawnTime).TotalSeconds;
            var progress = Math.Clamp(age / Math.Max(0.01f, flame.Lifetime), 0f, 1f);

            if (!_proto.TryIndex<ReagentFlameEffectPrototype>(flame.Effect, out var effect))
                continue;

            var scale = MathHelper.Lerp(effect.MinScale, effect.MaxScale, progress);
            _sprite.SetScale((uid, sprite), new Vector2(scale, scale));

            var alpha = progress > 0.8f ? MathHelper.Lerp(1f, 0f, (progress - 0.8f) / 0.2f) : 1f;
            _sprite.SetColor((uid, sprite), effect.FlameColor.WithAlpha(alpha));

            if (TryComp<PointLightComponent>(uid, out var light) && light.Color != effect.FlameColor)
                _pointLight.SetColor(uid, effect.FlameColor, light);
        }
    }
}
