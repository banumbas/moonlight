using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Classic.ZLevels.Core;
using Content.Shared._Classic.ZLevels.Roof;
using Content.Shared.Light.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

[TestFixture]
public sealed class PlanetaryLightingClientTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    [Test]
    public async Task EnabledPlanetaryCycleRemainsSynchronizedOnClient()
    {
        var map = SEntMan.System<SharedMapSystem>();
        var zLevels = SEntMan.System<ClassicZLevelsSystem>();
        var session = ServerSession!;
        var originalViewer = session.AttachedEntity;

        var networkUid = EntityUid.Invalid;
        var upperMapNet = NetEntity.Invalid;
        var lowerMapNet = NetEntity.Invalid;
        var mapsAdded = false;

        try
        {
            await Server.WaitPost(() =>
            {
                var upperMap = map.CreateMap();
                var lowerMap = map.CreateMap();
                var upperGrid = map.CreateGridEntity(upperMap);
                map.CreateGridEntity(lowerMap);
                SEntMan.EnsureComponent<ClassicZLevelRoofComponent>(upperGrid);

                var color = Color.FromHex("#A4C8E1");
                var light = SEntMan.EnsureComponent<MapLightComponent>(upperMap);
                light.AmbientLightColor = color;

                var cycle = SEntMan.EnsureComponent<LightCycleComponent>(upperMap);
                cycle.OriginalColor = color;
                cycle.Duration = TimeSpan.FromMinutes(47);
                cycle.Offset = TimeSpan.FromMinutes(13);
                cycle.Enabled = true;
                cycle.InitialOffset = false;

                var network = zLevels.CreateMapNetwork();
                networkUid = network;
                mapsAdded = zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [upperMap] = 0,
                    [lowerMap] = -1,
                });

                var viewer = SEntMan.SpawnEntity(null, new EntityCoordinates(upperMap, Vector2.Zero));
                Server.PlayerMan.SetAttachedEntity(session, viewer);
                upperMapNet = SEntMan.GetNetEntity(upperMap);
                lowerMapNet = SEntMan.GetNetEntity(lowerMap);
            });
            await Server.WaitAssertion(() => Assert.That(mapsAdded, Is.True));

            await Pair.RunTicksSync(10);
            await Pair.SyncTicks(targetDelta: 1);

            await Client.WaitAssertion(() =>
            {
                var upperMap = CEntMan.GetEntity(upperMapNet);
                var lowerMap = CEntMan.GetEntity(lowerMapNet);
                var upperLight = CEntMan.GetComponent<MapLightComponent>(upperMap);
                var lowerLight = CEntMan.GetComponent<MapLightComponent>(lowerMap);
                var upperCycle = CEntMan.GetComponent<LightCycleComponent>(upperMap);
                var lowerCycle = CEntMan.GetComponent<LightCycleComponent>(lowerMap);
                var upperShadow = CEntMan.GetComponent<SunShadowComponent>(upperMap);
                var lowerShadow = CEntMan.GetComponent<SunShadowComponent>(lowerMap);

                Assert.Multiple(() =>
                {
                    Assert.That(upperCycle.Enabled, Is.True);
                    Assert.That(lowerCycle.Enabled, Is.True);
                    Assert.That(lowerCycle.OriginalColor, Is.EqualTo(upperCycle.OriginalColor));
                    Assert.That(lowerCycle.Duration, Is.EqualTo(upperCycle.Duration));
                    Assert.That(lowerCycle.Offset, Is.EqualTo(upperCycle.Offset));
                    Assert.That(lowerLight.AmbientLightColor, Is.EqualTo(upperLight.AmbientLightColor));
                    Assert.That(upperLight.AmbientLightColor, Is.Not.EqualTo(upperCycle.OriginalColor),
                        "The enabled client light cycle must actually update ambient light.");
                    Assert.That(upperShadow.Direction, Is.Not.EqualTo(Vector2.Zero),
                        "The client sun cycle must produce a real direction, not matching defaults by accident.");
                    Assert.That(upperShadow.Alpha, Is.GreaterThan(0f),
                        "The client sun cycle must produce a visible shadow at the configured phase.");
                    Assert.That(lowerShadow.Direction, Is.EqualTo(upperShadow.Direction));
                    Assert.That(lowerShadow.Alpha, Is.EqualTo(upperShadow.Alpha));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(session, originalViewer));
            if (networkUid != EntityUid.Invalid)
            {
                await Server.WaitPost(() => zLevels.DeleteMapNetwork(networkUid));
                await Server.WaitRunTicks(2);
            }
        }
    }
}
