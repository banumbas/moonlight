using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Moonlight.SupplyPods;

/// <summary>
/// Visual style variants for supply pods. Maps to texture directories.
/// </summary>
[Serializable, NetSerializable]
public enum MoonlightSupplyPodVisual : byte
{
    Default,
    Nanotrasen,
    Syndicate,
    Bluespace,
    Cult,
    Gondola,
    Honk,
    Orange,
    Squad,
}

/// <summary>
/// Appearance keys for supply pod sprite state.
/// </summary>
[Serializable, NetSerializable]
public enum MoonlightSupplyPodVisuals : byte
{
    /// <summary>
    /// The current landing phase of the pod. See <see cref="MoonlightSupplyPodPhase"/>.
    /// </summary>
    Phase,
}

/// <summary>
/// Sprite layer keys for supply pod rendering. The pod uses a dedicated
/// <c>Falling</c> layer (separate from the storage Base/Door layers) so the
/// falling animation does not conflict with <c>EntityStorageVisualizerSystem</c>.
/// </summary>
[Serializable, NetSerializable]
public enum MoonlightSupplyPodVisualLayers : byte
{
    Base,
    Door,
    /// <summary>
    /// Overlay layer used exclusively for the falling animation.
    /// </summary>
    Falling,
}

/// <summary>
/// Landing phases for a supply pod. Controls client-side sprite/animation.
/// </summary>
[Serializable, NetSerializable]
public enum MoonlightSupplyPodPhase : byte
{
    /// <summary>
    /// Pod is hidden; only the warning indicator is visible. Waiting before the
    /// falling animation starts.
    /// </summary>
    Warning,

    /// <summary>
    /// Falling animation is playing on the pod entity itself.
    /// </summary>
    Falling,

    /// <summary>
    /// Pod has landed. Normal sprite is shown.
    /// </summary>
    Landed,
}

/// <summary>
/// Component placed on supply pod entities. The pod is an EntityStorage
/// that can hold players or items. Abstract: any system can request a
/// delivery via <see cref="SharedMoonlightSupplyPodSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MoonlightSupplyPodComponent : Component
{
    /// <summary>
    /// Which visual variant to use for this pod.
    /// </summary>
    [DataField, AutoNetworkedField]
    public MoonlightSupplyPodVisual Visual = MoonlightSupplyPodVisual.Default;

    /// <summary>
    /// Total time from delivery request until the pod lands. The falling
    /// animation (<see cref="FallAnimationLeadTime"/>) is played only during
    /// the final portion; before that the pod is in the Warning phase.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FallDuration = 5.0f;

    /// <summary>
    /// Duration of the RSI falling animation (2s + 19*0.009s ≈ 2.171s).
    /// The Falling phase begins exactly this long before landing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FallAnimationLeadTime = 2.171f;

    /// <summary>
    /// Current landing phase. Networked so the client can react without events.
    /// </summary>
    [DataField, AutoNetworkedField]
    public MoonlightSupplyPodPhase Phase = MoonlightSupplyPodPhase.Warning;

    /// <summary>
    /// Whether the pod auto-opens after landing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AutoOpen = true;

    /// <summary>
    /// Delay after landing before auto-opening (if enabled).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OpenDelay = 1.0f;

    /// <summary>
    /// Sound played when the pod launches/falls.
    /// </summary>
    [DataField]
    public SoundSpecifier? LaunchSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

    /// <summary>
    /// Sound played when the pod lands/impacts.
    /// </summary>
    [DataField]
    public SoundSpecifier? ImpactSound = new SoundPathSpecifier("/Audio/Effects/metal_thud1.ogg");

    /// <summary>
    /// Effect entity spawned at the impact location (explosion, dust, etc).
    /// </summary>
    [DataField]
    public EntProtoId? ImpactEffect = "MoonlightSupplyPodImpactEffect";

    /// <summary>
    /// Prototype for the non-clickable warning indicator spawned at the landing
    /// location during the Warning/Falling phases.
    /// </summary>
    [DataField]
    public EntProtoId TargetIndicatorProto = "MoonlightSupplyPodTargetIndicator";

    /// <summary>
    /// Radius (in tiles) for area damage applied on landing.
    /// </summary>
    [DataField]
    public float ImpactRadius = 1.0f;

    /// <summary>
    /// Damage applied to entities (except the pod and its contents) caught in the
    /// impact radius on landing. Armor is respected (not armor-piercing).
    /// </summary>
    [DataField]
    public DamageSpecifier ImpactDamage = new()
    {
        DamageDict = new()
        {
            { "Blunt", 100 },
            { "Structural", 500 },
        }
    };

    /// <summary>
    /// Minimum brute damage applied to mob passengers on impact.
    /// </summary>
    [DataField]
    public float PassengerMinBrute = 20f;

    /// <summary>
    /// Maximum brute damage applied to mob passengers on impact.
    /// </summary>
    [DataField]
    public float PassengerMaxBrute = 30f;

    /// <summary>
    /// Duration of the stun+knockdown applied to mob passengers the moment they
    /// are inserted, kept until landing (prevents miss-predicted escape attempts
    /// by blocking interactions at the attempt-event level). In seconds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PreLandStunTime = 10.0f;

    /// <summary>
    /// Lifetime of the pod after it opens before it despawns (seconds). 0 = permanent.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DespawnTime = 0f;
}
