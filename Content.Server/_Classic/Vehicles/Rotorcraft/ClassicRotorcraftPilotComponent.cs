namespace Content.Server._Classic.Vehicles.Rotorcraft;

/// <summary>Server-owned actions granted to the current rotorcraft pilot.</summary>
[RegisterComponent, Access(typeof(ClassicRotorcraftSystem))]
public sealed partial class ClassicRotorcraftPilotComponent : Component
{
    /// <summary>Action for starting the rotor and landing.</summary>
    public EntityUid? ToggleAction;

    /// <summary>Action for taking off or selecting the level above.</summary>
    public EntityUid? UpAction;

    /// <summary>Action for landing or selecting the level below.</summary>
    public EntityUid? DownAction;
}
