using Echo.Geometry;
using Echo.Models;

namespace Echo.Pipeline.Exporting;

internal static class GuideTileGeometry
{
    public static bool BelongsTo(Surface surface, int face, GuideTileRegion region, IEnumerable<int> steps)
    {
        foreach (var step in steps)
        {
            if (Quadrant(surface, face, region) != step)
            {
                return false;
            }

            region = region.Child(step);
        }

        return true;
    }

    public static int Quadrant(Surface surface, int face, GuideTileRegion region)
    {
        var triangle = surface.Faces[face];
        var a = surface.Vertices[triangle[0]];
        var b = surface.Vertices[triangle[1]];
        var c = surface.Vertices[triangle[2]];
        return region.Quadrant((a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3);
    }

    public static Surface Extract(Surface source, IEnumerable<int> faces, IList<double[]>? uvs = null,
        List<double[]>? outputUvs = null)
    {
        var tile = new Surface();
        var vertices = new Dictionary<int, int>();
        var assignedPhotos = source.SourceImages.Count == source.Faces.Count;
        foreach (var face in faces)
        {
            var triangle = new int[3];
            for (var corner = 0; corner < triangle.Length; corner++)
            {
                var original = source.Faces[face][corner];
                if (!vertices.TryGetValue(original, out var local))
                {
                    local = tile.Vertices.Count;
                    vertices.Add(original, local);
                    tile.Vertices.Add(source.Vertices[original]);
                    if (uvs != null)
                    {
                        outputUvs!.Add(uvs[original]);
                    }
                }

                triangle[corner] = local;
            }

            tile.Faces.Add(triangle);
            if (assignedPhotos)
            {
                tile.SourceImages.Add(source.SourceImages[face]);
            }
        }

        return tile;
    }

    public static bool[] BoundaryVertices(Surface surface)
    {
        var edges = new Dictionary<ulong, int>();
        foreach (var triangle in surface.Faces)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var first = triangle[corner];
                var second = triangle[(corner + 1) % 3];
                var edge = ((ulong)(uint)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
            }
        }

        var locked = new bool[surface.Vertices.Count];
        foreach (var entry in edges)
        {
            if (entry.Value != 2)
            {
                locked[(int)(entry.Key >> 32)] = true;
                locked[(int)(uint)entry.Key] = true;
            }
        }

        return locked;
    }

    public static List<List<int>> ReductionGroups(Surface surface, int faceLimit, CancellationToken cancellation)
    {
        var centres = new V3[surface.Faces.Count];
        for (var index = 0; index < centres.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = surface.Faces[index];
            centres[index] = (new V3(surface.Vertices[face[0]]) + new V3(surface.Vertices[face[1]]) +
                new V3(surface.Vertices[face[2]])) / 3;
        }

        var groups = new List<List<int>>();
        Split(Enumerable.Range(0, centres.Length).ToList());
        return groups;

        void Split(List<int> indices)
        {
            cancellation.ThrowIfCancellationRequested();
            if (indices.Count <= faceLimit)
            {
                groups.Add(indices);
                return;
            }

            var minimum = centres[indices[0]];
            var maximum = minimum;
            foreach (var index in indices)
            {
                var centre = centres[index];
                minimum = new V3(Math.Min(minimum.X, centre.X), Math.Min(minimum.Y, centre.Y), Math.Min(minimum.Z, centre.Z));
                maximum = new V3(Math.Max(maximum.X, centre.X), Math.Max(maximum.Y, centre.Y), Math.Max(maximum.Z, centre.Z));
            }

            var span = maximum - minimum;
            var axis = span.X >= span.Y && span.X >= span.Z ? 0 : span.Y >= span.Z ? 1 : 2;
            var middle = At(minimum, axis) + At(span, axis) * .5;
            var low = new List<int>();
            var high = new List<int>();
            foreach (var index in indices)
            {
                var group = At(centres[index], axis) < middle ? low : high;
                group.Add(index);
            }

            // Coincident centroids cannot be usefully separated spatially.
            if (low.Count == 0 || high.Count == 0)
            {
                var half = indices.Count / 2;
                low = indices.GetRange(0, half);
                high = indices.GetRange(half, indices.Count - half);
            }

            Split(low);
            Split(high);
        }

        static double At(V3 point, int axis) => axis switch
        {
            0 => point.X,
            1 => point.Y,
            _ => point.Z
        };
    }

    public static double[][] Bounds(Surface surface)
    {
        var minimum = (double[])surface.Vertices[0].Clone();
        var maximum = (double[])minimum.Clone();
        foreach (var vertex in surface.Vertices)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                minimum[axis] = Math.Min(minimum[axis], vertex[axis]);
                maximum[axis] = Math.Max(maximum[axis], vertex[axis]);
            }
        }

        return [minimum, maximum];
    }
}
