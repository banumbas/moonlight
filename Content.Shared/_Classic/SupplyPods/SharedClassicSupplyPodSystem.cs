using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.SupplyPods;

/// <summary>
/// Abstract base for the supply pod delivery system. Provides the public API
/// that both server and client can reference. Server-side implementations
/// perform actual entity spawning/delivery; client-side handles animations.
/// </summary>
public abstract partial class SharedClassicSupplyPodSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    [Dependency] protected readonly SharedTransformSystem Transform = default!;

    /// <summary>
    /// Default prototype for a supply pod entity. This entity should be an
    /// EntityStorage with a <see cref="ClassicSupplyPodComponent"/>.
    /// </summary>
    public static readonly EntProtoId DefaultPodPrototype = "ClassicSupplyPod";

    /// <summary>
    /// Maps a visual variant to its falling-animation RSI path and state id.
    /// Centralized so server + client stay in sync without hardcoding strings.
    /// </summary>
    public static readonly Dictionary<ClassicSupplyPodVisual, (string Rsi, string State)> FallingSprites = new()
    {
        { ClassicSupplyPodVisual.Default, ("/Textures/_Classic/Effects/supplypod_falling.rsi", "default_falling_animation") },
    };
}
