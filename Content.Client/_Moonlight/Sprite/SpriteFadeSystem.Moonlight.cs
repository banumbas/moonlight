#pragma warning disable IDE0130 // Namespace does not match folder structure
using Content.Client.Gameplay;
using Content.Client.IconSmoothing;
using Content.Shared._Moonlight.Sprite;
using Content.Shared.IconSmoothing;
using Content.Shared.Sprite;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Client.Sprite;

public sealed partial class SpriteFadeSystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IconSmoothSystem _iconSmooth = default!;

    private const float MoonlightFadeRadius = 2f;

    private readonly HashSet<Entity<SpriteFadeComponent>> _moonlightNearby = [];
    private readonly HashSet<MoonlightFadingSpriteComponent> _moonlightFading = [];

    private EntityQuery<MoonlightFadingSpriteComponent> _moonlightFadingQuery;
    private EntityQuery<MoonlightPerspectiveDepthComponent> _perspectiveDepthQuery;

    private void InitializeMoonlight()
    {
        _moonlightFadingQuery = GetEntityQuery<MoonlightFadingSpriteComponent>();
        _perspectiveDepthQuery = GetEntityQuery<MoonlightPerspectiveDepthComponent>();

        SubscribeLocalEvent<MoonlightFadingSpriteComponent, ComponentShutdown>(OnMoonlightFadingShutdown);
    }

    private void OnMoonlightFadingShutdown(
        Entity<MoonlightFadingSpriteComponent> entity,
        ref ComponentShutdown args)
    {
        if (MetaData(entity).EntityLifeStage >= EntityLifeStage.Terminating ||
            !_spriteQuery.TryComp(entity, out var sprite))
        {
            return;
        }

        foreach (var (layerIndex, originalAlpha) in entity.Comp.OriginalLayerAlphas)
        {
            if (!_sprite.TryGetLayer((entity.Owner, sprite), layerIndex, out var layer, false))
                continue;

            _sprite.LayerSetColor(
                (entity.Owner, sprite),
                layerIndex,
                layer.Color.WithAlpha(originalAlpha));
        }
    }

    /// <summary>
    /// Fades the upper IconSmooth corner layers near the local player or when
    /// they obscure another clickable entity at the player/cursor position.
    /// </summary>
    private void UpdateMoonlightFade(float change)
    {
        _moonlightFading.Clear();
        _moonlightNearby.Clear();

        var player = _playerManager.LocalEntity;
        if (player == null ||
            !TryComp(player, out TransformComponent? playerXform) ||
            !_spriteQuery.TryComp(player, out var playerSprite))
        {
            FadeOutMoonlight(change);
            return;
        }

        FadeNearbyMoonlight(player.Value, playerXform, playerSprite, change);
        FadeOccludingMoonlight(player.Value, playerSprite, change);
        FadeOutMoonlight(change);
    }

    private void FadeNearbyMoonlight(
        EntityUid player,
        TransformComponent playerXform,
        SpriteComponent playerSprite,
        float change)
    {
        var playerCoordinates = _transform.GetMapCoordinates(player, xform: playerXform);

        _lookup.GetEntitiesInRange(
            playerCoordinates,
            MoonlightFadeRadius,
            _moonlightNearby,
            LookupFlags.StaticSundries);

        foreach (var entity in _moonlightNearby)
        {
            if (!_spriteQuery.TryComp(entity, out var sprite) ||
                GetHighestDrawDepth(entity, sprite) < playerSprite.DrawDepth)
            {
                continue;
            }

            if (IsMoonlightTopFade(entity, entity.Comp))
            {
                TryFadeMoonlight(entity, sprite, change);
                continue;
            }

            if (entity.Comp.FadeTopOnly && CanUseRegularFade(entity, entity.Comp))
                TryFadeRegular(entity, sprite, change);
        }
    }

    private void FadeOccludingMoonlight(EntityUid player, SpriteComponent playerSprite, float change)
    {
        if (_stateManager.CurrentState is not GameplayState state)
            return;

        foreach (var (mapPosition, excludeBoundingBox) in _points)
        {
            using var clickable = state
                .GetClickableEntities(mapPosition, excludeFaded: false)
                .GetEnumerator();

            if (!clickable.MoveNext())
                continue;

            var nextEntity = clickable.Current;
            while (clickable.MoveNext())
            {
                var entity = nextEntity;
                nextEntity = clickable.Current;

                if (entity == player ||
                    !_fadeQuery.TryComp(entity, out var fade) ||
                    !IsMoonlightTopFade(entity, fade) ||
                    !_spriteQuery.TryComp(entity, out var sprite) ||
                    GetHighestDrawDepth(entity, sprite) < playerSprite.DrawDepth)
                {
                    continue;
                }

                if (excludeBoundingBox &&
                    _fixturesQuery.TryComp(entity, out var fixtures) &&
                    PointIntersectsHardFixture(entity, fixtures, mapPosition))
                {
                    continue;
                }

                TryFadeMoonlight((entity, fade), sprite, change);
            }
        }
    }

    private void TryFadeMoonlight(
        Entity<SpriteFadeComponent> entity,
        SpriteComponent sprite,
        float change)
    {
        if (!TryComp(entity, out IconSmoothComponent? smooth) ||
            _iconSmooth.HasMoonlightNorthNeighbour((entity.Owner, smooth)) ||
            !_iconSmooth.TryGetMoonlightUpperLayers(
                (entity.Owner, sprite),
                out var northEast,
                out var northWest))
        {
            return;
        }

        if (!_moonlightFadingQuery.TryComp(entity, out var fading))
            fading = AddComp<MoonlightFadingSpriteComponent>(entity);

        if (!_moonlightFading.Add(fading))
            return;

        fading.Alpha = Math.Max(fading.Alpha - change, TargetAlpha);
        ApplyMoonlightAlpha((entity.Owner, sprite), fading, northEast, northWest);
    }

    private void ApplyMoonlightAlpha(
        Entity<SpriteComponent> sprite,
        MoonlightFadingSpriteComponent fading,
        int northEast,
        int northWest)
    {
        ApplyMoonlightLayerAlpha(sprite, fading, northEast);
        ApplyMoonlightLayerAlpha(sprite, fading, northWest);
    }

    private void ApplyMoonlightLayerAlpha(
        Entity<SpriteComponent> sprite,
        MoonlightFadingSpriteComponent fading,
        int layerIndex)
    {
        if (!_sprite.TryGetLayer(sprite.AsNullable(), layerIndex, out var layer, false))
            return;

        if (!fading.OriginalLayerAlphas.TryGetValue(layerIndex, out var originalAlpha))
        {
            originalAlpha = layer.Color.A;
            fading.OriginalLayerAlphas[layerIndex] = originalAlpha;
        }

        var alpha = originalAlpha * fading.Alpha;
        if (!layer.Color.A.Equals(alpha))
            _sprite.LayerSetColor(sprite.AsNullable(), layerIndex, layer.Color.WithAlpha(alpha));
    }

    private bool PointIntersectsHardFixture(
        EntityUid entity,
        FixturesComponent fixtures,
        MapCoordinates mapPosition)
    {
        var transform = _physics.GetPhysicsTransform(entity);

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && _fixtures.TestPoint(fixture.Shape, transform, mapPosition.Position))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Only IconSmooth entities have distinct upper layers that can be faded
    /// independently. Other entities with FadeTopOnly use the regular whole-
    /// sprite fade path instead of being silently discarded here.
    /// </summary>
    private bool IsMoonlightTopFade(EntityUid entity, SpriteFadeComponent component)
    {
        return component.FadeTopOnly && HasComp<IconSmoothComponent>(entity);
    }

    /// <summary>
    /// Non-smoothed wall mounts cannot fade individual IconSmooth layers.
    /// For them FadeTopOnly means that the regular whole-sprite fade is only
    /// active while the entity faces north, where it overlaps the wall top.
    /// </summary>
    private bool CanUseRegularFade(EntityUid entity, SpriteFadeComponent component)
    {
        if (!component.FadeTopOnly)
            return true;

        return !HasComp<IconSmoothComponent>(entity) &&
               _transform.GetWorldRotation(entity).GetCardinalDir() == Direction.North;
    }

    private int GetHighestDrawDepth(EntityUid entity, SpriteComponent sprite)
    {
        return _perspectiveDepthQuery.HasComp(entity)
            ? Math.Max(sprite.DrawDepth, MoonlightPerspectiveDepthComponent.OverlayDrawDepth)
            : sprite.DrawDepth;
    }

    private void FadeOutMoonlight(float change)
    {
        var query = AllEntityQuery<MoonlightFadingSpriteComponent>();
        while (query.MoveNext(out var entity, out var fading))
        {
            if (!_spriteQuery.TryComp(entity, out var sprite) ||
                !_iconSmooth.TryGetMoonlightUpperLayers(
                    (entity, sprite),
                    out var northEast,
                    out var northWest))
            {
                RemCompDeferred<MoonlightFadingSpriteComponent>(entity);
                continue;
            }

            if (!_moonlightFading.Contains(fading) && fading.Alpha < 1f)
            {
                fading.Alpha = Math.Min(fading.Alpha + change, 1f);
                ApplyMoonlightAlpha((entity, sprite), fading, northEast, northWest);
            }

            if (fading.Alpha.Equals(1f))
                RemCompDeferred<MoonlightFadingSpriteComponent>(entity);
        }
    }
}
