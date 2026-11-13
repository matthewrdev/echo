using System.ComponentModel.Composition;
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

    public List<LineFeature> Detect(float[] gray, int width, int height, int limit, double minimumLength, CancellationToken cancellation)
    {
        var smooth = new float[gray.Length];
        var gx = new float[gray.Length];
        var gy = new float[gray.Length];
        var strength = new float[gray.Length];
        var seeds = new List<int>();
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                smooth[i] = (gray[i - width - 1] + 2 * gray[i - width] + gray[i - width + 1]
                    + 2 * gray[i - 1] + 4 * gray[i] + 2 * gray[i + 1]
                    + gray[i + width - 1] + 2 * gray[i + width] + gray[i + width + 1]) / 16;
            }
        }

        for (var y = 2; y < height - 2; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 2; x < width - 2; x++)
            {
                var i = y * width + x;
                gx[i] = (smooth[i + 1] - smooth[i - 1]) * .5f;
                gy[i] = (smooth[i + width] - smooth[i - width]) * .5f;
                strength[i] = MathF.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]);
                if (strength[i] >= .025f)
                {
                    seeds.Add(i);
                }
            }
        }

        seeds.Sort((a, b) => strength[b].CompareTo(strength[a]));
        var visited = new bool[gray.Length];
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
                        if (visited[index] || strength[index] < .025f)
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

            var line = Fit(region, strength, width, minimumLength);
            if (line == null)
            {
                continue;
            }
            line.Descriptor = Describe(gray, width, height, line);
            if (line.Descriptor.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines.OrderByDescending(line => line.Strength).Take(limit).ToList();
    }

    private LineFeature? Fit(List<int> region, float[] strength, int width, double minimumLength)
    {
        if (region.Count < minimumLength * .7)
        {
            return null;
        }
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
        var length = maximum - minimum;
        var isStraight = squaredError / weight <= 2.25;
        var isSupported = length >= minimumLength && isStraight && region.Count / length >= .7;
        if (!isSupported)
        {
            return null;
        }
        return new LineFeature
        {
            Start = [x + tx * minimum, y + ty * minimum],
            End = [x + tx * maximum, y + ty * maximum],
            Strength = length * weight / region.Count
        };
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
        for (var a = 0; a < lines.Count; a++)
        {
            cancellation.ThrowIfCancellationRequested();
            var first = lines[a];
            var ax = first.End[0] - first.Start[0];
            var ay = first.End[1] - first.Start[1];
            var lengthA = Math.Sqrt(ax * ax + ay * ay);
            for (var b = a + 1; b < lines.Count; b++)
            {
                var second = lines[b];
                var bx = second.End[0] - second.Start[0];
                var by = second.End[1] - second.Start[1];
                var lengthB = Math.Sqrt(bx * bx + by * by);
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
                if (!inside || result.Any(point => Math.Abs(point.X - x) < 8 && Math.Abs(point.Y - y) < 8))
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
