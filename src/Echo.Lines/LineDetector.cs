using System.Buffers;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Imaging;

namespace Echo.Lines;

[Export(typeof(ILineDetector)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class LineDetector : ILineDetector
{
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public LineDetector(IRasterImageService images)
    {
        this.images = images;
    }

    public List<LineFeature> Detect(float[] gray, int width, int height, int limit, double minimumLength, CancellationToken cancellation, LineDetectionOptions? options = null)
    {
        cancellation.ThrowIfCancellationRequested();
        options ??= new LineDetectionOptions();
        if (!double.IsFinite(options.GradientThreshold) || options.GradientThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The line gradient threshold must be finite and positive.");
        }
        var threshold = (float)options.GradientThreshold;
        var pixelCount = gray.Length;
        var strengthOffset = pixelCount * 2;
        var buffer = ArrayPool<float>.Shared.Rent(pixelCount * 3 + width * 3);
        bool[]? visited = null;
        try
        {
            visited = ArrayPool<bool>.Shared.Rent(pixelCount);
            visited.AsSpan(0, pixelCount).Clear();
            var gx = buffer.AsSpan(0, pixelCount);
            var gy = buffer.AsSpan(pixelCount, pixelCount);
            var strength = buffer.AsSpan(strengthOffset, pixelCount);
            var smooth = buffer.AsSpan(pixelCount * 3, width * 3);
            // Uncomputed border pixels stop region growth, even when this buffer
            // was last used by a larger image. All interior values are overwritten.
            strength.Clear();
            var seeds = new List<int>();
            var nextRow = 1;
            for (var y = 2; y < height - 2; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (; nextRow <= y + 1; nextRow++)
                {
                    SmoothRow(gray, width, nextRow, smooth.Slice(nextRow % 3 * width, width));
                }
                var previous = smooth.Slice((y - 1) % 3 * width, width);
                var current = smooth.Slice(y % 3 * width, width);
                var following = smooth.Slice((y + 1) % 3 * width, width);
                for (var x = 2; x < width - 2; x++)
                {
                    var i = y * width + x;
                    gx[i] = (current[x + 1] - current[x - 1]) * .5f;
                    gy[i] = (following[x] - previous[x]) * .5f;
                    strength[i] = MathF.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]);
                    if (strength[i] >= threshold)
                    {
                        seeds.Add(i);
                    }
                }
            }

            seeds.Sort((a, b) => buffer[strengthOffset + b].CompareTo(buffer[strengthOffset + a]));
            var region = new List<int>();
            var lines = new List<LineFeature>();
            foreach (var seed in seeds)
            {
                cancellation.ThrowIfCancellationRequested();
                if (visited[seed])
                {
                    continue;
                }
                region.Clear();
                region.Add(seed);
                visited[seed] = true;
                var nx = gx[seed] / strength[seed];
                var ny = gy[seed] / strength[seed];
                for (var cursor = 0; cursor < region.Count; cursor++)
                {
                    if (cursor % 1024 == 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                    }

                    var x = region[cursor] % width;
                    var y = region[cursor] / width;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var index = (y + dy) * width + x + dx;
                            if (visited[index] || strength[index] < threshold)
                            {
                                continue;
                            }
                            // Group adjacent edge pixels whose normals agree within 15 degrees.
                            if ((gx[index] * nx + gy[index] * ny) / strength[index] < .966)
                            {
                                continue;
                            }
                            visited[index] = true;
                            region.Add(index);
                        }
                    }
                }

                DetectSegments(CollectionsMarshal.AsSpan(region), strength, gray, width, height,
                    minimumLength, limit, options.SplitRegions, lines, 0, cancellation);
            }

            return lines;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
            if (visited != null)
            {
                ArrayPool<bool>.Shared.Return(visited);
            }
        }
    }

    private void RetainLine(List<LineFeature> lines, LineFeature candidate, int limit)
    {
        var low = 0;
        var high = lines.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (lines[middle].Strength.CompareTo(candidate.Strength) >= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        if (low >= limit)
        {
            return;
        }
        // Insert after equally strong lines to retain the original stable order.
        lines.Insert(low, candidate);
        if (lines.Count > limit)
        {
            lines.RemoveAt(limit);
        }
    }

    private void SmoothRow(ReadOnlySpan<float> gray, int width, int y, Span<float> target)
    {
        for (var x = 1; x < width - 1; x++)
        {
            var i = y * width + x;
            target[x] = (gray[i - width - 1] + 2 * gray[i - width] + gray[i - width + 1]
                + 2 * gray[i - 1] + 4 * gray[i] + 2 * gray[i + 1]
                + gray[i + width - 1] + 2 * gray[i + width] + gray[i + width + 1]) / 16;
        }
    }

    private void DetectSegments(Span<int> region, ReadOnlySpan<float> strength, float[] gray, int width, int height,
        double minimumLength, int limit, bool splitRegions, List<LineFeature> lines, int level, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (region.Length < minimumLength * .7)
        {
            return;
        }
        var fit = Fit(region, strength, width);
        var isStraight = fit.SquaredError / fit.Weight <= 2.25;
        var isSupported = fit.Length >= minimumLength && isStraight && region.Length / fit.Length >= .7;
        if (isSupported)
        {
            var line = new LineFeature
            {
                Start = [fit.X + fit.TangentX * fit.Minimum, fit.Y + fit.TangentY * fit.Minimum],
                End = [fit.X + fit.TangentX * fit.Maximum, fit.Y + fit.TangentY * fit.Maximum],
                Strength = fit.Length * fit.Weight / region.Length
            };
            line.Descriptor = Describe(gray, width, height, line);
            if (line.Descriptor.Length > 0)
            {
                RetainLine(lines, line, limit);
            }
            return;
        }
        var canSplit = splitRegions && level < 8 && region.Length >= minimumLength * 1.4;
        if (!canSplit)
        {
            return;
        }
        // A curved or branching region need not discard its straight subsegments.
        // Partition the existing pixel buffer; recursion owns disjoint spans.
        var middle = (fit.Minimum + fit.Maximum) * .5;
        var count = 0;
        for (var index = 0; index < region.Length; index++)
        {
            var pixel = region[index];
            var along = (pixel % width - fit.X) * fit.TangentX + (pixel / width - fit.Y) * fit.TangentY;
            if (along < middle)
            {
                var previous = region[count];
                region[count++] = pixel;
                region[index] = previous;
            }
        }
        if (count == 0 || count == region.Length)
        {
            return;
        }
        DetectSegments(region[..count], strength, gray, width, height, minimumLength, limit, splitRegions, lines, level + 1, cancellation);
        DetectSegments(region[count..], strength, gray, width, height, minimumLength, limit, splitRegions, lines, level + 1, cancellation);
    }

    private LineRegionFit Fit(ReadOnlySpan<int> region, ReadOnlySpan<float> strength, int width)
    {
        double x = 0;
        double y = 0;
        double weight = 0;
        foreach (var index in region)
        {
            var w = strength[index];
            x += index % width * w;
            y += index / width * w;
            weight += w;
        }
        x /= weight;
        y /= weight;
        double xx = 0;
        double xy = 0;
        double yy = 0;
        foreach (var index in region)
        {
            var dx = index % width - x;
            var dy = index / width - y;
            xx += dx * dx * strength[index];
            xy += dx * dy * strength[index];
            yy += dy * dy * strength[index];
        }
        var angle = .5 * Math.Atan2(2 * xy, xx - yy);
        var tx = Math.Cos(angle);
        var ty = Math.Sin(angle);
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        double squaredError = 0;
        foreach (var index in region)
        {
            var dx = index % width - x;
            var dy = index / width - y;
            var along = dx * tx + dy * ty;
            var across = dx * -ty + dy * tx;
            minimum = Math.Min(minimum, along);
            maximum = Math.Max(maximum, along);
            squaredError += across * across * strength[index];
        }
        return new LineRegionFit(x, y, tx, ty, minimum, maximum, squaredError, weight);
    }

    private float[] Describe(float[] gray, int width, int height, LineFeature line)
    {
        var dx = line.End[0] - line.Start[0];
        var dy = line.End[1] - line.Start[1];
        var length = Math.Sqrt(dx * dx + dy * dy);
        var descriptor = new float[128];
        for (var along = 0; along < 32; along++)
        {
            var t = (along + .5) / 32;
            for (var across = 0; across < 4; across++)
            {
                var offset = (across - 1.5) * 3;
                var x = line.Start[0] + t * dx - dy / length * offset;
                var y = line.Start[1] + t * dy + dx / length * offset;
                var sample = images.Sample(gray, width, height, x, y);
                if (!float.IsFinite(sample))
                {
                    return [];
                }
                descriptor[along * 4 + across] = sample;
            }
        }
        return Normalize(descriptor) ? descriptor : [];
    }

    public List<Feature> Junctions(IReadOnlyList<LineFeature> lines, float[] gray, int width, int height, int limit, CancellationToken cancellation)
    {
        var result = new List<Feature>();
        var directions = new LineDirection[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            directions[index] = new LineDirection(lines[index]);
        }
        for (var a = 0; a < lines.Count; a++)
        {
            cancellation.ThrowIfCancellationRequested();
            var first = lines[a];
            var ax = directions[a].X;
            var ay = directions[a].Y;
            var lengthA = directions[a].Length;
            for (var b = a + 1; b < lines.Count; b++)
            {
                var second = lines[b];
                var bx = directions[b].X;
                var by = directions[b].Y;
                var lengthB = directions[b].Length;
                var cross = ax * by - ay * bx;
                if (Math.Abs(cross) < .34 * lengthA * lengthB)
                {
                    continue;
                }
                var dx = second.Start[0] - first.Start[0];
                var dy = second.Start[1] - first.Start[1];
                var t = (dx * by - dy * bx) / cross;
                var u = (dx * ay - dy * ax) / cross;
                var onFirst = t >= -4 / lengthA && t <= 1 + 4 / lengthA;
                var onSecond = u >= -4 / lengthB && u <= 1 + 4 / lengthB;
                var onSegments = onFirst && onSecond;
                if (!onSegments)
                {
                    continue;
                }
                var x = first.Start[0] + t * ax;
                var y = first.Start[1] + t * ay;
                var insideWidth = x >= 9 && x < width - 9;
                var insideHeight = y >= 9 && y < height - 9;
                var inside = insideWidth && insideHeight;
                if (!inside || NearJunction(result, x, y))
                {
                    continue;
                }
                var descriptor = new float[64];
                var index = 0;
                for (var py = -7; py <= 7; py += 2)
                {
                    for (var px = -7; px <= 7; px += 2)
                    {
                        descriptor[index++] = images.Sample(gray, width, height, x + px, y + py);
                    }
                }
                if (Normalize(descriptor))
                {
                    result.Add(new Feature { X = x, Y = y, Kind = "junction", Descriptor = descriptor });
                }
                if (result.Count >= limit)
                {
                    return result;
                }
            }
        }
        return result;
    }

    private bool NearJunction(List<Feature> points, double x, double y)
    {
        foreach (var point in points)
        {
            if (Math.Abs(point.X - x) < 8 && Math.Abs(point.Y - y) < 8)
            {
                return true;
            }
        }
        return false;
    }

    private bool Normalize(Span<float> descriptor)
    {
        double sum = 0;
        foreach (var value in descriptor)
        {
            sum += value;
        }
        var mean = sum / descriptor.Length;
        double squared = 0;
        foreach (var value in descriptor)
        {
            squared += (value - mean) * (value - mean);
        }
        var norm = Math.Sqrt(squared);
        if (norm < .05)
        {
            return false;
        }
        for (var i = 0; i < descriptor.Length; i++)
        {
            descriptor[i] = (float)((descriptor[i] - mean) / norm);
        }
        return true;
    }
}
