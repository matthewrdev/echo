using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

public static class SurfaceExclusionFilter
{
    public static Surface Filter(Surface surface, IReadOnlyList<ExclusionRegion> regions,
        CancellationToken cancellation, WorkBuffers? work = null)
    {
        var enabled = regions.Where(region => region.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            return surface;
        }
        using var scratch = work?.CreateScope();
        IList<int> vertices = scratch?.Filled(surface.Vertices.Count, -1, new IntRecordCodec(), cancellation)
            ?? Enumerable.Repeat(-1, surface.Vertices.Count).ToArray();
        var result = work?.Surface() ?? new Surface();
        var assigned = surface.SourceImages.Count == surface.Faces.Count;
        for (var index = 0; index < surface.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = surface.Faces[index];
            var a = new V3(surface.Vertices[face[0]]);
            var b = new V3(surface.Vertices[face[1]]);
            var c = new V3(surface.Vertices[face[2]]);
            if (ExclusionGeometry.Intersects(enabled, a, b, c))
            {
                continue;
            }
            var output = new int[3];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                if (vertices[vertex] < 0)
                {
                    vertices[vertex] = result.Vertices.Count;
                    result.Vertices.Add(surface.Vertices[vertex]);
                }
                output[corner] = vertices[vertex];
            }
            result.Faces.Add(output);
            if (assigned)
            {
                result.SourceImages.Add(surface.SourceImages[index]);
            }
        }
        return result;
    }
}
