#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Shared.Sprite;

public sealed partial class SpriteFadeComponent
{
    /// <summary>
    /// Whether only the upper half of the sprite should fade.
    /// </summary>
    [DataField]
    public bool FadeTopOnly;
}
