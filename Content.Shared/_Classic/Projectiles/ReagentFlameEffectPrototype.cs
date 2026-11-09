using Content.Shared.Damage;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Classic.Projectiles;

/// <summary>
/// Prototype defining visual and combustion properties of a chemical reagent when fired by a flamethrower.
/// </summary>
[Prototype]
public sealed partial class ReagentFlameEffectPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<ReagentFlameEffectPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance, AbstractDataField]
    public bool Abstract { get; private set; }

    [DataField]
    public string StatePrefix = "red";

    [DataField]
    public Color FlameColor = Color.White;

    /// <summary>
    /// Direct damage dealt to entities impacted by the flame stream.
    /// </summary>
    [DataField]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// Fire stacks applied on direct hit from the flame stream.
    /// </summary>
    [DataField]
    public float FireStacks = 4.0f;

    /// <summary>
    /// Fire stacks applied per step when walking on the resulting tile fire.
    /// </summary>
    [DataField]
    public float TileFireStacks = 0.8f;

    /// <summary>
    /// Maximum fire stacks an entity can accumulate purely from walking or standing on this tile fire.
    /// </summary>
    [DataField]
    public float MaxTileFireStacks = 4.0f;

    /// <summary>
    /// Duration of the resulting ground fire hazard in seconds.
    /// </summary>
    [DataField]
    public float TileFireDuration = 10f;

    /// <summary>
    /// Movement speed modifier applied to entities walking on the resulting tile fire (1.0 = normal, 0.5 = 50% speed).
    /// </summary>
    [DataField]
    public float WalkSpeedModifier = 0.65f;

    /// <summary>
    /// Sprint speed modifier applied to entities walking on the resulting tile fire (1.0 = normal, 0.5 = 50% speed).
    /// </summary>
    [DataField]
    public float SprintSpeedModifier = 0.65f;


    /// <summary>
    /// Aerodynamic deceleration factor affecting stream range and bunching (applied to Box2D linear damping).
    /// </summary>
    [DataField]
    public float LinearDamping = 1.8f;

    /// <summary>
    /// Flight time of the flame projectile before expiring and creating the tile fire hazard (seconds).
    /// </summary>
    [DataField]
    public float ProjectileLifetime = 0.55f;

    /// <summary>
    /// Initial stream scale at muzzle.
    /// </summary>
    [DataField]
    public float MinScale = 0.55f;

    /// <summary>
    /// Final stream scale at maximum range.
    /// </summary>
    [DataField]
    public float MaxScale = 1.35f;
}
