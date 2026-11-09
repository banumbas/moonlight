using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Client._Classic.ZLevels.Core;

/// <summary>Native-resolution lower-level render targets, bounded to the visible openings.</summary>
internal static class ClassicZLevelRenderRegion
{
    public static UIBox2i Cover(UIBox2 openings, Vector2i screenSize, Vector2i previousSize, Vector2 padding)
    {
        var left = Math.Clamp((int) MathF.Floor(openings.Left - padding.X), 0, screenSize.X);
        var top = Math.Clamp((int) MathF.Floor(openings.Top - padding.Y), 0, screenSize.Y);
        var right = Math.Clamp((int) MathF.Ceiling(openings.Right + padding.X), left, screenSize.X);
        var bottom = Math.Clamp((int) MathF.Ceiling(openings.Bottom + padding.Y), top, screenSize.Y);
        var size = new Vector2i(
            TargetSize(right - left, screenSize.X, previousSize.X),
            TargetSize(bottom - top, screenSize.Y, previousSize.Y));

        left = TargetOrigin(left, right, size.X, screenSize.X);
        top = TargetOrigin(top, bottom, size.Y, screenSize.Y);
        return UIBox2i.FromDimensions(new Vector2i(left, top), size);
    }

    private static int TargetOrigin(int start, int end, int size, int screen)
    {
        var min = Math.Max(0, end - size);
        var max = Math.Min(start, screen - size);
        var center = (start + end - size) / 2;
        // Keep the texel phase of the engine's half/quarter-resolution lighting targets.
        // Otherwise moving the crop by one pixel also shifts shadow interpolation.
        // At an odd-sized screen edge, covering the opening takes precedence.
        var alignedMin = (min + 3) / 4 * 4;
        var alignedMax = max / 4 * 4;
        return alignedMin <= alignedMax
            ? Math.Clamp(center / 4 * 4, alignedMin, alignedMax)
            : Math.Clamp(center, min, max);
    }

    private static int TargetSize(int required, int screen, int previous)
    {
        // Quantization and shrink hysteresis avoid reallocating all the lighting targets
        // whenever a moving opening grows/shrinks by a pixel or crosses a size boundary.
        var size = 128;
        while (size < required)
            size *= 2;
        size = Math.Min(size, screen);
        return previous >= size && previous <= screen && required > previous / 4 ? previous : size;
    }
}
