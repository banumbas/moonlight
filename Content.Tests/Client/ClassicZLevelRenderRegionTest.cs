using System;
using System.Numerics;
using Content.Client._Classic.ZLevels.Core;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Client;

[TestFixture]
public sealed class ClassicZLevelRenderRegionTest
{
    [TestCase(940, 520)]
    [TestCase(-10, 520)]
    [TestCase(1900, 520)]
    [TestCase(940, -10)]
    [TestCase(940, 1070)]
    public void SmallOpeningKeepsItsGuardBandWithoutRenderingTheWholeScreen(int x, int y)
    {
        var screen = new Vector2i(1920, 1080);
        var region = ClassicZLevelRenderRegion.Cover(new UIBox2(x, y, x + 32, y + 32), screen,
            Vector2i.Zero, new Vector2(96));
        Assert.Multiple(() =>
        {
            Assert.That(region.Left, Is.InRange(0, Math.Max(0, x - 96)));
            Assert.That(region.Top, Is.InRange(0, Math.Max(0, y - 96)));
            Assert.That(region.Right, Is.InRange(Math.Min(1920, x + 128), 1920));
            Assert.That(region.Bottom, Is.InRange(Math.Min(1080, y + 128), 1080));
            Assert.That(region.Width * region.Height, Is.LessThan(screen.X * screen.Y / 10));
        });
    }

    [Test]
    public void MovingOpeningKeepsLightingTexelsAligned()
    {
        var screen = new Vector2i(1280, 720);
        for (var x = 581; x < 589; x++)
        for (var y = 273; y < 281; y++)
        {
            var region = ClassicZLevelRenderRegion.Cover(new UIBox2(x, y, x + 128, y + 128),
                screen, Vector2i.Zero, new Vector2(96));
            Assert.That(region.Left % 4, Is.Zero);
            Assert.That(region.Top % 4, Is.Zero);
            Assert.That(region.Left, Is.LessThanOrEqualTo(x - 96));
            Assert.That(region.Top, Is.LessThanOrEqualTo(y - 96));
            Assert.That(region.Right, Is.GreaterThanOrEqualTo(x + 224));
            Assert.That(region.Bottom, Is.GreaterThanOrEqualTo(y + 224));
        }
    }

    [Test]
    public void OddScreenSizeDoesNotClipAnOpeningAtTheEdge()
    {
        var screen = new Vector2i(1281, 721);
        var region = ClassicZLevelRenderRegion.Cover(new UIBox2(1240, 680, 1281, 721),
            screen, Vector2i.Zero, new Vector2(96));
        Assert.That(region.Right, Is.EqualTo(screen.X));
        Assert.That(region.Bottom, Is.EqualTo(screen.Y));
        Assert.That(region.Left, Is.LessThanOrEqualTo(1240 - 96));
        Assert.That(region.Top, Is.LessThanOrEqualTo(680 - 96));
    }

    [Test]
    public void DistantOpeningsCoverTheFullScreen()
    {
        var screen = new Vector2i(1920, 1080);
        var region = ClassicZLevelRenderRegion.Cover(new UIBox2(0, 0, 1920, 1080), screen,
            new Vector2i(256, 256), new Vector2(96));
        Assert.That(region, Is.EqualTo(UIBox2i.FromDimensions(Vector2i.Zero, screen)));
    }

    [Test]
    public void AOnePixelChangeDoesNotResizeTargetsAndSmallOpeningsEventuallyShrinkThem()
    {
        var screen = new Vector2i(1920, 1080);
        var large = ClassicZLevelRenderRegion.Cover(new UIBox2(300, 300, 557, 557), screen, Vector2i.Zero, Vector2.Zero);
        var small = ClassicZLevelRenderRegion.Cover(new UIBox2(300, 300, 555, 555), screen, large.Size, Vector2.Zero);
        Assert.That(small.Size, Is.EqualTo(large.Size));
        var tiny = ClassicZLevelRenderRegion.Cover(new UIBox2(300, 300, 332, 332), screen, small.Size, Vector2.Zero);
        Assert.That(tiny.Width, Is.LessThan(small.Width));
        Assert.That(tiny.Height, Is.LessThan(small.Height));
    }
}
