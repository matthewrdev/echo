using Echo.Contracts;

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
        var map = new SemanticMap { ImageId = imageId, Width = width, Height = height, Vegetation = new byte[count], Rock = new byte[count] };
        for (var pixel = 0; pixel < count; pixel++)
        {
            var maximum = float.NegativeInfinity;
            for (var label = 0; label < 150; label++)
            {
                maximum = Math.Max(maximum, logits[label * count + pixel]);
            }
            double total = 0;
            double vegetation = 0;
            double rock = 0;
            for (var label = 0; label < 150; label++)
            {
                var probability = Math.Exp(logits[label * count + pixel] - maximum);
                total += probability;
                // ADE20K tree, grass, plant, flower and palm; earth/field are
                // deliberately excluded because they can contain exposed rock.
                if (label is 4 or 9 or 17 or 66 or 72)
                {
                    vegetation += probability;
                }
                if (label is 16 or 34)
                {
                    rock += probability;
                }
            }
            if (double.IsFinite(total) && total > 0)
            {
                map.Vegetation[pixel] = (byte)Math.Round(255 * vegetation / total);
                map.Rock[pixel] = (byte)Math.Round(255 * rock / total);
            }
        }
        return map;
    }

    public static double ConservativeSample(SemanticMap map, byte[] probabilities, double u, double v)
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
