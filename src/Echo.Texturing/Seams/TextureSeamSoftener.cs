using Echo.Geometry;
using Echo.Models;
using Echo.Storage.Resources;
using Echo.Storage;
using SkiaSharp;

namespace Echo.Texturing.Seams;

public static class TextureSeamSoftener
{
    public static void Apply(Span<SKColor> pixels, int size, Surface mesh, IList<double[]> uvs,
        int width, CancellationToken cancellation, WorkBuffers? work = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (width is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (width == 0)
        {
            return;
        }

        var invalidLayout = size <= 0 || pixels.Length != (long)size * size
            || uvs.Count != mesh.Vertices.Count || mesh.SourceImages.Count != mesh.Faces.Count;
        if (invalidLayout)
        {
            throw new ArgumentException("Seam softening requires a square atlas and textured surface.");
        }

        var topology = SurfaceTopologyBuilder.Build(mesh, cancellation, work);
        SKColor[]? source = null;
        var changes = new Dictionary<int, TextureSeamPixelCorrection>();
        for (var face = 0; face < mesh.Faces.Count; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var edge = 0; edge < 3; edge++)
            {
                var other = topology.Neighbours[face * 3 + edge];
                if (other <= face || mesh.SourceImages[face] == mesh.SourceImages[other])
                {
                    continue;
                }

                var photographic = !string.IsNullOrEmpty(mesh.SourceImages[face]) && !string.IsNullOrEmpty(mesh.SourceImages[other]);
                if (!photographic)
                {
                    continue;
                }

                var indices = mesh.Faces[face];
                var a = indices[edge];
                var b = indices[(edge + 1) % 3];
                var others = mesh.Faces[other];
                var oa = Array.FindIndex(others, vertex => topology.WeldedVertices[vertex] == topology.WeldedVertices[a]);
                var ob = Array.FindIndex(others, vertex => topology.WeldedVertices[vertex] == topology.WeldedVertices[b]);
                if (oa < 0 || ob < 0 || oa == ob)
                {
                    continue;
                }

                source ??= pixels.ToArray();
                var first = new TextureSeamTriangle(Coordinate(uvs[a], size), Coordinate(uvs[b], size), Coordinate(uvs[indices[(edge + 2) % 3]], size));
                var second = new TextureSeamTriangle(Coordinate(uvs[others[oa]], size), Coordinate(uvs[others[ob]], size), Coordinate(uvs[others[3 - oa - ob]], size));
                AccumulateStrip(source, size, first, second, width, changes, cancellation);
                AccumulateStrip(source, size, second, first, width, changes, cancellation);
            }
        }
        // Only a narrow correction field is feathered. The original photographic
        // detail remains in every texel; chart interiors and UVs are untouched.
        foreach (var entry in changes)
        {
            cancellation.ThrowIfCancellationRequested();
            var colour = source![entry.Key];
            var delta = entry.Value.Sum / Math.Max(1, entry.Value.Weight);
            pixels[entry.Key] = new SKColor(Channel(colour.Red + delta.X), Channel(colour.Green + delta.Y),
                Channel(colour.Blue + delta.Z), colour.Alpha);
        }
    }

    private static void AccumulateStrip(SKColor[] source, int size, TextureSeamTriangle own, TextureSeamTriangle other,
        int width, Dictionary<int, TextureSeamPixelCorrection> changes, CancellationToken cancellation)
    {
        var edge = own.B - own.A;
        var length = edge.Length;
        if (length < 1e-8)
        {
            return;
        }

        var horizontal = Math.Abs(edge.X) >= Math.Abs(edge.Y);
        var start = horizontal ? own.A.X : own.A.Y;
        var end = horizontal ? own.B.X : own.B.Y;
        var low = Math.Max(0, (int)Math.Floor(Math.Min(start, end)) - width);
        var high = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(start, end)) + width);
        for (var along = low; along <= high; along++)
        {
            cancellation.ThrowIfCancellationRequested();
            var fraction = (along + .5 - start) / (end - start);
            var across = horizontal ? own.A.Y + edge.Y * fraction : own.A.X + edge.X * fraction;
            var from = Math.Max(0, (int)Math.Floor(across) - width * 2);
            var to = Math.Min(size - 1, (int)Math.Ceiling(across) + width * 2);
            for (var cross = from; cross <= to; cross++)
            {
                var x = horizontal ? along : cross;
                var y = horizontal ? cross : along;
                var point = new V3(x + .5, y + .5, 0);
                if (!own.Contains(point))
                {
                    continue;
                }

                var t = Math.Clamp((point - own.A).Dot(edge) / (length * length), 0, 1);
                var distance = (point - own.A - edge * t).Length;
                if (distance >= width || source[y * size + x].Alpha == 0)
                {
                    continue;
                }

                if (!Sample(source, size, own.InsideEdge(t), out var a) || !Sample(source, size, other.InsideEdge(t), out var b))
                {
                    continue;
                }

                var difference = (b - a) * .5;
                difference = new V3(Math.Clamp(difference.X, -32, 32), Math.Clamp(difference.Y, -32, 32), Math.Clamp(difference.Z, -32, 32));
                if (difference.Length < .5)
                {
                    continue;
                }

                var remaining = 1 - distance / width;
                var weight = remaining * remaining * (3 - 2 * remaining);
                var index = y * size + x;
                var previous = changes.GetValueOrDefault(index);
                changes[index] = new TextureSeamPixelCorrection(previous.Sum + difference * weight, previous.Weight + weight);
            }
        }
    }

    private static bool Sample(SKColor[] source, int size, V3 point, out V3 colour)
    {
        var px = Math.Clamp(point.X - .5, 0, size - 1);
        var py = Math.Clamp(point.Y - .5, 0, size - 1);
        var x = (int)px;
        var y = (int)py;
        var total = 0.0;
        colour = default;
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var pixel = source[Math.Min(size - 1, y + row) * size + Math.Min(size - 1, x + column)];
                if (pixel.Alpha == 0)
                {
                    continue;
                }

                var weight = (column == 0 ? 1 - px + x : px - x) * (row == 0 ? 1 - py + y : py - y);
                colour += new V3(pixel.Red, pixel.Green, pixel.Blue) * weight;
                total += weight;
            }
        }

        if (total < .5)
        {
            return false;
        }

        colour /= total;
        return true;
    }

    private static V3 Coordinate(double[] uv, int size) => new(uv[0] * size, uv[1] * size, 0);
    private static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
