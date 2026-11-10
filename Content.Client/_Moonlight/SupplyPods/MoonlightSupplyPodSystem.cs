using Content.Shared._Moonlight.SupplyPods;
using Content.Shared.Storage;
using Robust.Client.GameObjects;

namespace Content.Client._Moonlight.SupplyPods;

/// <summary>
/// Client-side supply pod system. Handles sprite appearance based on the
/// <see cref="MoonlightSupplyPodPhase"/> networked via the component state:
/// - <see cref="MoonlightSupplyPodPhase.Warning"/>: pod sprite hidden (only the
///   separate warning indicator entity is visible to players).
/// - <see cref="MoonlightSupplyPodPhase.Falling"/>: the pod plays the built-in RSI
///   falling animation via the dedicated <c>Falling</c> overlay layer.
/// - <see cref="MoonlightSupplyPodPhase.Landed"/>: the pod shows its normal
///   base/door sprites, with the door reflecting the storage open/closed state.
///
/// The falling animation uses a separate sprite layer
/// (<see cref="MoonlightSupplyPodVisualLayers.Falling"/>) so it does not conflict
/// with the storage base/door layers managed by the
/// <see cref="Content.Client.Storage.Visualizers.EntityStorageVisualizerSystem"/>.
///
/// The impact effect entity is spawned server-side and replicated via PVS, so the
/// client does not need to spawn it again.
/// </summary>
public sealed partial class MoonlightSupplyPodSystem : SharedMoonlightSupplyPodSystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    // RSI state names for the Door layer in the Landed phase. When the storage is
    // open the door swaps to the open state; when closed it uses the closed state.
    // The Base layer always keeps its prototype state (default_pod).
    private const string DoorStateClosed = "default_closed";
    private const string DoorStateOpen = "default_open";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MoonlightSupplyPodComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    /// <summary>
    /// Update the pod's sprite based on the current landing phase.
    /// During the <see cref="MoonlightSupplyPodPhase.Landed"/> phase the Door layer
    /// also reflects the storage open/closed state.
    /// </summary>
    private void OnAppearanceChange(Entity<MoonlightSupplyPodComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        // Determine the phase; default to Landed if missing (so it stays visible).
        if (!_appearance.TryGetData<MoonlightSupplyPodPhase>(ent.Owner, MoonlightSupplyPodVisuals.Phase, out var phase, args.Component))
            phase = MoonlightSupplyPodPhase.Landed;

        switch (phase)
        {
            case MoonlightSupplyPodPhase.Warning:
                // Pod is hidden during the warning phase; only the indicator is visible.
                args.Sprite.Visible = false;
                break;

            case MoonlightSupplyPodPhase.Falling:
                // Show the pod with the falling animation on the dedicated Falling layer.
                args.Sprite.Visible = true;

                // The Falling layer's RSI is already set in the prototype
                // (_Moonlight/Effects/supplypod_falling.rsi). We only need to select
                // the correct state for the visual variant.
                if (!FallingSprites.TryGetValue(ent.Comp.Visual, out var spriteInfo))
                    spriteInfo = FallingSprites[MoonlightSupplyPodVisual.Default];

                _sprite.LayerSetRsiState((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Falling, spriteInfo.State);
                _sprite.LayerSetVisible((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Falling, true);

                // Hide base/door layers while falling.
                _sprite.LayerSetVisible((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Base, false);
                _sprite.LayerSetVisible((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Door, false);
                break;

            case MoonlightSupplyPodPhase.Landed:
            default:
                // Show normal layers; hide the falling-animation layer.
                args.Sprite.Visible = true;

                _sprite.LayerSetVisible((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Falling, false);
                _sprite.LayerSetVisible((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Base, true);

                // Reflect the storage open/closed state on the Door layer.
                // This fixes the issue where the open sprite looked identical to the closed one.
                var open = _appearance.TryGetData<bool>(ent.Owner, StorageVisuals.Open, out var openValue, args.Component)
                           && openValue;

                // The Door layer swaps between the closed/open RSI states.
                _sprite.LayerSetVisible((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Door, true);
                _sprite.LayerSetRsiState((ent.Owner, args.Sprite), MoonlightSupplyPodVisualLayers.Door,
                    open ? DoorStateOpen : DoorStateClosed);

                break;
        }
    }
}
