using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

public static class SurfacePhotographCoverage
{
    public static PhotographCoverageReport Measure(Surface mesh, CancellationToken cancellation)
    {
        if (mesh.SourceImages.Count != mesh.Faces.Count)
        {
            throw new ArgumentException("Expected one photograph assignment per face.", nameof(mesh));
        }
        var area = 0.0;
        var inferredArea = 0.0;
        var inferredFaces = 0;
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = mesh.Faces[index];
            var a = new V3(mesh.Vertices[face[0]]);
            var b = new V3(mesh.Vertices[face[1]]);
            var c = new V3(mesh.Vertices[face[2]]);
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
