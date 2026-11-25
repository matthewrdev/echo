using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Meshing.Repair;
using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Topology;

public static class SurfaceTopology
{
    public static SurfaceTopologyReport Measure(Surface source, CancellationToken cancellation, WorkBuffers? work = null)
    {
        using var scratch = work?.CreateScope();
        var main = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, 0, cancellation, scratch).Surface;
        var mesh = Weld(main, cancellation, scratch);
        var report = new SurfaceTopologyReport { MainHullFaces = mesh.Faces.Count };
        var edges = new BinaryHashIndex<SurfaceEdge, BoundaryEdge>(mesh.Faces.Count,
            new SurfaceEdgeRecordCodec(), new BoundaryEdgeRecordCodec(), scratch, cancellation);
        foreach (var boundary in SurfaceBoundaryLoops.Find(mesh, cancellation, scratch, repairEdges: edges, topology: report))
        {
            report.Boundaries.Add(Describe(mesh, boundary));
        }

        return report;
    }

    private static Surface Weld(Surface source, CancellationToken cancellation, WorkBuffers? work)
    {
        var mesh = work?.Surface() ?? new Surface();
        var reader = new SurfaceGeometryReader(source);
        var writer = new SurfaceGeometryWriter(mesh);
        var positions = new BinaryHashIndex<V3, int>(source.Vertices.Count,
            new PositionKeyCodec(), new IntRecordCodec(), work, cancellation);
        IList<int> remap = work?.Filled(source.Vertices.Count, 0, new IntRecordCodec(), cancellation) ?? new int[source.Vertices.Count];
        for (var index = 0; index < source.Vertices.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = reader.Vertex(index);
            if (!positions.TryGetValue(point, out var mapped))
            {
                mapped = mesh.Vertices.Count;
                positions.Add(point, mapped);
                writer.AddVertex(point);
            }

            remap[index] = mapped;
        }

        for (var index = 0; index < source.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(index);
            writer.AddFace(new SurfaceTriangleVertices(remap[face.A], remap[face.B], remap[face.C]));
        }

        return mesh;
    }

    private static SurfaceBoundaryReport Describe(Surface mesh, SurfaceBoundaryLoop boundary)
    {
        var origin = new V3(mesh.Vertices[boundary.Vertices[0]]);
        var centre = new V3();
        var area = new V3();
        var perimeter = 0d;
        for (var index = 0; index < boundary.Vertices.Count; index++)
        {
            var point = new V3(mesh.Vertices[boundary.Vertices[index]]);
            var next = new V3(mesh.Vertices[boundary.Vertices[(index + 1) % boundary.Vertices.Count]]);
            centre += point;
            area += (point - origin).Cross(next - origin);
            perimeter += (next - point).Length;
        }

        centre /= boundary.Vertices.Count;
        var radius = boundary.Vertices.Max(vertex => (new V3(mesh.Vertices[vertex]) - centre).Length);
        return new SurfaceBoundaryReport(boundary.Vertices.Count, perimeter, radius, area.Dot(boundary.AdjacentNormal) < 0);
    }
}
