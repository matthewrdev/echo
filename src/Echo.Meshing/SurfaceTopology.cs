using Echo.Models;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

public static class SurfaceTopology
{
    public static SurfaceTopologyReport Measure(Surface source, CancellationToken cancellation, WorkBuffers? work = null)
    {
        using var scratch = work?.CreateScope();
        var main = SurfaceIslandTrimmer.Trim(source, IslandTrimMode.Main, 0, cancellation, scratch).Surface;
        var mesh = Weld(main, cancellation, scratch);
        var report = new SurfaceTopologyReport { MainHullFaces = mesh.Faces.Count };
        var reader = new SurfaceGeometryReader(mesh);
        var edges = new BinaryHashIndex<SurfaceEdge, BoundaryEdge>(mesh.Faces.Count,
            new SurfaceEdgeRecordCodec(), new BoundaryEdgeRecordCodec(), scratch, cancellation);
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(index);
            var origin = reader.Vertex(face.A);
            var area = (reader.Vertex(face.B) - origin).Cross(reader.Vertex(face.C) - origin).Length;
            if (!double.IsFinite(area) || area <= 0)
            {
                report.DegenerateFaces++;
            }
            for (var corner = 0; corner < 3; corner++)
            {
                var from = face[corner];
                var to = face[(corner + 1) % 3];
                var key = new SurfaceEdge(Math.Min(from, to), Math.Max(from, to));
                if (edges.TryGetValue(key, out var existing))
                {
                    if (existing.Count == 1 && existing.From == from)
                    {
                        report.WindingConflicts++;
                    }
                    edges[key] = existing with
                    {
                        Count = existing.Count + 1
                    };
                }
                else
                {
                    edges.Add(key, new BoundaryEdge(from, to, default, 1));
                }
            }
        }
        foreach (var edge in edges.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            if (edge.Count == 1)
            {
                report.BoundaryEdges++;
            }
            else if (edge.Count > 2)
            {
                report.NonManifoldEdges++;
            }
        }
        if (report.BoundaryEdges > 0)
        {
            foreach (var boundary in SurfaceBoundaryLoops.Find(mesh, cancellation, scratch))
            {
                report.Boundaries.Add(Describe(mesh, boundary));
            }
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
