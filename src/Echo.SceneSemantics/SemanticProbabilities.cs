using Echo.Models;

namespace Echo.SceneSemantics;

public static class SemanticProbabilities
{
    public static SemanticMap Decode(string imageId, int width, int height, ReadOnlySpan<float> logits)
    {
        var count = checked(width * height);
        if (width <= 0 || height <= 0 || logits.Length != count * 150)
        {
            throw new ArgumentException("Invalid ADE20K logits.", nameof(logits));
        }

        var map = new SemanticMap { ImageId = imageId, Width = width, Height = height, Vegetation = new byte[count], Rock = new byte[count], Sky = new byte[count] };
        const int blockSize = 256;
        Span<float> maxima = stackalloc float[blockSize];
        Span<double> totals = stackalloc double[blockSize];
        Span<double> vegetation = stackalloc double[blockSize];
        Span<double> rock = stackalloc double[blockSize];
        for (var first = 0; first < count; first += blockSize)
        {
            var length = Math.Min(blockSize, count - first);
            maxima.Fill(float.NegativeInfinity);
            totals.Clear();
            vegetation.Clear();
            rock.Clear();
            // Read contiguous class slices while retaining the original label
            // order and double-precision accumulation for each individual pixel.
            for (var label = 0; label < 150; label++)
            {
                var values = logits.Slice(label * count + first, length);
                for (var pixel = 0; pixel < length; pixel++)
                {
                    maxima[pixel] = Math.Max(maxima[pixel], values[pixel]);
                }
            }

            for (var label = 0; label < 150; label++)
            {
                var values = logits.Slice(label * count + first, length);
                var isVegetation = label is 4 or 9 or 17 or 66 or 72;
                var isRock = label is 16 or 34;
                for (var pixel = 0; pixel < length; pixel++)
                {
                    var probability = Math.Exp(values[pixel] - maxima[pixel]);
                    totals[pixel] += probability;
                    if (isVegetation)
                    {
                        vegetation[pixel] += probability;
                    }

                    if (isRock)
                    {
                        rock[pixel] += probability;
                    }
                }
            }

            for (var pixel = 0; pixel < length; pixel++)
            {
                var total = totals[pixel];
                if (double.IsFinite(total) && total > 0)
                {
                    var destination = first + pixel;
                    map.Vegetation[destination] = (byte)Math.Round(255 * vegetation[pixel] / total);
                    map.Rock[destination] = (byte)Math.Round(255 * rock[pixel] / total);
                    map.Sky[destination] = (byte)Math.Round(255 * Math.Exp(logits[2 * count + destination] - maxima[pixel]) / total);
                }
            }
        }

        return map;
    }

    public static double ConservativeSample(SemanticMap map, IReadOnlyList<byte> probabilities, double u, double v)
    {
        var x = (int)Math.Floor(u * map.Width - .5);
        var y = (int)Math.Floor(v * map.Height - .5);
        var outside = !double.IsFinite(u) || !double.IsFinite(v)
            || x < 0 || y < 0 || x + 1 >= map.Width || y + 1 >= map.Height;
        if (outside)
        {
            return 0;
        }

        var first = y * map.Width + x;
        return Math.Min(Math.Min(probabilities[first], probabilities[first + 1]),
            Math.Min(probabilities[first + map.Width], probabilities[first + map.Width + 1])) / 255.0;
    }
}
