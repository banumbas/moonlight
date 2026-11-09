using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._Classic.Projectiles;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.StepTrigger.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Server._Classic.Projectiles;

/// <summary>
/// Server system handling flame impact ignition, chemical puddle creation,
/// and spawning responsive tile fire hazard linked to reagent prototypes.
/// </summary>
public sealed class FlameProjectileSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SpeedModifierContactsSystem _speedModifierContacts = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlameProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<FlameTileFireComponent, StepTriggeredOnEvent>(OnTileFireStepTriggeredOn);
        SubscribeLocalEvent<FlameTileFireComponent, StartCollideEvent>(OnTileFireCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FlameProjectileComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var flame, out var physics))
        {
            flame.Age += frameTime;

            if (_proto.TryIndex(flame.Effect, out var effect) && effect.LinearDamping > 0f)
            {
                var newVel = physics.LinearVelocity * MathF.Max(0f, 1f - effect.LinearDamping * frameTime);
                _physics.SetLinearVelocity(uid, newVel, body: physics);
            }

            if (flame.Age >= flame.Lifetime)
            {
                SpawnTileFire(_transform.GetMoverCoordinates(uid), flame);
                QueueDel(uid);
            }
        }
    }

    private void OnProjectileHit(Entity<FlameProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (_proto.TryIndex(ent.Comp.Effect, out var effect))
        {
            if (TryComp<FlammableComponent>(args.Target, out var flammable))
                _flammable.AdjustFireStacks(args.Target, effect.FireStacks, flammable, ignite: true);
        }

        SpawnTileFire(_transform.GetMoverCoordinates(ent.Owner), ent.Comp);
        QueueDel(ent.Owner);
    }

    private void OnTileFireStepTriggeredOn(Entity<FlameTileFireComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (_proto.TryIndex(ent.Comp.Effect, out var effect))
            ApplyTileFire(args.Tripper, effect);
    }

    private void OnTileFireCollide(Entity<FlameTileFireComponent> ent, ref StartCollideEvent args)
    {
        if (args.OtherFixture.Hard && _proto.TryIndex<ReagentFlameEffectPrototype>(ent.Comp.Effect, out var effect))
            ApplyTileFire(args.OtherEntity, effect);
    }

    private void ApplyTileFire(EntityUid target, ReagentFlameEffectPrototype effect)
    {
        if (!TryComp<FlammableComponent>(target, out var flammable))
            return;

        if (flammable.FireStacks < effect.MaxTileFireStacks)
        {
            var added = MathF.Min(effect.TileFireStacks, effect.MaxTileFireStacks - flammable.FireStacks);
            _flammable.AdjustFireStacks(target, added, flammable, ignite: true);
        }
        else if (!flammable.OnFire)
        {
            _flammable.Ignite(target, target, flammable);
        }
    }

    private void SpawnTileFire(EntityCoordinates coords, FlameProjectileComponent flame)
    {
        if (!_proto.TryIndex(flame.Effect, out var effect))
            return;

        // Drop chemical fuel puddle
        if (flame.ReagentAmount > FixedPoint2.Zero &&
            _puddle.TrySpillAt(coords, new Solution(flame.Reagent, flame.ReagentAmount), out var puddleUid, sound: false))
        {
            if (TryComp<FlammableComponent>(puddleUid, out var puddleFlammable))
                _flammable.AdjustFireStacks(puddleUid, effect.FireStacks, puddleFlammable, ignite: true);
        }

        // Check if there is already an active tile fire at this location
        foreach (var existing in _lookup.GetEntitiesInRange<FlameTileFireComponent>(coords, 0.4f))
        {
            if (TryComp<TimedDespawnComponent>(existing.Owner, out var existingTimed))
                existingTimed.Lifetime = MathF.Max(existingTimed.Lifetime, effect.TileFireDuration);

            if (TryComp<SpeedModifierContactsComponent>(existing.Owner, out var existingSpeed))
            {
                var walkMod = MathF.Min(existingSpeed.WalkSpeedModifier, effect.WalkSpeedModifier);
                var sprintMod = MathF.Min(existingSpeed.SprintSpeedModifier, effect.SprintSpeedModifier);
                _speedModifierContacts.ChangeSpeedModifiers(existing.Owner, walkMod, sprintMod, existingSpeed);
            }

            return;
        }

        // Immediately ignite any flammable entities directly on the impacted tile
        foreach (var ent in _lookup.GetEntitiesInRange<FlammableComponent>(coords, 0.45f))
        {
            ApplyTileFire(ent.Owner, effect);
        }

        var fire = Spawn("FlameTileFire", coords);

        if (TryComp<FlameTileFireComponent>(fire, out var fireComp))
        {
            fireComp.Effect = flame.Effect;
            Dirty(fire, fireComp);
        }

        if (TryComp<TimedDespawnComponent>(fire, out var timed))
            timed.Lifetime = effect.TileFireDuration;

        if (TryComp<SpeedModifierContactsComponent>(fire, out var speedContacts))
            _speedModifierContacts.ChangeSpeedModifiers(fire, effect.WalkSpeedModifier, effect.SprintSpeedModifier, speedContacts);

        if (_pointLight.TryGetLight(fire, out var light))
            _pointLight.SetColor(fire, effect.FlameColor, light);

        foreach (var ent in _lookup.GetEntitiesInRange<MovementSpeedModifierComponent>(coords, 0.45f))
        {
            _speedModifierContacts.AddModifiedEntity(ent.Owner);
        }
    }
}
