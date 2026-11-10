/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<float>
        MoonlightBaseFallingDamage = CVarDef.Create("zlevels.ce_base_falling_damage", 0.75f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        MoonlightBaseFallingOtherDamage = CVarDef.Create("zlevels.ce_base_falling_other_damage", 0.4f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        MoonlightBaseFallingStunTime = CVarDef.Create("zlevels.ce_base_falling_stun_time", 0.1f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        MoonlightBaseFallingOtherStunTime = CVarDef.Create("zlevels.ce_base_falling_other_stun_time", 0.06f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> ZLevelsPhysicsTickRate =
        CVarDef.Create("zlevels.ce_physics.tick_rate", 60, CVar.ARCHIVE);

    public static readonly CVarDef<bool> ZLevelsPhysicsClientSimulation =
        CVarDef.Create("zlevels.ce_physics.client_simulation", true, CVar.ARCHIVE | CVar.CLIENT);

    /**
     * Physics
     */

    public static readonly CVarDef<float>
        MoonlightZLevelsPhysicsGravityForce = CVarDef.Create("ce.zlevels.physics.gravity_force", 9.8f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        MoonlightZLevelsPhysicsVelocityLimit = CVarDef.Create("ce.zlevels.physics.velocity_limit", 20f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// The minimum speed required to trigger LandEvent events.
    /// </summary>
    public static readonly CVarDef<float>
        MoonlightZLevelsPhysicsImpactVelocity = CVarDef.Create("ce.zlevels.physics.impact_velocity", 3f, CVar.SERVER | CVar.REPLICATED);

    /**
     * Rendering
     */

    public static readonly CVarDef<int>
        MoonlightZLevelsRenderingMaxZLevelsBelowRendering = CVarDef.Create("ce.zlevels.rendering.max_zLevels_below_rendering", 1, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Client render distance below the player, additionally limited by the server.</summary>
    public static readonly CVarDef<int>
        MoonlightZLevelsRenderingLevelsBelow = CVarDef.Create("ce.zlevels.rendering.levels_below", MoonlightSharedZLevelsSystem.MaxZLevelsBelowRendering, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Client render distance above the player while looking up.</summary>
    public static readonly CVarDef<int>
        MoonlightZLevelsRenderingLevelsAbove = CVarDef.Create("ce.zlevels.rendering.levels_above", MoonlightSharedZLevelsSystem.MaxZLevelsAboveRendering, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Render lower levels only around visible floor openings, at the main viewport's pixel density.</summary>
    public static readonly CVarDef<bool>
        MoonlightZLevelsRenderingCropLowerLevels = CVarDef.Create("ce.zlevels.rendering.crop_lower_levels", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Apply the engine light-map blur to Z-levels below the player.
    /// Disabled by default because the lower level is already blurred by the Z overlay,
    /// while the light-map blur adds six more full-screen passes per visible level.
    /// </summary>
    public static readonly CVarDef<bool>
        MoonlightZLevelsRenderingLowerLevelLightBlur = CVarDef.Create("ce.zlevels.rendering.lower_level_light_blur", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Apply the content-side blur used for roof and tile-emission lighting to Z-levels below the player.
    /// The final Z-level blur normally provides enough smoothing for the lower layer on its own.
    /// </summary>
    public static readonly CVarDef<bool>
        MoonlightZLevelsRenderingLowerLevelContentLightBlur = CVarDef.Create("ce.zlevels.rendering.lower_level_content_light_blur", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Draw the ambient-occlusion overlay on Z-levels below the player.
    /// This does not control point-light shadows.
    /// </summary>
    public static readonly CVarDef<bool>
        MoonlightZLevelsRenderingLowerLevelAmbientOcclusion = CVarDef.Create("ce.zlevels.rendering.lower_level_ambient_occlusion", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Draw lower-level roof and tile-emission lighting directly into the viewport light target when
    /// the content-side blur is disabled, avoiding the enlarged intermediate target and copy-back pass.
    /// </summary>
    public static readonly CVarDef<bool>
        MoonlightZLevelsRenderingLowerLevelDirectLightTarget = CVarDef.Create("ce.zlevels.rendering.lower_level_direct_light_target", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Use soft point-light shadows on Z-levels below the player.
    /// Hard shadows retain occlusion while using the cheaper light shader.
    /// </summary>
    public static readonly CVarDef<bool>
        MoonlightZLevelsRenderingLowerLevelSoftShadows = CVarDef.Create("ce.zlevels.rendering.lower_level_soft_shadows", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
