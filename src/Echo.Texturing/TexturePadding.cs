using SkiaSharp;
using Echo.Contracts;

namespace Echo.Texturing;

internal static class TexturePadding
{
    public static void Extend(Span<SKColor> pixels, int size, int gutter, CancellationToken cancellation, Action<OperationProgress>? progress = null)
    {
        if (gutter == 0)
        {
            return;
        }

        var padding = new Dictionary<int, PaddingTexel>();
        for (var y = 0; y < size; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (y % 32 == 0)
            {
                progress?.Invoke(new OperationProgress("Atlas gutters", "Extending nearest source texels around chart edges…", y, size, "rows"));
            }
            for (var x = 0; x < size; x++)
            {
                var source = y * size + x;
                if (pixels[source].Alpha == 0 || !IsBoundary(pixels, size, x, y))
                {
                    continue;
                }

                // Copy the closest ORIGINAL texel, never a previously padded one.
                // Iterated first-neighbour dilation produces diagonal streaks and
                // carries colours from the wrong part of an island's boundary.
                for (var dy = -gutter; dy <= gutter; dy++)
                {
                    var targetY = y + dy;
                    if (targetY < 0 || targetY >= size)
                    {
                        continue;
                    }
                    for (var dx = -gutter; dx <= gutter; dx++)
                    {
                        var targetX = x + dx;
                        var distance = dx * dx + dy * dy;
                        var withinGutter = targetX >= 0 && targetX < size && distance <= gutter * gutter;
                        if (!withinGutter)
                        {
                            continue;
                        }

                        var target = targetY * size + targetX;
                        if (pixels[target].Alpha != 0)
                        {
                            continue;
                        }
                        if (!padding.TryGetValue(target, out var nearest) || distance < nearest.DistanceSquared)
                        {
                            padding[target] = new PaddingTexel(source, distance);
                        }
                    }
                }
            }
        }

        foreach (var entry in padding)
        {
            pixels[entry.Key] = pixels[entry.Value.Source];
        }
    }

    private static bool IsBoundary(ReadOnlySpan<SKColor> pixels, int size, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var adjacentX = x + dx;
                var adjacentY = y + dy;
                var inside = adjacentX >= 0 && adjacentX < size && adjacentY >= 0 && adjacentY < size;
                if (inside && pixels[adjacentY * size + adjacentX].Alpha == 0)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
