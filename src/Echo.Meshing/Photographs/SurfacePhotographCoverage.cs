using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Models;

namespace Echo.Meshing.Photographs;

public static class SurfacePhotographCoverage
{
    public static PhotographCoverageReport Measure(Surface mesh, CancellationToken cancellation)
    {
        if (mesh.SourceImages.Count != mesh.Faces.Count)
        {
            throw new ArgumentException("Expected one photograph assignment per face.", nameof(mesh));
        }
        var reader = new SurfaceGeometryReader(mesh);
        var area = 0.0;
        var inferredArea = 0.0;
        var inferredFaces = 0;
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(index);
            var a = reader.Vertex(face.A);
            var b = reader.Vertex(face.B);
            var c = reader.Vertex(face.C);
            var triangleArea = (b - a).Cross(c - a).Length * .5;
            area += triangleArea;
            if (string.IsNullOrEmpty(mesh.SourceImages[index]))
            {
                inferredFaces++;
                inferredArea += triangleArea;
            }
        }
        return new PhotographCoverageReport(mesh.Faces.Count, inferredFaces, area, inferredArea);
    }
}
