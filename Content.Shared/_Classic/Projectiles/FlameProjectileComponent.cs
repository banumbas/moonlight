using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Projectiles;

/// <summary>
/// Attached to physical flame stream projectiles.
/// Controls lifetime and links to the reagent flame effect prototype.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class FlameProjectileComponent : Component
{
    /// <summary>
    /// ID of the reagent flame effect prototype governing visual and combustion properties.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ReagentFlameEffectPrototype> Effect = "Napalm";

    /// <summary>
    /// Total lifetime of the flame projectile before expiring.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Lifetime = 0.55f;

    /// <summary>
    /// Timestamp when this projectile was spawned (used by client for jitter-free visual progression).
    /// </summary>
    public TimeSpan SpawnTime = TimeSpan.Zero;

    /// <summary>
    /// Server-side elapsed age in seconds.
    /// </summary>
    public float Age = 0f;

    /// <summary>
    /// Identifier of the chemical reagent carried by this projectile for puddles.
    /// </summary>
    [DataField]
    public string Reagent = "Napalm";

    /// <summary>
    /// Volume of reagent carried for puddle creation.
    /// </summary>
    [DataField]
    public FixedPoint2 ReagentAmount = FixedPoint2.New(1);

    /// <summary>
    /// Current visual stage (1 to 4) applied to the sprite.
    /// </summary>
    public int CurrentStage = 0;
}
