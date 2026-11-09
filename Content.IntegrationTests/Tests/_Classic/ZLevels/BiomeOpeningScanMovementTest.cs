using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server._Classic.Station;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Classic.ZLevels;

#pragma warning disable RA0002

[TestFixture]
public sealed class BiomeOpeningScanMovementTest : GameTest
{
    private static readonly ProtoId<BiomeTemplatePrototype> UndergroundBiome = "ClassicUndergroundDirtStone";

    [Test]
    public async Task MovingViewportReusesCompletedOpeningChunks()
    {
        var em = Server.EntMan;
        var map = em.System<SharedMapSystem>();
        var biomes = em.System<BiomeSystem>();
        EntityUid terrain = default;

        try
        {
            await Server.WaitPost(() =>
            {
                terrain = map.CreateMap(runMapInit: false);
                biomes.EnsurePlanet(
                    terrain,
                    Server.ProtoMan.Index(UndergroundBiome),
                    42);
                var streaming = em.EnsureComponent<ClassicBiomeStreamingComponent>(terrain);
                streaming.WorkBudget = TimeSpan.FromSeconds(1);
                streaming.OpeningCellsPerSlice = 64;
                map.InitializeMap(terrain);

                var biome = em.GetComponent<BiomeComponent>(terrain);
                var grid = em.GetComponent<MapGridComponent>(terrain);
                for (var x = -1; x <= 5; x++)
                for (var y = -1; y <= 4; y++)
                    biome.LoadedChunks.Add(new Vector2i(x * 8, y * 8));

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var begin = typeof(BiomeSystem).GetMethod("BeginClassicStreamingBudget", flags)!;
                var request = typeof(BiomeSystem).GetMethod("TryGetClassicOpeningWorldBounds", flags)!;

                bool Scan(Box2 bounds, int maxCalls, out int calls)
                {
                    var args = new object[] { terrain, biome, grid, bounds, default(Box2) };
                    for (calls = 1; calls <= maxCalls; calls++)
                    {
                        begin.Invoke(biomes, null);
                        if ((bool) request.Invoke(biomes, args)!)
                            return true;
                    }

                    calls = maxCalls;
                    return false;
                }

                Assert.That(Scan(new Box2(0f, 0f, 31f, 31f), 64, out _), Is.True);
                Assert.That(Scan(new Box2(8f, 0f, 39f, 31f), 8, out var movingCalls), Is.True,
                    $"A one-chunk viewport shift rescanned the whole opening range in {movingCalls} calls.");
            });
        }
        finally
        {
            if (terrain != EntityUid.Invalid)
                await Server.WaitPost(() => em.DeleteEntity(terrain));
        }
    }
}
