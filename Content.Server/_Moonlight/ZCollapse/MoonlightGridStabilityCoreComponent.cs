namespace Content.Server._Moonlight.ZCollapse;

/// <summary>
/// Keeps a floating grid aloft: when anchored, seeds the tile it stands on with
/// <see cref="LevitationForce"/> stability, which then flood-fills outward across the grid.
/// </summary>
[RegisterComponent]
public sealed partial class MoonlightGridStabilityCoreComponent : Component
{
    [DataField]
    public int LevitationForce = 20;
}


