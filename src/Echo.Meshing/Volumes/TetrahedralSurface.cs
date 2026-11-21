using Echo.Compute;
using Echo.Geometry;
using Echo.Meshing.Geometry;
using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Volumes;

internal static class TetrahedralSurface
{
    public static Surface Extract(IReadOnlyDictionary<Grid, float> samples, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<string>? progress = null, Action<OperationProgress>? operationProgress = null, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        ReadOnlySpan<Grid> offsets = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)];
        // Every cube uses the same body diagonal. Shared cube faces therefore
        // have identical subdivisions, including ambiguous saddle configurations.
        ReadOnlySpan<int> tetrahedra = [0, 5, 1, 6, 0, 1, 2, 6, 0, 2, 3, 6,
            0, 3, 7, 6, 0, 7, 4, 6, 0, 4, 5, 6];
        var mesh = work?.Surface() ?? new Surface();
        var reader = new SurfaceGeometryReader(mesh);
        var writer = new SurfaceGeometryWriter(mesh);
        using var scratch = work?.CreateScope();
        var vertices = new BinaryHashIndex<VolumeEdge, int>(Math.Min(samples.Count, 262144), new VolumeEdgeRecordCodec(), new IntRecordCodec(), scratch, cancellation);
        Span<Grid> corners = stackalloc Grid[8];
        Span<float> values = stackalloc float[8];
        operationProgress?.Invoke(new OperationProgress("Surface extraction", "Extracting the zero crossing surface…", 0, samples.Count, "lattice cells"));
        var completed = 0;
        using var acceleration = SparseVolumeCompute.Open(compute, backend, samples, cancellation, out var mask, VolumeCellBatch.Capacity);
        using var batches = new VolumeCellReader(samples.Keys, acceleration, backend, mask, cancellation);
        while (batches.Read() is { } batch)
        {
            var keys = batch.Keys;
            var activeCells = batch.Records;
            var count = batch.Count;
            var accelerated = batch.Accelerated;
            var activeCount = batch.ActiveCount;
            completed += count;
            for (var cell = 0; cell < (accelerated ? activeCount : count); cell++)
            {
                var key = accelerated ? new Grid(activeCells[cell * 12], activeCells[cell * 12 + 1], activeCells[cell * 12 + 2]) : keys[cell];
                cancellation.ThrowIfCancellationRequested();
                var complete = true;
                var negative = false;
                var positive = false;
                for (var index = 0; index < 8; index++)
                {
                    var offset = offsets[index];
                    corners[index] = key.Add(offset.X, offset.Y, offset.Z);
                    var found = accelerated;
                    if (accelerated)
                    {
                        values[index] = BitConverter.Int32BitsToSingle(activeCells[cell * 12 + 4 + index]);
                    }
                    else
                    {
                        found = samples.TryGetValue(corners[index], out values[index]);
                    }

                    if (!found || !float.IsFinite(values[index]))
                    {
                        complete = false;
                        break;
                    }

                    negative |= values[index] < 0;
                    positive |= values[index] >= 0;
                }

                var crossesSurface = complete && negative && positive;
                if (!crossesSurface)
                {
                    continue;
                }

                for (var index = 0; index < tetrahedra.Length; index += 4)
                {
                    Append(mesh, reader, writer, vertices, corners, values, tetrahedra.Slice(index, 4), voxelSize);
                }
            }

            var message = $"Extracting surface: {completed:N0}/{samples.Count:N0} lattice cells; {mesh.Faces.Count:N0} triangles…";
            if (operationProgress != null)
            {
                operationProgress(new OperationProgress("Surface extraction", message, completed, samples.Count, "lattice cells"));
            }
            else
            {
                progress?.Invoke(message);
            }
        }

        operationProgress?.Invoke(new OperationProgress("Surface extraction", $"Extracted {mesh.Faces.Count:N0} triangles.", samples.Count, samples.Count, "lattice cells"));
        return mesh;
    }

    private static void Append(Surface mesh, SurfaceGeometryReader reader, SurfaceGeometryWriter writer, BinaryHashIndex<VolumeEdge, int> vertices,
        ReadOnlySpan<Grid> corners, ReadOnlySpan<float> values, ReadOnlySpan<int> tetrahedron, double voxelSize)
    {
        Span<int> negative = stackalloc int[4];
        Span<int> positive = stackalloc int[4];
        var below = 0;
        var above = 0;
        foreach (var corner in tetrahedron)
        {
            if (values[corner] < 0)
            {
                negative[below++] = corner;
            }
            else
            {
                positive[above++] = corner;
            }
        }

        if (below == 0 || above == 0)
        {
            return;
        }

        var inside = corners[negative[0]];
        var outside = corners[positive[0]];
        var direction = new V3(outside.X - inside.X, outside.Y - inside.Y, outside.Z - inside.Z);
        Span<int> polygon = stackalloc int[4];
        if (below == 1 || above == 1)
        {
            var single = below == 1 ? negative[0] : positive[0];
            var opposite = below == 1 ? positive : negative;
            for (var index = 0; index < 3; index++)
            {
                polygon[index] = Vertex(mesh, writer, vertices, corners[single], corners[opposite[index]],
                    values[single], values[opposite[index]], voxelSize);
            }

            AddTriangle(reader, writer, polygon[0], polygon[1], polygon[2], direction, voxelSize);
            return;
        }

        for (var index = 0; index < 4; index++)
        {
            var a = negative[index < 2 ? 0 : 1];
            var b = positive[index is 0 or 3 ? 0 : 1];
            polygon[index] = Vertex(mesh, writer, vertices, corners[a], corners[b], values[a], values[b], voxelSize);
        }

        AddTriangle(reader, writer, polygon[0], polygon[1], polygon[2], direction, voxelSize);
        AddTriangle(reader, writer, polygon[0], polygon[2], polygon[3], direction, voxelSize);
    }

    private static int Vertex(Surface mesh, SurfaceGeometryWriter writer, BinaryHashIndex<VolumeEdge, int> vertices,
        Grid a, Grid b, float valueA, float valueB, double voxelSize)
    {
        // Zero crossings at grid vertices must weld across all incident edges.
        var edge = valueA == 0 ? new VolumeEdge(a, a) : valueB == 0 ? new VolumeEdge(b, b) : OrderedEdge(a, b);
        if (vertices.TryGetValue(edge, out var existing))
        {
            return existing;
        }

        var fraction = (double)valueA / (valueA - valueB);
        var index = mesh.Vertices.Count;
        writer.AddVertex(new V3((a.X + (b.X - a.X) * fraction) * voxelSize,
            (a.Y + (b.Y - a.Y) * fraction) * voxelSize, (a.Z + (b.Z - a.Z) * fraction) * voxelSize));
        vertices.Add(edge, index);
        return index;
    }

    private static VolumeEdge OrderedEdge(Grid a, Grid b)
    {
        var order = a.X.CompareTo(b.X);
        if (order == 0)
        {
            order = a.Y.CompareTo(b.Y);
        }

        if (order == 0)
        {
            order = a.Z.CompareTo(b.Z);
        }

        return order <= 0 ? new VolumeEdge(a, b) : new VolumeEdge(b, a);
    }

    private static void AddTriangle(SurfaceGeometryReader reader, SurfaceGeometryWriter writer, int a, int b, int c, V3 direction, double voxelSize)
    {
        var origin = reader.Vertex(a);
        var normal = (reader.Vertex(b) - origin).Cross(reader.Vertex(c) - origin);
        if (normal.Length <= voxelSize * voxelSize * 1e-12)
        {
            return;
        }

        writer.AddFace(normal.Dot(direction) >= 0 ? new SurfaceTriangleVertices(a, b, c) : new SurfaceTriangleVertices(a, c, b));
    }
}
