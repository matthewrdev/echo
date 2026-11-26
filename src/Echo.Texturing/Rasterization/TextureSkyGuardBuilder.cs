using Echo.Geometry;
using SkiaSharp;

namespace Echo.Texturing.Rasterization;

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
        var seedRows = Math.Min(3, rows);
        var colours = new V3[columns * seedRows];
        var eligible = new bool[colours.Length];
        ClassifyRows(pixels, width, height, swapRedBlue, cellSize, columns, colours, eligible, 0, seedRows, cancellation);
        var seeds = new Queue<int>();
        for (var column = 0; column < columns; column++)
        {
            if (IsSkySeed(colours, eligible, columns, column))
            {
                seeds.Enqueue(column);
            }
        }

        // A photo without a top-border seed cannot contribute to the mask.
        if (seeds.Count == 0)
        {
            return null;
        }

        Array.Resize(ref colours, columns * rows);
        Array.Resize(ref eligible, colours.Length);
        ClassifyRows(pixels, width, height, swapRedBlue, cellSize, columns, colours, eligible, seedRows, rows, cancellation);
        var sky = FloodSky(colours, eligible, columns, seeds, cancellation);
        if (sky.All(value => value))
        {
            return null;
        }
        // Include one analysis cell for bilinear footprints and mask rounding.
        var radius = (int)Math.Ceiling((double)marginPixels / cellSize) + 1;
        var firstSafeDistance = radius + 1;
        var distance = Distances(sky, columns, firstSafeDistance, cancellation);
        // Limit colour extension to a narrow band. Large projection errors or
        // invented geometry must not be concealed by unlimited edge stretching.
        var nearest = NearestSafe(distance, columns, firstSafeDistance, radius * 4, cancellation);
        return new TextureSkyGuard(columns, cellSize, nearest);
    }

    private static void ClassifyRows(ReadOnlySpan<SKColor> pixels, int width, int height, bool swapRedBlue,
        int cellSize, int columns, V3[] colours, bool[] eligible, int firstRow, int endRow, CancellationToken cancellation)
    {
        for (var row = firstRow; row < endRow; row++)
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
    }

    private static bool[] FloodSky(V3[] colours, bool[] eligible, int width, Queue<int> pending, CancellationToken cancellation)
    {
        var sky = new bool[colours.Length];
        foreach (var seed in pending)
        {
            sky[seed] = true;
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

    private static int[] NearestSafe(int[] fromSky, int width, int firstSafeDistance, int limit, CancellationToken cancellation)
    {
        var nearest = new int[fromSky.Length];
        var distance = new int[fromSky.Length];
        var pending = new Queue<int>();
        for (var index = 0; index < fromSky.Length; index++)
        {
            nearest[index] = fromSky[index] >= firstSafeDistance ? index : -1;
            // Only safe cells touching the unsafe band can extend into it.
            // Seed them in row order to keep equal-distance choices stable.
            if (fromSky[index] == firstSafeDistance)
            {
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

            var count = Neighbours(index, width, nearest.Length, neighbours);
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
