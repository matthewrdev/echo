using Echo.Texturing.Charts;
namespace Echo.Texturing.Layout;

internal static class TextureMaskBuilder
{
    public static TextureMask Build(TexturePatch patch, double scale, int cellSize, int gutter,
        int quarterTurns, CancellationToken cancellation)
    {
        var rotated = quarterTurns % 2 != 0;
        var padding = (int)Math.Ceiling((double)gutter / cellSize);
        var width = (int)Math.Ceiling((rotated ? patch.Bounds.Height : patch.Bounds.Width) * scale / cellSize) + 1;
        var height = (int)Math.Ceiling((rotated ? patch.Bounds.Width : patch.Bounds.Height) * scale / cellSize) + 1;
        var rows = new List<TextureMaskSpan>[height + padding * 2];
        for (var y = 0; y < rows.Length; y++)
        {
            rows[y] = [];
        }

        Span<TextureCoordinate> points = stackalloc TextureCoordinate[3];
        foreach (var face in patch.Triangles)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var corner = 0; corner < 3; corner++)
            {
                var point = patch.Coordinate(face[corner], quarterTurns);
                points[corner] = new TextureCoordinate(point.X * scale / cellSize, point.Y * scale / cellSize);
            }

            Rasterize(points, rows, width, height, padding);
        }

        // Preserve separate runs on each row: collapsing them to an envelope would
        // lose the very cavities that small islands need to reuse.
        var spans = new List<TextureMaskSpan>();
        for (var y = 0; y < rows.Length; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            var row = rows[y];
            row.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (var index = 0; index < row.Count; index++)
            {
                var start = row[index].Start;
                var end = row[index].End;
                while (index + 1 < row.Count && row[index + 1].Start <= end)
                {
                    end = Math.Max(end, row[++index].End);
                }

                spans.Add(new TextureMaskSpan(y, start, end));
            }
        }

        // Wide runs usually reject colliding placements earliest on both backends.
        return new TextureMask(width + padding * 2, rows.Length,
            spans.OrderByDescending(span => span.End - span.Start).ThenBy(span => span.Y).ThenBy(span => span.Start).ToArray(), quarterTurns);
    }

    private static void Rasterize(ReadOnlySpan<TextureCoordinate> points, List<TextureMaskSpan>[] rows,
        int width, int height, int padding)
    {
        var top = Math.Clamp((int)Math.Floor(Math.Min(points[0].Y, Math.Min(points[1].Y, points[2].Y))), 0, height - 1);
        var bottom = Math.Clamp((int)Math.Floor(Math.Max(points[0].Y, Math.Max(points[1].Y, points[2].Y))), 0, height - 1);
        for (var y = top; y <= bottom; y++)
        {
            var left = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            for (var edge = 0; edge < 3; edge++)
            {
                var a = points[edge];
                var b = points[(edge + 1) % 3];
                if (Math.Max(a.Y, b.Y) < y || Math.Min(a.Y, b.Y) > y + 1)
                {
                    continue;
                }

                var t0 = Math.Abs(b.Y - a.Y) < 1e-12 ? 0 : Math.Clamp((y - a.Y) / (b.Y - a.Y), 0, 1);
                var t1 = Math.Abs(b.Y - a.Y) < 1e-12 ? 1 : Math.Clamp((y + 1 - a.Y) / (b.Y - a.Y), 0, 1);
                var x0 = a.X + (b.X - a.X) * t0;
                var x1 = a.X + (b.X - a.X) * t1;
                left = Math.Min(left, Math.Min(x0, x1));
                right = Math.Max(right, Math.Max(x0, x1));
            }

            if (!double.IsFinite(left))
            {
                continue;
            }

            // Cover every cell touched by a triangle, then dilate in both axes.
            // The chart origin is padding cells in from this mask's origin.
            var start = Math.Clamp((int)Math.Floor(left), 0, width - 1);
            var end = Math.Clamp((int)Math.Floor(right), 0, width - 1) + 1 + padding * 2;
            for (var row = y; row <= y + padding * 2; row++)
            {
                rows[row].Add(new TextureMaskSpan(row, start, end));
            }
        }
    }
}
