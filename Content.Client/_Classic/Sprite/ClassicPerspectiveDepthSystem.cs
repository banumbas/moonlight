using System.Numerics;
using Content.Client.IconSmoothing;
using Content.Shared._Classic.Sprite;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Maths;

namespace Content.Client._Classic.Sprite;

/// <summary>
/// Registers the high-depth pass for the upper strip of Classic perspective structures.
/// </summary>
public sealed partial class ClassicPerspectiveDepthSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new ClassicPerspectiveDepthOverlay());

        SubscribeLocalEvent<ClassicPerspectiveDepthComponent, ComponentStartup>(OnPerspectiveStartup);
        SubscribeLocalEvent<ClassicPerspectiveDepthComponent, AfterAutoHandleStateEvent>(OnPerspectiveState);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<ClassicPerspectiveDepthOverlay>();
    }

    private void OnPerspectiveStartup(
        Entity<ClassicPerspectiveDepthComponent> entity,
        ref ComponentStartup args)
    {
        ApplyBaseDrawDepth(entity);
    }

    private void OnPerspectiveState(
        Entity<ClassicPerspectiveDepthComponent> entity,
        ref AfterAutoHandleStateEvent args)
    {
        ApplyBaseDrawDepth(entity);
    }

    private void ApplyBaseDrawDepth(Entity<ClassicPerspectiveDepthComponent> entity)
    {
        _sprite.SetDrawDepth(entity.Owner, entity.Comp.BaseDrawDepth);
    }
}

/// <summary>
/// Draws only the source-image rows which extend above a structure's one-tile footprint.
/// </summary>
internal sealed partial class ClassicPerspectiveDepthOverlay : Overlay
{
    [Dependency] private IEntityManager _entity = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly IconSmoothSystem _iconSmooth;
    private readonly SharedTransformSystem _transform;
    private readonly EntityQuery<SpriteComponent> _spriteQuery;
    private readonly EntityQuery<IconSmoothComponent> _smoothQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;
    private readonly HashSet<Entity<ClassicPerspectiveDepthComponent>> _intersecting = [];
    private readonly List<RenderEntry> _renderEntries = [];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public ClassicPerspectiveDepthOverlay()
    {
        IoCManager.InjectDependencies(this);

        _lookup = _entity.System<EntityLookupSystem>();
        _iconSmooth = _entity.System<IconSmoothSystem>();
        _transform = _entity.System<SharedTransformSystem>();
        _spriteQuery = _entity.GetEntityQuery<SpriteComponent>();
        _smoothQuery = _entity.GetEntityQuery<IconSmoothComponent>();
        _xformQuery = _entity.GetEntityQuery<TransformComponent>();
        ZIndex = ClassicPerspectiveDepthComponent.OverlayDrawDepth;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        _intersecting.Clear();
        _renderEntries.Clear();

        // The lookup tree tracks the one-tile fixture, while the visual reaches half a tile farther north.
        _lookup.GetEntitiesIntersecting(
            args.MapId,
            args.WorldAABB.Enlarged(0.5f),
            _intersecting,
            LookupFlags.StaticSundries);

        foreach (var entity in _intersecting)
        {
            if (!_spriteQuery.TryComp(entity, out var sprite) ||
                !_xformQuery.TryComp(entity, out var xform) ||
                !sprite.Visible ||
                !sprite.AddToTree)
            {
                continue;
            }

            var (worldPosition, worldRotation) = _transform.GetWorldPositionRotation(entity);

            // Clyde pixel-snaps the grid tree rather than every child sprite. Repeat that offset so the
            // separately rendered strip never develops a sub-pixel seam against the original wall sprite.
            if (xform.GridUid is { } grid && _xformQuery.TryComp(grid, out var gridXform))
                worldPosition += GetGridPixelSnapOffset(_transform.GetWorldPosition(gridXform), args.Viewport, eye);

            var screenY = args.Viewport.WorldToLocal(worldPosition).Y;
            _renderEntries.Add(new RenderEntry(entity, sprite, worldPosition, worldRotation, screenY));
        }

        _renderEntries.Sort(RenderEntryComparer.Instance);

        foreach (var entry in _renderEntries)
        {
            if (_smoothQuery.TryComp(entry.Uid, out var smooth) &&
                _iconSmooth.HasClassicNorthNeighbour((entry.Uid, smooth)))
            {
                continue;
            }

            var hasUpperLayers = _iconSmooth.TryGetClassicUpperLayers(
                (entry.Uid, entry.Sprite),
                out var upperLayer1,
                out var upperLayer2);

            DrawTopOverlay(
                entry.Sprite,
                args.WorldHandle,
                eye.Rotation,
                entry.WorldRotation,
                entry.WorldPosition,
                hasUpperLayers ? upperLayer1 : -1,
                hasUpperLayers ? upperLayer2 : -1);
        }
    }

    private static Vector2 GetGridPixelSnapOffset(
        Vector2 gridWorldPosition,
        IClydeViewport viewport,
        IEye eye)
    {
        var viewScale = eye.Scale * viewport.RenderScale *
                        new Vector2(EyeManager.PixelsPerMeter, -EyeManager.PixelsPerMeter);
        var relativePosition = eye.Rotation.RotateVec(gridWorldPosition - eye.Position.Position - eye.Offset);
        var screenPosition = relativePosition * viewScale + viewport.Size / 2f;
        var screenOffset = screenPosition.Rounded() - screenPosition;
        return (-eye.Rotation).RotateVec(screenOffset / viewScale);
    }

    private static void DrawTopOverlay(
        SpriteComponent sprite,
        DrawingHandleWorld drawingHandle,
        Angle eyeRotation,
        Angle worldRotation,
        Vector2 worldPosition,
        int upperLayer1,
        int upperLayer2)
    {
        var angle = (worldRotation + eyeRotation).Reduced().FlipPositive();
        var cardinal = Angle.Zero;

        if (!sprite.NoRotation && sprite.SnapCardinals)
            cardinal = angle.RoundToCardinalAngle();

        var entityMatrix = Matrix3Helpers.CreateTransform(
            worldPosition,
            sprite.NoRotation ? -eyeRotation : worldRotation - cardinal);
        var spriteMatrix = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);

        if (!sprite.GranularLayersRendering)
        {
            var layerIndex = 0;
            foreach (var layer in sprite.AllLayers)
            {
                if (layer is SpriteComponent.Layer spriteLayer &&
                    IsOverlayLayer(layerIndex, upperLayer1, upperLayer2))
                    DrawLayerTop(spriteLayer, drawingHandle, spriteMatrix, angle, sprite);

                layerIndex++;
            }

            return;
        }

        entityMatrix = Matrix3Helpers.CreateTransform(worldPosition, worldRotation);
        var transformDefault = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);

        entityMatrix = Matrix3Helpers.CreateTransform(worldPosition, worldRotation - angle.RoundToCardinalAngle());
        var transformSnap = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);

        entityMatrix = Matrix3Helpers.CreateTransform(worldPosition, -eyeRotation);
        var transformNoRotation = Matrix3x2.Multiply(sprite.LocalMatrix, entityMatrix);

        var granularLayerIndex = 0;
        foreach (var layer in sprite.AllLayers)
        {
            if (layer is not SpriteComponent.Layer spriteLayer ||
                !IsOverlayLayer(granularLayerIndex, upperLayer1, upperLayer2))
            {
                granularLayerIndex++;
                continue;
            }

            var transform = spriteLayer.RenderingStrategy switch
            {
                LayerRenderingStrategy.UseSpriteStrategy => spriteMatrix,
                LayerRenderingStrategy.Default => transformDefault,
                LayerRenderingStrategy.NoRotation => transformNoRotation,
                LayerRenderingStrategy.SnapToCardinals => transformSnap,
                _ => spriteMatrix,
            };

            DrawLayerTop(spriteLayer, drawingHandle, transform, angle, sprite);
            granularLayerIndex++;
        }
    }

    private static bool IsOverlayLayer(int layerIndex, int upperLayer1, int upperLayer2)
    {
        return upperLayer1 < 0 || layerIndex == upperLayer1 || layerIndex == upperLayer2;
    }

    private static void DrawLayerTop(
        SpriteComponent.Layer layer,
        DrawingHandleWorld drawingHandle,
        Matrix3x2 spriteMatrix,
        Angle angle,
        SpriteComponent sprite)
    {
        if (!layer.Visible || layer.Blank || layer.CopyToShaderParameters != null)
            return;

        var state = layer.ActualState;
        var matrixDirection = state == null
            ? RsiDirection.South
            : SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);
        var textureDirection = matrixDirection;

        if (state != null && sprite.EnableDirectionOverride)
            textureDirection = sprite.DirectionOverride.Convert(state.RsiDirections);
        textureDirection = textureDirection.OffsetRsiDir(layer.DirOffset);

        Texture texture;
        if (state != null)
            texture = state.GetFrame(textureDirection, layer.AnimationFrame);
        else if (layer.Texture != null)
            texture = layer.Texture;
        else
            return;

        // A 32x32 layer belongs to the physical footprint and must not leak into the high-depth pass.
        if (texture.Height <= EyeManager.PixelsPerMeter)
            return;

        layer.GetLayerDrawMatrix(matrixDirection, out var layerMatrix);
        var transformMatrix = Matrix3x2.Multiply(layerMatrix, spriteMatrix);
        drawingHandle.SetTransform(transformMatrix);

        if (layer.Shader != null)
            drawingHandle.UseShader(layer.Shader);

        var overlayHeight = Math.Min(ClassicPerspectiveDepthComponent.OverlayHeight, texture.Height);
        var textureSize = texture.Size / (float) EyeManager.PixelsPerMeter;
        var quad = Box2.FromDimensions(textureSize / -2, textureSize);
        quad.Bottom = quad.Top - overlayHeight / (float) EyeManager.PixelsPerMeter;

        var source = new UIBox2(0, 0, texture.Width, overlayHeight);
        var layerColor = sprite.Color * layer.Color;
        if (layer.ShaderPrototype == SpriteSystem.UnshadedId)
            layerColor = new Color(new Vector4(-1) - layerColor.RGBA);

        drawingHandle.DrawTextureRectRegion(texture, quad, layerColor, source);

        if (layer.Shader != null)
            drawingHandle.UseShader(null);
    }

    private readonly record struct RenderEntry(
        EntityUid Uid,
        SpriteComponent Sprite,
        Vector2 WorldPosition,
        Angle WorldRotation,
        float ScreenY);

    private sealed class RenderEntryComparer : IComparer<RenderEntry>
    {
        public static readonly RenderEntryComparer Instance = new();

        public int Compare(RenderEntry x, RenderEntry y)
        {
            var comparison = x.Sprite.RenderOrder.CompareTo(y.Sprite.RenderOrder);
            if (comparison != 0)
                return comparison;

            comparison = x.ScreenY.CompareTo(y.ScreenY);
            return comparison != 0 ? comparison : x.Uid.CompareTo(y.Uid);
        }
    }
}
