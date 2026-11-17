using System.Buffers;
using System.Diagnostics;
using Echo.Contracts;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceHoleRepair
{
    public static int Fill(Surface mesh, double voxelSize, CancellationToken cancellation,
        Func<V3, bool>? supportsSurface = null, Func<SurfaceTriangle, bool>? acceptsTriangle = null, WorkBuffers? work = null, Action<OperationProgress>? progress = null,
        bool allowCurvedInference = false)
    {
        if (allowCurvedInference && acceptsTriangle == null)
        {
            throw new ArgumentException("Inferred patches require a visibility check.", nameof(acceptsTriangle));
        }
        using var scratch = work?.CreateScope();
        work = scratch;
        var maximumLoop = supportsSurface != null ? 8192 : allowCurvedInference ? 1024 : 256;
        var edges = new BinaryHashIndex<SurfaceEdge, BoundaryEdge>(mesh.Faces.Count, new SurfaceEdgeRecordCodec(), new BoundaryEdgeRecordCodec(), work, cancellation);
        var loops = SurfaceBoundaryLoops.Find(mesh, cancellation, work, progress, edges);
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
                if (!SupportedLoop(points, boundary.AdjacentNormal, voxelSize, supportsSurface != null, allowCurvedInference,
                    out var patchNormal, out var requiresObservation))
                {
                    continue;
                }
                var verifyObservation = requiresObservation && !allowCurvedInference;
                if (verifyObservation && supportsSurface == null)
                {
                    continue;
                }

                List<int[]>? patch;
                if (allowCurvedInference)
                {
                    // A planar ear order can cut through a curved cave wall even
                    // when its boundary projects to a simple polygon. Prefer the
                    // adjacent normals and intersection checks in the 3D order.
                    index ??= new SurfaceTriangleIndex(mesh, cancellation, work);
                    patch = CurvedHoleTriangulation.Fill(mesh, loop, edges, index, cancellation, work)
                        ?? Triangulate(loop, points, patchNormal, edges, cancellation);
                }
                else
                {
                    patch = Triangulate(loop, points, patchNormal, edges, cancellation);
                }
                if (patch == null && supportsSurface != null)
                {
                    index ??= new SurfaceTriangleIndex(mesh, cancellation, work);
                    patch = CurvedHoleTriangulation.Fill(mesh, loop, edges, index, cancellation, work);
                }
                if (patch == null)
                {
                    continue;
                }
                if (verifyObservation && !ObservedPatch(mesh, patch, supportsSurface!, cancellation))
                {
                    continue;
                }
                if (acceptsTriangle != null)
                {
                    index ??= new SurfaceTriangleIndex(mesh, cancellation, work);
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

    private static bool SupportedLoop(ReadOnlySpan<V3> points, V3 adjacentNormal, double voxelSize,
        bool allowObservedCompletion, bool allowCurvedInference, out V3 normal, out bool requiresObservation)
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
        if (allowCurvedInference && radius > voxelSize * 24)
        {
            return false;
        }
        foreach (var point in points)
        {
            var deviation = Math.Abs((point - centre).Dot(normal));
            requiresObservation |= deviation > voxelSize * .75;
            var maximumDeviation = allowCurvedInference ? Math.Min(voxelSize * 6, radius * .4)
                : allowObservedCompletion ? Math.Max(voxelSize * 4, radius * .4) : voxelSize * .75;
            if (deviation > maximumDeviation)
            {
                return false;
            }
            if (!allowObservedCompletion && !allowCurvedInference)
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
        BinaryHashIndex<SurfaceEdge, BoundaryEdge> edges, CancellationToken cancellation)
    {
        return PlanarEarTriangulation.Fill(boundary, points, normal, edges, cancellation);
    }

}
