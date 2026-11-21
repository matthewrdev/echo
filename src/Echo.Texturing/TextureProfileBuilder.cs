namespace Echo.Texturing;

public sealed class TextureProfileBuilder : ITextureProfileBuilder
{
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
        var top = new int[columns];
        Array.Fill(top, int.MaxValue);
        var bottom = new int[columns];
        Array.Fill(bottom, -1);
        Span<SkiaSharp.SKPoint> points = stackalloc SkiaSharp.SKPoint[3];
        foreach (var face in patch.Triangles)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var point = patch.Coordinate(face[corner], quarterTurns);
                points[corner] = new SkiaSharp.SKPoint((float)(point.X * scale / cellSize), (float)(point.Y * scale / cellSize));
            }

            var minimumX = Math.Min(points[0].X, Math.Min(points[1].X, points[2].X));
            var maximumX = Math.Max(points[0].X, Math.Max(points[1].X, points[2].X));
            var left = Math.Clamp((int)Math.Floor(minimumX), 0, columns - 1);
            var right = Math.Clamp((int)Math.Floor(maximumX), 0, columns - 1);
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

        var paddedTop = new int[columns + padding * 2];
        Array.Fill(paddedTop, int.MaxValue);
        var paddedBottom = new int[paddedTop.Length];
        Array.Fill(paddedBottom, -1);
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

        var maximum = -1;
        foreach (var value in paddedBottom)
        {
            maximum = Math.Max(maximum, value);
        }
        return new TextureProfile(paddedTop, paddedBottom, maximum, quarterTurns);
    }
}
