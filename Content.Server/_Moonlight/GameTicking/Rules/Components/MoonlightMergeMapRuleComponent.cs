using Content.Server._Moonlight.GameTicking.Rules;
using Content.Shared._Moonlight.ZLevels.Mapping.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Moonlight.GameTicking.Rules.Components;

/// <summary>
/// Merges a map file into an existing map in a Moonlight station Z-network when the game rule is added.
/// The loaded grids are reported through <see cref="Content.Server.GameTicking.Rules.RuleLoadedGridsEvent"/>.
/// </summary>
[RegisterComponent, Access(typeof(MoonlightMergeMapRuleSystem))]
public sealed partial class MoonlightMergeMapRuleComponent : Component
{
    /// <summary>
    /// Map file whose contents will be merged into the target Z-level.
    /// </summary>
    [DataField]
    public ResPath? MapPath;

    /// <summary>Alternative to MapPath: all maps in this bundle are placed at consecutive depths.</summary>
    [DataField]
    public ProtoId<MoonlightZLevelMapPrototype>? ZMap;

    /// <summary>
    /// Depth of the existing Moonlight Z-level that receives the map contents.
    /// </summary>
    [DataField(required: true)]
    public int TargetDepth;

    /// <summary>
    /// When set, generates permanent biome terrain on this Z-depth underneath the merged grids.
    /// </summary>
    [DataField]
    public int? BiomeGroundDepth;

    /// <summary>Choose one tile-aligned position for the complete structure instead of its saved coordinates.</summary>
    [DataField]
    public bool RandomPlacement;

    /// <summary>Minimum gap between the station and the structure's bounding radii, in metres.</summary>
    [DataField]
    public float MinDistance = 128f;

    /// <summary>Maximum gap between the station and the structure's bounding radii, in metres.</summary>
    [DataField]
    public float MaxDistance = 512f;

    /// <summary>Clearance around existing structures when choosing a position.</summary>
    [DataField]
    public float PlacementPadding = 8f;

    /// <summary>Maximum number of candidate positions; failure leaves the source maps unloaded.</summary>
    [DataField]
    public int PlacementAttempts = 32;
}
