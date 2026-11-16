using System.Buffers;
using System.Diagnostics;
using Echo.Contracts;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceHoleRepair
{
    public static int Fill(Surface mesh, double voxelSize, CancellationToken cancellation,
        Func<V3, bool>? supportsSurface = null, Func<SurfaceTriangle, bool>? acceptsTriangle = null, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
    {
        var maximumLoop = supportsSurface == null ? 256 : 8192;
        var edges = CollectEdges(mesh, cancellation, work, progress);
        var loops = SurfaceBoundaryLoops.Find(mesh, cancellation, work, progress);
        SurfaceTriangleIndex? index = null;
        var repaired = 0;
        var examined = 0;
        var progressClock = Stopwatch.StartNew();
        var positions = ArrayPool<V3>.Shared.Rent(maximumLoop);
        try
        {
            foreach (var boundary in loops)
            {
                cancellation.ThrowIfCancellationRequested();
                if (examined == 0 || progressClock.ElapsedMilliseconds >= 125)
                {
                    progress?.Invoke(new OperationProgress("Hole repair", $"Examined {examined:N0} enclosed boundaries; closed {repaired:N0} holes.", examined, Unit: "boundaries"));
                    progressClock.Restart();
                }
                examined++;
                var loop = boundary.Vertices;
                if (loop.Count > maximumLoop)
                {
                    continue;
                }
                loop.Reverse();
                var points = positions.AsSpan(0, loop.Count);
                for (var vertex = 0; vertex < loop.Count; vertex++)
                {
                    points[vertex] = new V3(mesh.Vertices[loop[vertex]]);
                }
                if (!SupportedLoop(points, boundary.AdjacentNormal, voxelSize, supportsSurface != null,
                    out var patchNormal, out var requiresObservation))
                {
                    continue;
                }
                if (requiresObservation && supportsSurface == null)
                {
                    continue;
                }

                var patch = Triangulate(loop, points, patchNormal, edges, cancellation);
                if (patch == null && supportsSurface != null)
                {
                    index ??= new SurfaceTriangleIndex(mesh, cancellation);
                    patch = CurvedHoleTriangulation.Fill(mesh, loop, edges, index, cancellation);
                }
                if (patch == null)
                {
                    continue;
                }
                if (requiresObservation && !ObservedPatch(mesh, patch, supportsSurface!, cancellation))
                {
                    continue;
                }
                if (acceptsTriangle != null)
                {
                    index ??= new SurfaceTriangleIndex(mesh, cancellation);
                    if (!AcceptedPatch(mesh, patch, acceptsTriangle, index, cancellation))
                    {
                        continue;
                    }
                }

                repaired++;
                foreach (var face in patch)
                {
                    mesh.Faces.Add(face);
                }
                foreach (var face in patch)
                {
                    index?.Add(new SurfaceTriangle(new V3(mesh.Vertices[face[0]]), new V3(mesh.Vertices[face[1]]), new V3(mesh.Vertices[face[2]])));
                    for (var corner = 0; corner < 3; corner++)
                    {
                        var from = face[corner];
                        var to = face[(corner + 1) % 3];
                        var key = new SurfaceEdge(Math.Min(from, to), Math.Max(from, to));
                        edges[key] = edges.TryGetValue(key, out var existing)
                            ? existing with
                            {
                                Count = existing.Count + 1
                            }
                            : new BoundaryEdge(from, to, patchNormal, 1);
                    }
                }
            }
        }
        finally
        {
            index?.Dispose();
            ArrayPool<V3>.Shared.Return(positions);
        }
        progress?.Invoke(new OperationProgress("Hole repair", $"Examined {examined:N0} enclosed boundaries; closed {repaired:N0} holes.", examined, examined, "boundaries"));
        return repaired;
    }

    private static bool AcceptedPatch(Surface mesh, List<int[]> patch, Func<SurfaceTriangle, bool> acceptsTriangle,
        SurfaceTriangleIndex index, CancellationToken cancellation)
    {
        foreach (var face in patch)
        {
            cancellation.ThrowIfCancellationRequested();
            var triangle = new SurfaceTriangle(new V3(mesh.Vertices[face[0]]),
                new V3(mesh.Vertices[face[1]]), new V3(mesh.Vertices[face[2]]));
            if (!acceptsTriangle(triangle) || index.Intersects(triangle))
            {
                return false;
            }
        }
        return true;
    }

    private static IDictionary<SurfaceEdge, BoundaryEdge> CollectEdges(Surface mesh, CancellationToken cancellation, WorkBuffers? work, Action<OperationProgress>? progress)
    {
        IDictionary<SurfaceEdge, BoundaryEdge> edges = work?.Map(new SurfaceEdgeRecordCodec(), new BoundaryEdgeRecordCodec()) ?? new Dictionary<SurfaceEdge, BoundaryEdge>();
        var completed = 0;
        foreach (var face in mesh.Faces)
        {
            if (completed++ % 10000 == 0)
            {
                progress?.Invoke(new OperationProgress("Hole edge indexing", "Indexing surface edges…", completed, mesh.Faces.Count, "triangles"));
            }
            cancellation.ThrowIfCancellationRequested();
            var a = new V3(mesh.Vertices[face[0]]);
            var normal = (new V3(mesh.Vertices[face[1]]) - a).Cross(new V3(mesh.Vertices[face[2]]) - a).Unit;
            for (var corner = 0; corner < 3; corner++)
            {
                var from = face[corner];
                var to = face[(corner + 1) % 3];
                var key = new SurfaceEdge(Math.Min(from, to), Math.Max(from, to));
                if (edges.TryGetValue(key, out var existing))
                {
                    edges[key] = existing with
                    {
                        Count = existing.Count + 1
                    };
                }
                else
                {
                    edges.Add(key, new BoundaryEdge(from, to, normal, 1));
                }
            }
        }
        return edges;
    }

    private static bool SupportedLoop(ReadOnlySpan<V3> points, V3 adjacentNormal, double voxelSize,
        bool allowObservedCompletion, out V3 normal, out bool requiresObservation)
    {
        requiresObservation = false;
        var origin = points[0];
        var area = new V3();
        var centre = new V3();
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            var next = points[(index + 1) % points.Length];
            area += (point - origin).Cross(next - origin);
            centre += point;
        }
        normal = area.Unit;
        centre /= points.Length;
        if (area.Length < voxelSize * voxelSize * 1e-6 || normal.Dot(adjacentNormal) < .5)
        {
            return false;
        }

        var radius = 0d;
        foreach (var point in points)
        {
            radius = Math.Max(radius, (point - centre).Length);
        }
        foreach (var point in points)
        {
            var deviation = Math.Abs((point - centre).Dot(normal));
            requiresObservation |= deviation > voxelSize * .75;
            var maximumDeviation = allowObservedCompletion ? Math.Max(voxelSize * 4, radius * .4) : voxelSize * .75;
            if (deviation > maximumDeviation)
            {
                return false;
            }
            if (!allowObservedCompletion)
            {
                foreach (var other in points)
                {
                    if ((point - other).Length > voxelSize * 12)
                    {
                        return false;
                    }
                }
            }
        }
        requiresObservation |= radius > voxelSize * 6;

        return true;
    }

    private static bool ObservedPatch(Surface mesh, List<int[]> patch, Func<V3, bool> supportsSurface, CancellationToken cancellation)
    {
        foreach (var face in patch)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = new V3(mesh.Vertices[face[0]]);
            var b = new V3(mesh.Vertices[face[1]]);
            var c = new V3(mesh.Vertices[face[2]]);
            var observed = supportsSurface((a + b + c) / 3) && supportsSurface((a + b) * .5)
                && supportsSurface((b + c) * .5) && supportsSurface((c + a) * .5);
            if (!observed)
            {
                return false;
            }
        }
        return true;
    }

    private static List<int[]>? Triangulate(List<int> boundary, ReadOnlySpan<V3> points, V3 normal,
        IDictionary<SurfaceEdge, BoundaryEdge> edges, CancellationToken cancellation)
    {
        if (CrossesItself(points, normal))
        {
            return null;
        }
        var remaining = Enumerable.Range(0, boundary.Count).ToList();
        var result = new List<int[]>(boundary.Count - 2);
        while (remaining.Count > 3)
        {
            cancellation.ThrowIfCancellationRequested();
            var found = false;
            for (var index = 0; index < remaining.Count; index++)
            {
                var before = remaining[(index + remaining.Count - 1) % remaining.Count];
                var current = remaining[index];
                var after = remaining[(index + 1) % remaining.Count];
                var a = points[before];
                var b = points[current];
                var c = points[after];
                var diagonal = new SurfaceEdge(Math.Min(boundary[before], boundary[after]), Math.Max(boundary[before], boundary[after]));
                if ((b - a).Cross(c - a).Dot(normal) <= 1e-12 || edges.ContainsKey(diagonal))
                {
                    continue;
                }

                var containsVertex = false;
                foreach (var vertex in remaining)
                {
                    var isCorner = vertex == before || vertex == current || vertex == after;
                    if (isCorner)
                    {
                        continue;
                    }
                    var point = points[vertex];
                    var inside = (b - a).Cross(point - a).Dot(normal) >= -1e-12
                        && (c - b).Cross(point - b).Dot(normal) >= -1e-12
                        && (a - c).Cross(point - c).Dot(normal) >= -1e-12;
                    if (inside)
                    {
                        containsVertex = true;
                        break;
                    }
                }
                if (containsVertex)
                {
                    continue;
                }

                result.Add([boundary[before], boundary[current], boundary[after]]);
                remaining.RemoveAt(index);
                found = true;
                break;
            }
            if (!found)
            {
                return null;
            }
        }

        var first = points[remaining[0]];
        var second = points[remaining[1]];
        var third = points[remaining[2]];
        if ((second - first).Cross(third - first).Dot(normal) <= 1e-12)
        {
            return null;
        }
        result.Add([boundary[remaining[0]], boundary[remaining[1]], boundary[remaining[2]]]);
        return result;
    }

    private static bool CrossesItself(ReadOnlySpan<V3> points, V3 normal)
    {
        for (var first = 0; first < points.Length; first++)
        {
            var firstEnd = (first + 1) % points.Length;
            var a = points[first];
            var b = points[firstEnd];
            for (var second = first + 2; second < points.Length; second++)
            {
                var secondEnd = (second + 1) % points.Length;
                if (secondEnd == first)
                {
                    continue;
                }
                var c = points[second];
                var d = points[secondEnd];
                var oppositeFirst = (b - a).Cross(c - a).Dot(normal) * (b - a).Cross(d - a).Dot(normal) < -1e-12;
                var oppositeSecond = (d - c).Cross(a - c).Dot(normal) * (d - c).Cross(b - c).Dot(normal) < -1e-12;
                if (oppositeFirst && oppositeSecond)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
