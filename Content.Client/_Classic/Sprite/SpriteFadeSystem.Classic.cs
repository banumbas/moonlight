#pragma warning disable IDE0130 // Namespace does not match folder structure
using Content.Client.Gameplay;
using Content.Client.IconSmoothing;
using Content.Shared._Classic.Sprite;
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

    private const float ClassicFadeRadius = 2f;

    private readonly HashSet<Entity<SpriteFadeComponent>> _classicNearby = [];
    private readonly HashSet<ClassicFadingSpriteComponent> _classicFading = [];

    private EntityQuery<ClassicFadingSpriteComponent> _classicFadingQuery;
    private EntityQuery<ClassicPerspectiveDepthComponent> _perspectiveDepthQuery;

    private void InitializeClassic()
    {
        _classicFadingQuery = GetEntityQuery<ClassicFadingSpriteComponent>();
        _perspectiveDepthQuery = GetEntityQuery<ClassicPerspectiveDepthComponent>();

        SubscribeLocalEvent<ClassicFadingSpriteComponent, ComponentShutdown>(OnClassicFadingShutdown);
    }

    private void OnClassicFadingShutdown(
        Entity<ClassicFadingSpriteComponent> entity,
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
    private void UpdateClassicFade(float change)
    {
        _classicFading.Clear();
        _classicNearby.Clear();

        var player = _playerManager.LocalEntity;
        if (player == null ||
            !TryComp(player, out TransformComponent? playerXform) ||
            !_spriteQuery.TryComp(player, out var playerSprite))
        {
            FadeOutClassic(change);
            return;
        }

        FadeNearbyClassic(player.Value, playerXform, playerSprite, change);
        FadeOccludingClassic(player.Value, playerSprite, change);
        FadeOutClassic(change);
    }

    private void FadeNearbyClassic(
        EntityUid player,
        TransformComponent playerXform,
        SpriteComponent playerSprite,
        float change)
    {
        var playerCoordinates = _transform.GetMapCoordinates(player, xform: playerXform);

        _lookup.GetEntitiesInRange(
            playerCoordinates,
            ClassicFadeRadius,
            _classicNearby,
            LookupFlags.StaticSundries);

        foreach (var entity in _classicNearby)
        {
            if (!_spriteQuery.TryComp(entity, out var sprite) ||
                GetHighestDrawDepth(entity, sprite) < playerSprite.DrawDepth)
            {
                continue;
            }

            if (IsClassicTopFade(entity, entity.Comp))
            {
                TryFadeClassic(entity, sprite, change);
                continue;
            }

            if (entity.Comp.FadeTopOnly && CanUseRegularFade(entity, entity.Comp))
                TryFadeRegular(entity, sprite, change);
        }
    }

    private void FadeOccludingClassic(EntityUid player, SpriteComponent playerSprite, float change)
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
                    !IsClassicTopFade(entity, fade) ||
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

                TryFadeClassic((entity, fade), sprite, change);
            }
        }
    }

    private void TryFadeClassic(
        Entity<SpriteFadeComponent> entity,
        SpriteComponent sprite,
        float change)
    {
        if (!TryComp(entity, out IconSmoothComponent? smooth) ||
            _iconSmooth.HasClassicNorthNeighbour((entity.Owner, smooth)) ||
            !_iconSmooth.TryGetClassicUpperLayers(
                (entity.Owner, sprite),
                out var northEast,
                out var northWest))
        {
            return;
        }

        if (!_classicFadingQuery.TryComp(entity, out var fading))
            fading = AddComp<ClassicFadingSpriteComponent>(entity);

        if (!_classicFading.Add(fading))
            return;

        fading.Alpha = Math.Max(fading.Alpha - change, TargetAlpha);
        ApplyClassicAlpha((entity.Owner, sprite), fading, northEast, northWest);
    }

    private void ApplyClassicAlpha(
        Entity<SpriteComponent> sprite,
        ClassicFadingSpriteComponent fading,
        int northEast,
        int northWest)
    {
        ApplyClassicLayerAlpha(sprite, fading, northEast);
        ApplyClassicLayerAlpha(sprite, fading, northWest);
    }

    private void ApplyClassicLayerAlpha(
        Entity<SpriteComponent> sprite,
        ClassicFadingSpriteComponent fading,
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
    private bool IsClassicTopFade(EntityUid entity, SpriteFadeComponent component)
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
            ? Math.Max(sprite.DrawDepth, ClassicPerspectiveDepthComponent.OverlayDrawDepth)
            : sprite.DrawDepth;
    }

    private void FadeOutClassic(float change)
    {
        var query = AllEntityQuery<ClassicFadingSpriteComponent>();
        while (query.MoveNext(out var entity, out var fading))
        {
            if (!_spriteQuery.TryComp(entity, out var sprite) ||
                !_iconSmooth.TryGetClassicUpperLayers(
                    (entity, sprite),
                    out var northEast,
                    out var northWest))
            {
                RemCompDeferred<ClassicFadingSpriteComponent>(entity);
                continue;
            }

            if (!_classicFading.Contains(fading) && fading.Alpha < 1f)
            {
                fading.Alpha = Math.Min(fading.Alpha + change, 1f);
                ApplyClassicAlpha((entity, sprite), fading, northEast, northWest);
            }

            if (fading.Alpha.Equals(1f))
                RemCompDeferred<ClassicFadingSpriteComponent>(entity);
        }
    }
}
