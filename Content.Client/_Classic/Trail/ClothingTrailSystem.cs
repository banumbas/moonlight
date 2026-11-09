using Content.Client._Starlight.Overlay.Trail;
using Content.Shared._Classic.Trail;
using Content.Shared._Starlight.Trail;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Containers;

namespace Content.Client._Classic.Trail;

/// <summary>
/// Applies a clothing item's client-side motion trail to its wearer.
/// </summary>
public sealed partial class ClothingTrailSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;

    private EntityQuery<ItemToggleComponent> _toggleQuery;
    private EntityQuery<TrailComponent> _trailQuery;

    public override void Initialize()
    {
        base.Initialize();

        _toggleQuery = GetEntityQuery<ItemToggleComponent>();
        _trailQuery = GetEntityQuery<TrailComponent>();

        SubscribeLocalEvent<ClothingTrailComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClothingTrailComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<ClothingTrailComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ClothingTrailComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ClothingTrailComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<ClothingTrailComponent> ent, ref MapInitEvent args)
    {
        if (!_toggleQuery.TryComp(ent, out var toggle) || !toggle.Activated)
            return;

        UpdateTrail(ent);
    }

    private void OnToggled(Entity<ClothingTrailComponent> ent, ref ItemToggledEvent args)
    {
        UpdateTrail(ent, args.Activated);
    }

    private void OnInserted(Entity<ClothingTrailComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        UpdateTrail(ent);
    }

    private void OnRemoved(Entity<ClothingTrailComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        RemoveTrail(ent.Comp);
    }

    private void OnShutdown(Entity<ClothingTrailComponent> ent, ref ComponentShutdown args)
    {
        RemoveTrail(ent.Comp);
    }

    private void UpdateTrail(Entity<ClothingTrailComponent> clothing, bool? enabled = null)
    {
        if (enabled == false || !_toggleQuery.TryComp(clothing, out var toggle) || !toggle.Activated)
        {
            RemoveTrail(clothing.Comp);
            return;
        }

        if (!_container.TryGetContainingContainer((clothing.Owner, null, null), out var container))
        {
            RemoveTrail(clothing.Comp);
            return;
        }

        var wearer = container.Owner;
        if (clothing.Comp.Wearer is { } previousWearer && previousWearer != wearer)
            RemoveTrail(clothing.Comp);

        if (clothing.Comp.Wearer == wearer && _trailQuery.TryComp(wearer, out var existingTrail))
        {
            ConfigureTrail(existingTrail, clothing.Comp.Trail);
            return;
        }

        if (_trailQuery.HasComp(wearer))
            return;

        var trail = EnsureComp<TrailComponent>(wearer);
        ConfigureTrail(trail, clothing.Comp.Trail);
        clothing.Comp.Wearer = wearer;
        clothing.Comp.OwnsTrail = true;
    }

    private void RemoveTrail(ClothingTrailComponent clothing)
    {
        if (clothing.Wearer is not { } wearer)
            return;

        if (clothing.OwnsTrail && _trailQuery.TryComp(wearer, out _))
            RemComp<TrailComponent>(wearer);

        clothing.Wearer = null;
        clothing.OwnsTrail = false;
    }

    private static void ConfigureTrail(TrailComponent trail, TrailSettings settings)
    {
        trail.TrailColor = settings.Color;
        trail.FadeColor = settings.FadeColor;
        trail.MaxPoints = settings.MaxPoints;
        trail.MinDistance = settings.MinDistance;
        trail.LineWidth = settings.LineWidth;
        trail.DecayDelay = settings.DecayDelay;
        trail.DecayInterval = settings.DecayInterval;
        trail.Shader = settings.Shader;
        trail.Mode = settings.Mode;
        trail.SkipSamples = settings.SkipSamples;

        if (trail.Points.Capacity != trail.MaxPoints)
            trail.Points.Resize(trail.MaxPoints);
        if (trail.Samples.Capacity != trail.MaxPoints)
            trail.Samples.Resize(trail.MaxPoints);
    }
}
