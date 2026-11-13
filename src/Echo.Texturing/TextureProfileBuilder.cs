using System.ComponentModel.Composition;
namespace Echo.Texturing;

[Export(typeof(ITextureProfileBuilder)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureProfileBuilder : ITextureProfileBuilder
{
    [ImportingConstructor]
    public TextureProfileBuilder()
    {
    }

    public TextureProfile Build(TexturePatch patch, double scale, int cellSize, int gutter, int quarterTurns)
    {
        var rotated = quarterTurns % 2 != 0;
        var width = rotated ? patch.Bounds.Height : patch.Bounds.Width;
        var height = rotated ? patch.Bounds.Width : patch.Bounds.Height;
        var padding = (int)Math.Ceiling((double)gutter / cellSize);
        var columns = Math.Max(1, (int)Math.Ceiling(width * scale / cellSize) + 1);
        var top = Enumerable.Repeat(int.MaxValue, columns).ToArray();
        var bottom = Enumerable.Repeat(-1, columns).ToArray();
        foreach (var face in patch.Triangles)
        {
            var points = face.Select(v =>
            {
                var point = patch.Coordinate(v, quarterTurns);
                return new SkiaSharp.SKPoint((float)(point.X * scale / cellSize), (float)(point.Y * scale / cellSize));
            }).ToArray();
            var left = Math.Clamp((int)Math.Floor(points.Min(p => p.X)), 0, columns - 1);
            var right = Math.Clamp((int)Math.Floor(points.Max(p => p.X)), 0, columns - 1);
            for (var x = left; x <= right; x++)
            {
                var low = double.PositiveInfinity;
                var high = double.NegativeInfinity;
                for (var edge = 0; edge < 3; edge++)
                {
                    var a = points[edge];
                    var b = points[(edge + 1) % 3];
                    if (Math.Max(a.X, b.X) < x || Math.Min(a.X, b.X) > x + 1)
                    {
                        continue;
                    }

                    var t0 = Math.Abs(b.X - a.X) < 1e-10 ? 0 : Math.Clamp((x - a.X) / (b.X - a.X), 0, 1);
                    var t1 = Math.Abs(b.X - a.X) < 1e-10 ? 1 : Math.Clamp((x + 1 - a.X) / (b.X - a.X), 0, 1);
                    var y0 = a.Y + (b.Y - a.Y) * t0;
                    var y1 = a.Y + (b.Y - a.Y) * t1;
                    low = Math.Min(low, Math.Min(y0, y1));
                    high = Math.Max(high, Math.Max(y0, y1));
                }

                if (double.IsFinite(low))
                {
                    top[x] = Math.Min(top[x], (int)Math.Floor(low));
                    bottom[x] = Math.Max(bottom[x], (int)Math.Floor(high) + 1);
                }
            }
        }

        var paddedTop = Enumerable.Repeat(int.MaxValue, columns + padding * 2).ToArray();
        var paddedBottom = Enumerable.Repeat(-1, paddedTop.Length).ToArray();
        for (var x = 0; x < columns; x++)
        {
            if (bottom[x] < 0)
            {
                continue;
            }

            for (var dx = -padding; dx <= padding; dx++)
            {
                var index = x + padding + dx;
                paddedTop[index] = Math.Min(paddedTop[index], top[x]);
                paddedBottom[index] = Math.Max(paddedBottom[index], bottom[x] + padding * 2);
            }
        }

        return new TextureProfile(paddedTop, paddedBottom, paddedBottom.Max(), quarterTurns);
    }
}
