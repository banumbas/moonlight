using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using ContentDrawDepth = Content.Shared.DrawDepth.DrawDepth;
using DrawDepthTag = Robust.Shared.GameObjects.DrawDepth;

namespace Content.Shared._Classic.Sprite;

/// <summary>
/// Marks a one-tile structure whose taller sprite has a perspective overhang.
/// </summary>
/// <remarks>
/// The normal sprite is drawn at <see cref="BaseDrawDepth"/>. The client repeats only the upper strip at
/// <see cref="OverlayDrawDepth"/>, allowing objects north of the structure to pass underneath the overhang while
/// objects south of it remain in front of the structure body.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ClassicPerspectiveDepthComponent : Component
{
    /// <summary>
    /// Draw depth used for the complete structure sprite below the perspective overhang.
    /// </summary>
    [DataField(customTypeSerializer: typeof(ConstantSerializer<DrawDepthTag>)), AutoNetworkedField]
    public int BaseDrawDepth = (int) ContentDrawDepth.Walls;

    /// <summary>
    /// Height of the perspective overhang in source pixels.
    /// </summary>
    public const int OverlayHeight = 22;

    /// <summary>
    /// Draw depth of the perspective overhang.
    /// </summary>
    public const int OverlayDrawDepth = (int) ContentDrawDepth.Overdoors;
}
