using System.ComponentModel.Composition;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITextureChartOrienter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureChartOrienter : ITextureChartOrienter
{
    [ImportingConstructor]
    public TextureChartOrienter()
    {
    }

    public void Orient(TexturePatch patch)
    {
        var points = patch.Coordinates.Values.Select(p => new SKPoint((float)p[0], (float)p[1])).Distinct()
            .OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var hull = Hull(points);
        var bestArea = double.PositiveInfinity;
        var cosine = 1.0;
        var sine = 0.0;
        for (var edge = 0; edge < hull.Count; edge++)
        {
            var start = hull[edge];
            var end = hull[(edge + 1) % hull.Count];
            var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var width = hull.Max(p => p.X * c + p.Y * s) - hull.Min(p => p.X * c + p.Y * s);
            var height = hull.Max(p => -p.X * s + p.Y * c) - hull.Min(p => -p.X * s + p.Y * c);
            if (width * height < bestArea)
            {
                bestArea = width * height;
                cosine = c;
                sine = s;
            }
        }

        foreach (var key in patch.Coordinates.Keys.ToArray())
        {
            var point = patch.Coordinates[key];
            patch.Coordinates[key] = [point[0] * cosine + point[1] * sine, -point[0] * sine + point[1] * cosine];
        }

        var values = patch.Coordinates.Values;
        patch.Bounds = new SKRect((float)values.Min(p => p[0]), (float)values.Min(p => p[1]),
            (float)values.Max(p => p[0]), (float)values.Max(p => p[1]));
    }

    private List<SKPoint> Hull(SKPoint[] points)
    {
        var lower = new List<SKPoint>();
        var upper = new List<SKPoint>();
        foreach (var point in points)
        {
            Append(lower, point);
        }

        foreach (var point in points.Reverse())
        {
            Append(upper, point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private void Append(List<SKPoint> hull, SKPoint point)
    {
        while (hull.Count >= 2)
        {
            var a = hull[^2];
            var b = hull[^1];
            var cross = (double)(b.X - a.X) * (point.Y - a.Y) - (double)(b.Y - a.Y) * (point.X - a.X);
            if (cross > 0)
            {
                break;
            }

            hull.RemoveAt(hull.Count - 1);
        }

        hull.Add(point);
    }
}
