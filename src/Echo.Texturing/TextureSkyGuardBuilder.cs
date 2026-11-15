using Echo.Geometry;
using SkiaSharp;

namespace Echo.Texturing;

internal static class TextureSkyGuardBuilder
{
    public static TextureSkyGuard? Build(ReadOnlySpan<SKColor> pixels, int width, int height,
        bool swapRedBlue, int marginPixels, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (marginPixels is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(marginPixels));
        }
        var cellSize = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / 1024d));
        var columns = (width + cellSize - 1) / cellSize;
        var rows = (height + cellSize - 1) / cellSize;
        var colours = new V3[columns * rows];
        var eligible = new bool[colours.Length];
        for (var row = 0; row < rows; row++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < columns; column++)
            {
                var sum = new V3();
                var squares = new V3();
                var count = 0;
                for (var y = row * cellSize; y < Math.Min(height, (row + 1) * cellSize); y++)
                {
                    for (var x = column * cellSize; x < Math.Min(width, (column + 1) * cellSize); x++)
                    {
                        var pixel = pixels[y * width + x];
                        var colour = new V3(swapRedBlue ? pixel.Blue : pixel.Red, pixel.Green, swapRedBlue ? pixel.Red : pixel.Blue);
                        sum += colour;
                        squares += new V3(colour.X * colour.X, colour.Y * colour.Y, colour.Z * colour.Z);
                        count++;
                    }
                }
                var mean = sum / count;
                var variance = squares / count - new V3(mean.X * mean.X, mean.Y * mean.Y, mean.Z * mean.Z);
                var smooth = Math.Max(variance.X, Math.Max(variance.Y, variance.Z)) <= 144;
                var blueSky = mean.Z > mean.X * 1.06 && mean.Z >= mean.Y * .98 && mean.Y >= mean.X * .9
                    && mean.X + mean.Y + mean.Z > 270;
                var maximum = Math.Max(mean.X, Math.Max(mean.Y, mean.Z));
                var minimum = Math.Min(mean.X, Math.Min(mean.Y, mean.Z));
                var paleSky = minimum >= 140 && maximum - minimum <= maximum * .2;
                var index = row * columns + column;
                colours[index] = mean;
                eligible[index] = smooth && (blueSky || paleSky);
            }
        }
        var sky = FloodSky(colours, eligible, columns, cancellation);
        if (!sky.Any(value => value) || sky.All(value => value))
        {
            return null;
        }
        // Include one analysis cell for bilinear footprints and mask rounding.
        var radius = (int)Math.Ceiling((double)marginPixels / cellSize) + 1;
        var distance = Distances(sky, columns, radius, cancellation);
        var safe = distance.Select(value => value > radius).ToArray();
        // Limit colour extension to a narrow band. Large projection errors or
        // invented geometry must not be concealed by unlimited edge stretching.
        var nearest = NearestSafe(safe, columns, radius * 4, cancellation);
        return new TextureSkyGuard(columns, cellSize, nearest);
    }

    private static bool[] FloodSky(V3[] colours, bool[] eligible, int width, CancellationToken cancellation)
    {
        var sky = new bool[colours.Length];
        var pending = new Queue<int>();
        for (var x = 0; x < width; x++)
        {
            if (IsSkySeed(colours, eligible, width, x))
            {
                sky[x] = true;
                pending.Enqueue(x);
            }
        }
        Span<int> neighbours = stackalloc int[4];
        while (pending.TryDequeue(out var index))
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Neighbours(index, width, sky.Length, neighbours);
            foreach (var other in neighbours[..count])
            {
                var candidate = !sky[other] && eligible[other];
                if (!candidate)
                {
                    continue;
                }
                var difference = colours[index] - colours[other];
                var edge = Math.Max(Math.Abs(difference.X), Math.Max(Math.Abs(difference.Y), Math.Abs(difference.Z))) > 24;
                if (!edge)
                {
                    sky[other] = true;
                    pending.Enqueue(other);
                }
            }
        }
        return sky;
    }

    private static bool IsSkySeed(V3[] colours, bool[] eligible, int width, int column)
    {
        if (!eligible[column])
        {
            return false;
        }
        var colour = colours[column];
        var blue = colour.Z > colour.X * 1.06 && colour.Z >= colour.Y * .98 && colour.Y >= colour.X * .9;
        var bright = Math.Min(colour.X, Math.Min(colour.Y, colour.Z)) >= 185;
        if (!blue && !bright)
        {
            return false;
        }
        // A pale rock can touch the top border too. Require a smooth seed
        // neighbourhood, not just one bright pixel or one low-variance cell.
        for (var y = 0; y < Math.Min(3, colours.Length / width); y++)
        {
            for (var x = Math.Max(0, column - 2); x <= Math.Min(width - 1, column + 2); x++)
            {
                var index = y * width + x;
                var difference = colours[index] - colour;
                var textured = Math.Max(Math.Abs(difference.X), Math.Max(Math.Abs(difference.Y), Math.Abs(difference.Z))) > 18;
                if (!eligible[index] || textured)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static int[] Distances(bool[] seeds, int width, int limit, CancellationToken cancellation)
    {
        var distance = Enumerable.Repeat(int.MaxValue, seeds.Length).ToArray();
        var pending = new Queue<int>();
        for (var index = 0; index < seeds.Length; index++)
        {
            if (seeds[index])
            {
                distance[index] = 0;
                pending.Enqueue(index);
            }
        }
        Span<int> neighbours = stackalloc int[4];
        while (pending.TryDequeue(out var index))
        {
            cancellation.ThrowIfCancellationRequested();
            if (distance[index] >= limit)
            {
                continue;
            }
            var count = Neighbours(index, width, seeds.Length, neighbours);
            foreach (var other in neighbours[..count])
            {
                if (distance[other] == int.MaxValue)
                {
                    distance[other] = distance[index] + 1;
                    pending.Enqueue(other);
                }
            }
        }
        return distance;
    }

    private static int[] NearestSafe(bool[] safe, int width, int limit, CancellationToken cancellation)
    {
        var nearest = Enumerable.Repeat(-1, safe.Length).ToArray();
        var distance = new int[safe.Length];
        var pending = new Queue<int>();
        for (var index = 0; index < safe.Length; index++)
        {
            if (safe[index])
            {
                nearest[index] = index;
                pending.Enqueue(index);
            }
        }
        Span<int> neighbours = stackalloc int[4];
        while (pending.TryDequeue(out var index))
        {
            cancellation.ThrowIfCancellationRequested();
            if (distance[index] >= limit)
            {
                continue;
            }
            var count = Neighbours(index, width, safe.Length, neighbours);
            foreach (var other in neighbours[..count])
            {
                if (nearest[other] < 0)
                {
                    nearest[other] = nearest[index];
                    distance[other] = distance[index] + 1;
                    pending.Enqueue(other);
                }
            }
        }
        return nearest;
    }

    private static int Neighbours(int index, int width, int length, Span<int> neighbours)
    {
        var count = 0;
        if (index % width > 0)
        {
            neighbours[count++] = index - 1;
        }
        if (index % width < width - 1)
        {
            neighbours[count++] = index + 1;
        }
        if (index >= width)
        {
            neighbours[count++] = index - width;
        }
        if (index + width < length)
        {
            neighbours[count++] = index + width;
        }
        return count;
    }
}
