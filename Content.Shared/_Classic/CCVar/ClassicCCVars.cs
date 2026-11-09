using Robust.Shared.Configuration;

namespace Content.Shared._Classic.CCVar;

[CVarDefs]
public sealed partial class ClassicCCVars
{
    /// <summary>
    /// Master switch for atmospherics. When disabled, atmos simulation, pressure effects,
    /// atmos devices, and ambient temperature exchange are skipped.
    /// </summary>
    public static readonly CVarDef<bool> AtmosEnabled =
        CVarDef.Create("atmos.enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// When true, shuttle game rules, event shuttles, unknown shuttles, evac pods, and shuttle map loads are disabled.
    /// </summary>
    public static readonly CVarDef<bool> DisableShuttleEvents =
        CVarDef.Create("classic.shuttle_events.disable", true, CVar.SERVERONLY | CVar.ARCHIVE);
}
