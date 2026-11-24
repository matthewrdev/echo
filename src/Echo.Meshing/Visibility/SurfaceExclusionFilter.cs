using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Models;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Visibility;

public static class SurfaceExclusionFilter
{
    public static Surface Filter(Surface surface, IReadOnlyList<ExclusionRegion> regions,
        CancellationToken cancellation, WorkBuffers? work = null) =>
        Filter(surface, new RegionCropFilter(regions), cancellation, work);

    public static Surface Filter(Surface surface, RegionCropFilter crop,
        CancellationToken cancellation, WorkBuffers? work = null)
    {
        if (!crop.HasRegions)
        {
            return surface;
        }

        using var scratch = work?.CreateScope();
        IList<int> vertices = scratch?.Filled(surface.Vertices.Count, -1, new IntRecordCodec(), cancellation)
            ?? Enumerable.Repeat(-1, surface.Vertices.Count).ToArray();
        var result = work?.Surface() ?? new Surface();
        var writer = new SurfaceGeometryWriter(result);
        Span<int> output = stackalloc int[3];
        var reader = new SurfaceGeometryReader(surface);
        var assigned = surface.SourceImages.Count == surface.Faces.Count;
        for (var index = 0; index < surface.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(index);
            var a = reader.Vertex(face.A);
            var b = reader.Vertex(face.B);
            var c = reader.Vertex(face.C);
            if (crop.ExcludesTriangle(a, b, c))
            {
                continue;
            }

            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                if (vertices[vertex] < 0)
                {
                    vertices[vertex] = result.Vertices.Count;
                    writer.AddVertex(reader.Vertex(vertex));
                }

                output[corner] = vertices[vertex];
            }

            writer.AddFace(new SurfaceTriangleVertices(output[0], output[1], output[2]));
            if (assigned)
            {
                result.SourceImages.Add(surface.SourceImages[index]);
            }
        }

        return result;
    }
}
