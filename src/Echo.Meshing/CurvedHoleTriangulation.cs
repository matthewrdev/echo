using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

internal static class CurvedHoleTriangulation
{
    public static List<int[]>? Fill(Surface mesh, List<int> loop, BinaryHashIndex<SurfaceEdge, BoundaryEdge> edges,
        SurfaceTriangleIndex surface, CancellationToken cancellation)
    {
        var count = loop.Count;
        var before = Enumerable.Range(0, count).Select(index => (index + count - 1) % count).ToArray();
        var after = Enumerable.Range(0, count).Select(index => (index + 1) % count).ToArray();
        var points = loop.Select(vertex => new V3(mesh.Vertices[vertex])).ToArray();
        var normals = new V3[count];
        for (var index = 0; index < count; index++)
        {
            var a = loop[index];
            var b = loop[after[index]];
            normals[index] = edges[new SurfaceEdge(Math.Min(a, b), Math.Max(a, b))].Normal;
        }
        var active = Enumerable.Repeat(true, count).ToArray();
        var pending = new PriorityQueue<int, double>();
        var patch = new List<int[]>();
        var patchGeometry = new List<SurfaceTriangle>();
        var remaining = count;
        void Enqueue(int index)
        {
            var a = points[before[index]];
            var b = points[index];
            var c = points[after[index]];
            var cross = (b - a).Cross(c - a);
            var normal = cross.Unit;
            var agreement = Math.Min(normal.Dot(normals[before[index]]), normal.Dot(normals[index]));
            var quality = ((b - a).Length * (b - a).Length + (c - b).Length * (c - b).Length + (a - c).Length * (a - c).Length)
                / Math.Max(cross.Length, 1e-12);
            pending.Enqueue(index, 1 - agreement + .02 * quality);
        }
        for (var index = 0; index < count; index++)
        {
            Enqueue(index);
        }
        while (remaining > 3)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!pending.TryDequeue(out var index, out _))
            {
                return null;
            }
            if (!active[index])
            {
                continue;
            }
            var first = before[index];
            var last = after[index];
            var triangle = new SurfaceTriangle(points[first], points[index], points[last]);
            var cross = (triangle.B - triangle.A).Cross(triangle.C - triangle.A);
            var normal = cross.Unit;
            var agreement = normal.Dot(normals[first] + normals[index]);
            var diagonal = new SurfaceEdge(Math.Min(loop[first], loop[last]), Math.Max(loop[first], loop[last]));
            var unsuitable = cross.Length < 1e-10 || agreement <= .05 || edges.ContainsKey(diagonal);
            if (unsuitable || surface.Intersects(triangle))
            {
                continue;
            }
            var crosses = patchGeometry.Any(other => SurfaceTriangleIntersection.Intersects(triangle, other));
            for (var vertex = 0; vertex < count && !crosses; vertex++)
            {
                var corner = vertex == first || vertex == index || vertex == last;
                if (!active[vertex] || corner)
                {
                    continue;
                }
                crosses = SurfaceTriangleIntersection.Crosses(points[vertex], points[after[vertex]], triangle);
                var offset = points[vertex] - triangle.A;
                if (Math.Abs(offset.Dot(normal)) < 1e-8)
                {
                    var inside = (triangle.B - triangle.A).Cross(offset).Dot(normal) >= -1e-10
                        && (triangle.C - triangle.B).Cross(points[vertex] - triangle.B).Dot(normal) >= -1e-10
                        && (triangle.A - triangle.C).Cross(points[vertex] - triangle.C).Dot(normal) >= -1e-10;
                    crosses |= inside;
                }
            }
            if (crosses)
            {
                continue;
            }
            patch.Add([loop[first], loop[index], loop[last]]);
            patchGeometry.Add(triangle);
            active[index] = false;
            after[first] = last;
            before[last] = first;
            normals[first] = normal;
            remaining--;
            Enqueue(first);
            Enqueue(last);
        }
        var start = Array.FindIndex(active, value => value);
        var middle = after[start];
        var end = after[middle];
        var final = new SurfaceTriangle(points[start], points[middle], points[end]);
        var finalNormal = (final.B - final.A).Cross(final.C - final.A);
        var degenerate = finalNormal.Length < 1e-10 || finalNormal.Unit.Dot(normals[start] + normals[middle] + normals[end]) <= .05;
        if (degenerate || surface.Intersects(final) || patchGeometry.Any(other => SurfaceTriangleIntersection.Intersects(final, other)))
        {
            return null;
        }
        patch.Add([loop[start], loop[middle], loop[end]]);
        return patch;
    }
}
