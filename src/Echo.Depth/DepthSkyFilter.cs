using Echo.Contracts;

namespace Echo.Depth;

public static class DepthSkyFilter
{
    public static int Apply(DepthMap map, SemanticMap semantics, CancellationToken cancellation)
    {
        var valid = semantics.ImageId == map.ImageId && semantics.Width > 0 && semantics.Height > 0
            && (long)semantics.Width * semantics.Height == semantics.SkySamples.Count;
        if (!valid)
        {
            throw new ArgumentException("Sky filtering requires matching sky probabilities.", nameof(semantics));
        }
        // Classify each low-resolution semantic cell once. Paged probability
        // buffers must not be reread four times for every full-resolution pixel.
        var cells = new bool[semantics.SkySamples.Count];
        for (var y = 0; y < semantics.Height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < semantics.Width; x++)
            {
                cells[y * semantics.Width + x] = IsSkyCell(semantics, x, y);
            }
        }
        var removed = 0;
        for (var y = 0; y < map.Height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sy = Math.Clamp((int)Math.Floor((y + .5) / map.Height * semantics.Height - .5), 0, semantics.Height - 1);
            for (var x = 0; x < map.Width; x++)
            {
                var index = y * map.Width + x;
                var sx = Math.Clamp((int)Math.Floor((x + .5) / map.Width * semantics.Width - .5), 0, semantics.Width - 1);
                if (map.Depth[index] <= 0 || !cells[sy * semantics.Width + sx])
                {
                    continue;
                }
                map.Depth[index] = 0;
                map.Confidence[index] = 0;
                removed++;
            }
        }
        return removed;
    }

    public static bool IsSky(SemanticMap map, double u, double v)
    {
        if (map.SkySamples.Count == 0)
        {
            return false;
        }
        // Require all four neighbouring labels to agree. Clamp at the image
        // border so the topmost sky pixels are masked as well as the interior.
        var x = Math.Clamp((int)Math.Floor(u * map.Width - .5), 0, map.Width - 1);
        var y = Math.Clamp((int)Math.Floor(v * map.Height - .5), 0, map.Height - 1);
        return IsSkyCell(map, x, y);
    }

    private static bool IsSkyCell(SemanticMap map, int x, int y)
    {
        var nextX = Math.Min(x + 1, map.Width - 1);
        var nextY = Math.Min(y + 1, map.Height - 1);
        var minimum = Math.Min(Math.Min(map.SkySamples[y * map.Width + x], map.SkySamples[y * map.Width + nextX]),
            Math.Min(map.SkySamples[nextY * map.Width + x], map.SkySamples[nextY * map.Width + nextX]));
        return minimum >= 204;
    }
}
