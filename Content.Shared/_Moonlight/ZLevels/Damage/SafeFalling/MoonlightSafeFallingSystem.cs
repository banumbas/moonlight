/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Shared._Moonlight.ZLevels.Damage.SafeFalling;

public sealed class MoonlightSafeFallingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MoonlightSafeFallingComponent, MoonlightZFallingDamageCalculateEvent>(OnFallingDamageCalculate);
    }

    private void OnFallingDamageCalculate(Entity<MoonlightSafeFallingComponent> ent, ref MoonlightZFallingDamageCalculateEvent args)
    {
        if (args.Fallen == ent.Owner)
            return;

        args.DamageMultiplier *= ent.Comp.DamageMultiplier;
        args.StunMultiplier *= ent.Comp.StunMultiplier;
    }
}
