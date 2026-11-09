#pragma warning disable IDE0130 // Namespace does not match folder structure
using Content.Client.Clickable;

namespace Content.Client.Sprite;

/// <summary>
/// Tracks the upper IconSmooth layers used by Classic wall fading.
/// </summary>
[RegisterComponent, Access(typeof(SpriteFadeSystem), typeof(ClickableSystem))]
public sealed partial class ClassicFadingSpriteComponent : Component
{
    /// <summary>
    /// Current alpha multiplier applied to the upper wall layers.
    /// </summary>
    public float Alpha = 1f;

    /// <summary>
    /// Original alpha values keyed by sprite layer index.
    /// </summary>
    public readonly Dictionary<int, float> OriginalLayerAlphas = [];
}
