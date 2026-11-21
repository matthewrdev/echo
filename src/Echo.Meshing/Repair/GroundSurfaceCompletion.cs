using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Meshing.Volumes;
using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Repair;

internal static class GroundSurfaceCompletion
{
    public static void Fill(Surface mesh, double voxelSize, Func<V3, bool> observed, CancellationToken cancellation, WorkBuffers? work = null)
    {
        if (mesh.Vertices.Count == 0)
        {
            return;
        }
        using var scratch = work?.CreateScope(preferResident: true);
        var reader = new SurfaceGeometryReader(mesh);
        IList<V3> points = scratch?.List(new V3RecordCodec()) ?? new List<V3>(mesh.Vertices.Count);
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        for (var vertex = 0; vertex < mesh.Vertices.Count; vertex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = reader.Vertex(vertex);
            points.Add(point);
            minimum = Math.Min(minimum, point.Y);
            maximum = Math.Max(maximum, point.Y);
        }
        // Reconstruction coordinates are East/Up/South. Restrict stitching to
        // upward-facing terrain in the upper part of the scan, not cliff walls.
        var floor = minimum + (maximum - minimum) * .6;
        var span = voxelSize * 8;
        var edges = new BinaryHashIndex<SurfaceEdge, BoundaryEdge>(mesh.Faces.Count, new SurfaceEdgeRecordCodec(), new BoundaryEdgeRecordCodec(), scratch, cancellation);
        var groundFaces = new Dictionary<Grid, List<int>>();
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = mesh.Faces[index];
            var a = points[face[0]];
            var b = points[face[1]];
            var c = points[face[2]];
            var normal = (b - a).Cross(c - a).Unit;
            AddEdges(edges, face, normal);
            if (Math.Abs(normal.Y) > .35 && Math.Max(a.Y, Math.Max(b.Y, c.Y)) >= floor)
            {
                IndexFace(groundFaces, index, a, b, c, span);
            }
        }
        var boundaryVertices = new HashSet<int>();
        var pending = new Queue<SurfaceEdge>();
        var incoming = new Dictionary<int, List<int>>();
        foreach (var entry in edges)
        {
            var edge = entry.Value;
            var groundBoundary = edge.Count == 1 && edge.Normal.Y >= .65
                && Math.Min(points[edge.From].Y, points[edge.To].Y) >= floor;
            if (groundBoundary)
            {
                boundaryVertices.Add(edge.From);
                boundaryVertices.Add(edge.To);
                pending.Enqueue(entry.Key);
                AddIncoming(incoming, edge.From, edge.To);
            }
        }
        var vertexCells = new Dictionary<Grid, List<int>>();
        foreach (var vertex in boundaryVertices)
        {
            Add(vertexCells, Cell(points[vertex], span), vertex);
        }
        var visited = new HashSet<SurfaceEdge>();
        var candidates = new List<GroundCandidate>();
        var nearbyFaces = new HashSet<int>();
        Span<V3> triangle = stackalloc V3[3];
        while (pending.TryDequeue(out var key))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!visited.Add(key) || edges[key].Count != 1)
            {
                continue;
            }
            var edge = edges[key];
            var a = points[edge.To];
            var b = points[edge.From];
            var length = (b - a).Length;
            if (length > span || length < voxelSize * 1e-6)
            {
                continue;
            }
            candidates.Clear();
            var centre = Cell((a + b) * .5, span);
            for (var z = -1; z <= 1; z++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    if (!vertexCells.TryGetValue(centre.Add(x, 0, z), out var vertices))
                    {
                        continue;
                    }
                    foreach (var vertex in vertices)
                    {
                        if (vertex == edge.From || vertex == edge.To)
                        {
                            continue;
                        }
                        var c = points[vertex];
                        var ac = (c - a).Length;
                        var bc = (c - b).Length;
                        var normal = (b - a).Cross(c - a);
                        var suitable = ac <= span && bc <= span && normal.Unit.Y >= .8
                            && normal.Length > Math.Max(length * ac, Math.Max(length * bc, ac * bc)) * .1;
                        if (suitable)
                        {
                            candidates.Add(new GroundCandidate(vertex, length * ac * bc / (2 * normal.Length)));
                        }
                    }
                }
            }
            candidates.Sort((first, second) => first.Radius.CompareTo(second.Radius));
            foreach (var candidate in candidates)
            {
                var vertex = candidate.Vertex;
                if (!Available(edges, edge.From, vertex) || !Available(edges, vertex, edge.To))
                {
                    continue;
                }
                var c = points[vertex];
                triangle[0] = a;
                triangle[1] = b;
                triangle[2] = c;
                NearbyFaces(groundFaces, triangle, span, nearbyFaces);
                if (!Empty(mesh, points, triangle, edge.To, edge.From, vertex, nearbyFaces))
                {
                    continue;
                }
                var supported = observed((a + b + c) / 3) && observed((a + c) * .5) && observed((b + c) * .5);
                if (!supported)
                {
                    continue;
                }
                var face = new[] { edge.To, edge.From, vertex };
                int[]? bridge = null;
                var joinsAtVertexOnly = !edges.ContainsKey(Key(edge.From, vertex)) && !edges.ContainsKey(Key(vertex, edge.To));
                if (joinsAtVertexOnly)
                {
                    // Joining an edge to an isolated boundary vertex creates a
                    // pinched, non-manifold vertex. Bridge two complete boundary
                    // edges atomically instead, with a second matching triangle.
                    bridge = Bridge(mesh, points, edges, incoming, groundFaces, nearbyFaces,
                        edge.To, edge.From, vertex, span, observed);
                    if (bridge == null)
                    {
                        continue;
                    }
                }
                var index = mesh.Faces.Count;
                mesh.Faces.Add(face);
                AddEdges(edges, face, (b - a).Cross(c - a).Unit);
                IndexFace(groundFaces, index, a, b, c, span);
                if (bridge != null)
                {
                    var d = points[bridge[2]];
                    IndexFace(groundFaces, mesh.Faces.Count, a, c, d, span);
                    mesh.Faces.Add(bridge);
                    AddEdges(edges, bridge, (c - a).Cross(d - a).Unit);
                    QueueBoundary(edges, bridge, pending, incoming);
                }
                QueueBoundary(edges, face, pending, incoming);
                break;
            }
        }
    }

    private static int[]? Bridge(Surface mesh, IList<V3> points, BinaryHashIndex<SurfaceEdge, BoundaryEdge> edges,
        Dictionary<int, List<int>> incoming, Dictionary<Grid, List<int>> groundFaces, HashSet<int> nearby,
        int a, int b, int c, double span, Func<V3, bool> observed)
    {
        if (!incoming.TryGetValue(c, out var predecessors))
        {
            return null;
        }
        ReadOnlySpan<V3> first = [points[a], points[b], points[c]];
        Span<V3> second = stackalloc V3[3];
        foreach (var d in predecessors)
        {
            var distinct = d != a && d != b && edges[Key(d, c)].Count == 1;
            if (!distinct || !Available(edges, c, d) || !Available(edges, d, a))
            {
                continue;
            }
            var ac = points[c] - points[a];
            var ad = points[d] - points[a];
            var cd = points[d] - points[c];
            var normal = ac.Cross(ad);
            var suitable = ad.Length <= span && cd.Length <= span && normal.Unit.Y >= .8
                && normal.Length > Math.Max(ac.Length * ad.Length, Math.Max(ac.Length * cd.Length, ad.Length * cd.Length)) * .1;
            if (!suitable)
            {
                continue;
            }
            second[0] = points[a];
            second[1] = points[c];
            second[2] = points[d];
            NearbyFaces(groundFaces, second, span, nearby);
            if (GroundProjection.Overlaps(first, second) || !Empty(mesh, points, second, a, c, d, nearby))
            {
                continue;
            }
            var supported = observed((points[a] + points[c] + points[d]) / 3)
                && observed((points[a] + points[d]) * .5) && observed((points[c] + points[d]) * .5);
            if (supported)
            {
                return [a, c, d];
            }
        }
        return null;
    }

    private static void QueueBoundary(BinaryHashIndex<SurfaceEdge, BoundaryEdge> edges, int[] face,
        Queue<SurfaceEdge> pending, Dictionary<int, List<int>> incoming)
    {
        for (var corner = 0; corner < 3; corner++)
        {
            var a = face[corner];
            var b = face[(corner + 1) % 3];
            var key = Key(a, b);
            if (edges[key].Count == 1)
            {
                pending.Enqueue(key);
                AddIncoming(incoming, a, b);
            }
        }
    }

    private static void AddIncoming(Dictionary<int, List<int>> incoming, int from, int to)
    {
        if (!incoming.TryGetValue(to, out var vertices))
        {
            vertices = [];
            incoming.Add(to, vertices);
        }
        if (!vertices.Contains(from))
        {
            vertices.Add(from);
        }
    }

    private static bool Empty(Surface mesh, IList<V3> points, ReadOnlySpan<V3> triangle,
        int a, int b, int c, HashSet<int> nearby)
    {
        Span<V3> other = stackalloc V3[3];
        foreach (var index in nearby)
        {
            var face = mesh.Faces[index];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                other[corner] = points[vertex];
                var distinct = vertex != a && vertex != b && vertex != c;
                if (distinct && GroundProjection.Contains(triangle, other[corner]))
                {
                    return false;
                }
            }
            if (GroundProjection.Overlaps(triangle, other))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Available(BinaryHashIndex<SurfaceEdge, BoundaryEdge> edges, int from, int to)
    {
        if (!edges.TryGetValue(Key(from, to), out var edge))
        {
            return true;
        }
        return edge.Count == 1 && edge.From == to && edge.To == from;
    }

    private static void AddEdges(BinaryHashIndex<SurfaceEdge, BoundaryEdge> edges, int[] face, V3 normal)
    {
        for (var corner = 0; corner < 3; corner++)
        {
            var a = face[corner];
            var b = face[(corner + 1) % 3];
            var key = Key(a, b);
            edges[key] = edges.TryGetValue(key, out var edge)
                ? edge with
                {
                    Count = edge.Count + 1
                } : new BoundaryEdge(a, b, normal, 1);
        }
    }

    private static void IndexFace(Dictionary<Grid, List<int>> cells, int index, V3 a, V3 b, V3 c, double span)
    {
        var left = (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)) / span);
        var right = (int)Math.Floor(Math.Max(a.X, Math.Max(b.X, c.X)) / span);
        var bottom = (int)Math.Floor(Math.Min(a.Z, Math.Min(b.Z, c.Z)) / span);
        var top = (int)Math.Floor(Math.Max(a.Z, Math.Max(b.Z, c.Z)) / span);
        for (var z = bottom; z <= top; z++)
        {
            for (var x = left; x <= right; x++)
            {
                Add(cells, new Grid(x, 0, z), index);
            }
        }
    }

    private static void NearbyFaces(Dictionary<Grid, List<int>> cells, ReadOnlySpan<V3> triangle, double span, HashSet<int> faces)
    {
        faces.Clear();
        var first = Cell(triangle[0], span);
        var second = Cell(triangle[1], span);
        var third = Cell(triangle[2], span);
        for (var z = Math.Min(first.Z, Math.Min(second.Z, third.Z)); z <= Math.Max(first.Z, Math.Max(second.Z, third.Z)); z++)
        {
            for (var x = Math.Min(first.X, Math.Min(second.X, third.X)); x <= Math.Max(first.X, Math.Max(second.X, third.X)); x++)
            {
                if (cells.TryGetValue(new Grid(x, 0, z), out var indices))
                {
                    faces.UnionWith(indices);
                }
            }
        }
    }

    private static void Add(Dictionary<Grid, List<int>> cells, Grid key, int value)
    {
        if (!cells.TryGetValue(key, out var values))
        {
            values = [];
            cells.Add(key, values);
        }
        values.Add(value);
    }

    private static Grid Cell(V3 point, double span) => new((int)Math.Floor(point.X / span), 0, (int)Math.Floor(point.Z / span));

    private static SurfaceEdge Key(int a, int b) => new(Math.Min(a, b), Math.Max(a, b));
}
