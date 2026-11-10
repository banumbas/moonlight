/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Client._Moonlight.ZLevels.Core;
using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Flight;
using Content.Shared._Moonlight.ZLevels.Flight.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Moonlight.ZLevels.Flight;

public sealed partial class MoonlightClientZFlightSystem : MoonlightSharedZFlightSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MoonlightZFlyerComponent, MoonlightZPhysicsComponent, TransformComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var flyer, out var zPhys, out var xform, out var sprite))
        {
            if (!flyer.Active)
                continue;
            if (_timing.CurTime < flyer.NextVfx)
                continue;
            flyer.NextVfx = _timing.CurTime + TimeSpan.FromSeconds(0.2f);

            if (flyer.FlightVfx is not null)
            {
                var vfx = SpawnAtPosition(flyer.FlightVfx, xform.Coordinates);
                _sprite.SetOffset(vfx, new Vector2(0, zPhys.LocalPosition * MoonlightClientZLevelsSystem.ZLevelOffset) + zPhys.SpriteOffsetDefault);
            }
        }
    }
}
