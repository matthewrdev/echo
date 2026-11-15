using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceHoleRepair
{
    public static int Fill(Surface mesh, double voxelSize, CancellationToken cancellation,
        Func<V3, bool>? supportsSurface = null, Func<SurfaceTriangle, bool>? acceptsTriangle = null)
    {
        var maximumLoop = supportsSurface == null ? 256 : 8192;
        var edges = CollectEdges(mesh, cancellation);
        var loops = SurfaceBoundaryLoops.Find(mesh, cancellation);
        SurfaceTriangleIndex? index = null;
        var repaired = 0;
        foreach (var boundary in loops)
        {
            cancellation.ThrowIfCancellationRequested();
            var loop = boundary.Vertices;
            if (loop.Count > maximumLoop)
            {
                continue;
            }
            loop.Reverse();
            if (!SupportedLoop(mesh, loop, boundary.AdjacentNormal, voxelSize, supportsSurface != null,
                out var patchNormal, out var requiresObservation))
            {
                continue;
            }
            if (requiresObservation && supportsSurface == null)
            {
                continue;
            }

            var patch = Triangulate(mesh, loop, patchNormal, edges, cancellation);
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
            mesh.Faces.AddRange(patch);
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

    private static Dictionary<SurfaceEdge, BoundaryEdge> CollectEdges(Surface mesh, CancellationToken cancellation)
    {
        var edges = new Dictionary<SurfaceEdge, BoundaryEdge>();
        foreach (var face in mesh.Faces)
        {
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

    private static bool SupportedLoop(Surface mesh, List<int> loop, V3 adjacentNormal, double voxelSize,
        bool allowObservedCompletion, out V3 normal, out bool requiresObservation)
    {
        requiresObservation = false;
        var origin = new V3(mesh.Vertices[loop[0]]);
        var area = new V3();
        var centre = new V3();
        for (var index = 0; index < loop.Count; index++)
        {
            var point = new V3(mesh.Vertices[loop[index]]);
            var next = new V3(mesh.Vertices[loop[(index + 1) % loop.Count]]);
            area += (point - origin).Cross(next - origin);
            centre += point;
        }
        normal = area.Unit;
        centre /= loop.Count;
        if (area.Length < voxelSize * voxelSize * 1e-6 || normal.Dot(adjacentNormal) < .5)
        {
            return false;
        }

        var radius = loop.Max(vertex => (new V3(mesh.Vertices[vertex]) - centre).Length);
        foreach (var vertex in loop)
        {
            var point = new V3(mesh.Vertices[vertex]);
            var deviation = Math.Abs((point - centre).Dot(normal));
            requiresObservation |= deviation > voxelSize * .75;
            var maximumDeviation = allowObservedCompletion ? Math.Max(voxelSize * 4, radius * .4) : voxelSize * .75;
            if (deviation > maximumDeviation)
            {
                return false;
            }
            if (!allowObservedCompletion)
            {
                foreach (var other in loop)
                {
                    if ((point - new V3(mesh.Vertices[other])).Length > voxelSize * 12)
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

    private static List<int[]>? Triangulate(Surface mesh, List<int> boundary, V3 normal,
        Dictionary<SurfaceEdge, BoundaryEdge> edges, CancellationToken cancellation)
    {
        if (CrossesItself(mesh, boundary, normal))
        {
            return null;
        }
        var remaining = new List<int>(boundary);
        var result = new List<int[]>();
        while (remaining.Count > 3)
        {
            cancellation.ThrowIfCancellationRequested();
            var found = false;
            for (var index = 0; index < remaining.Count; index++)
            {
                var before = remaining[(index + remaining.Count - 1) % remaining.Count];
                var current = remaining[index];
                var after = remaining[(index + 1) % remaining.Count];
                var a = new V3(mesh.Vertices[before]);
                var b = new V3(mesh.Vertices[current]);
                var c = new V3(mesh.Vertices[after]);
                var diagonal = new SurfaceEdge(Math.Min(before, after), Math.Max(before, after));
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
                    var point = new V3(mesh.Vertices[vertex]);
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

                result.Add([before, current, after]);
                remaining.RemoveAt(index);
                found = true;
                break;
            }
            if (!found)
            {
                return null;
            }
        }

        var first = new V3(mesh.Vertices[remaining[0]]);
        var second = new V3(mesh.Vertices[remaining[1]]);
        var third = new V3(mesh.Vertices[remaining[2]]);
        if ((second - first).Cross(third - first).Dot(normal) <= 1e-12)
        {
            return null;
        }
        result.Add(remaining.ToArray());
        return result;
    }

    private static bool CrossesItself(Surface mesh, List<int> boundary, V3 normal)
    {
        for (var first = 0; first < boundary.Count; first++)
        {
            var firstEnd = (first + 1) % boundary.Count;
            var a = new V3(mesh.Vertices[boundary[first]]);
            var b = new V3(mesh.Vertices[boundary[firstEnd]]);
            for (var second = first + 2; second < boundary.Count; second++)
            {
                var secondEnd = (second + 1) % boundary.Count;
                if (secondEnd == first)
                {
                    continue;
                }
                var c = new V3(mesh.Vertices[boundary[second]]);
                var d = new V3(mesh.Vertices[boundary[secondEnd]]);
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
