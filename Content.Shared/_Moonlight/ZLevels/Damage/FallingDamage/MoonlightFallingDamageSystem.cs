/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared.Damage.Systems;

namespace Content.Shared._Moonlight.ZLevels.Damage.FallingDamage;

public sealed partial class MoonlightFallingDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoonlightFallingDamageComponent, MoonlightZFellOnMeEvent>(OnFallOnMe);
    }

    private void OnFallOnMe(Entity<MoonlightFallingDamageComponent> ent, ref MoonlightZFellOnMeEvent args)
    {
        _damageable.TryChangeDamage(args.Fallen, ent.Comp.Damage * args.Speed);
    }
}
